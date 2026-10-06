using System;
using System.Collections.Generic;
using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

/// <summary>
/// 역할·목적: 드론 작업/경로 의도·우선순위·현장 예약·배정 공개에 대한 NUnit EditMode 회귀 검증.
/// 입력·검사: 현장/재고/수행자/용량과 경로 응답을 준비해 실제 그룹/세 ECB 경계를 검사한다. 실물 운송/이동이나 경로 평가 Producer를 실행하지 않는다.
/// 수명: EcsWorldTestFixture가 각 사례의 독립 World를 준비하고 종료 시 해제한다.
/// </summary>
public class DroneTaskSchedulingTests : EcsWorldTestFixture
{
    private GameSimulationGroup _simulation;
    private CommandGroup _command;
    private DroneDecisionGroup _decision;
    private DroneReservationGroup _reservation;
    private DroneExecutionGroup _execution;
    private DroneStateApplyGroup _apply;
    private BuildingSimulationGroup _building;
    private BuildingStateApplyGroup _buildingApply;
    private SimulationCommitGroup _commit;
    private double _elapsedTime;

    [SetUp]
    public override void SetUp()
    {
        base.SetUp();
        CreateGameplayPrefabDatabases();
        ItemConfigInitSystem.InitializeItemRegistry(_entityManager);
        _elapsedTime = 0;
        _simulation = _world.GetOrCreateSystemManaged<GameSimulationGroup>();
        _command = _world.GetOrCreateSystemManaged<CommandGroup>();
        _decision = _world.GetOrCreateSystemManaged<DroneDecisionGroup>();
        _reservation = _world.GetOrCreateSystemManaged<DroneReservationGroup>();
        _execution = _world.GetOrCreateSystemManaged<DroneExecutionGroup>();
        _apply = _world.GetOrCreateSystemManaged<DroneStateApplyGroup>();
        _building = _world.GetOrCreateSystemManaged<BuildingSimulationGroup>();
        _buildingApply = _world.GetOrCreateSystemManaged<BuildingStateApplyGroup>();
        _building.AddSystemToUpdateList(_buildingApply);
        _building.AddSystemToUpdateList(_world.GetOrCreateSystemManaged<EndBuildingEntityCommandBufferSystem>());
        var drone = _world.GetOrCreateSystemManaged<DroneSimulationGroup>();
        _commit = _world.GetOrCreateSystemManaged<SimulationCommitGroup>();
        _commit.AddSystemToUpdateList(_world.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>());
        var synchronization = _world.GetOrCreateSystemManaged<SynchronizationGroup>();
        _simulation.AddSystemToUpdateList(_command);
        _simulation.AddSystemToUpdateList(_building);
        _simulation.AddSystemToUpdateList(drone);
        _simulation.AddSystemToUpdateList(_commit);
        _simulation.AddSystemToUpdateList(synchronization);
        drone.AddSystemToUpdateList(_decision);
        drone.AddSystemToUpdateList(_reservation);
        drone.AddSystemToUpdateList(_execution);
        drone.AddSystemToUpdateList(_apply);

        _command.AddSystemToUpdateList(_world.GetOrCreateSystem<BuildingPlacementCommandSystem>());
        _command.AddSystemToUpdateList(_world.GetOrCreateSystemManaged<EndCommandEntityCommandBufferSystem>());
        _decision.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneTaskDecisionSystem>());
        _reservation.AddSystemToUpdateList(_world.GetOrCreateSystem<ConstructionSupplyReservationSystem>());
        _execution.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneTaskExecutionSystem>());
        _apply.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneTaskLifecycleApplySystem>());
        _apply.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneTaskAssignmentPublishSystem>());
        synchronization.AddSystemToUpdateList(_world.GetOrCreateSystem<BuildingSpatialSyncSystem>());
        synchronization.AddSystemToUpdateList(_world.GetOrCreateSystem<ItemSpatialSyncSystem>());
        synchronization.AddSystemToUpdateList(_world.GetOrCreateSystem<ResourceSpatialSyncSystem>());

        _command.SortSystems();
        _decision.SortSystems();
        _reservation.SortSystems();
        _execution.SortSystems();
        _apply.SortSystems();
        _buildingApply.SortSystems();
        _building.SortSystems();
        drone.SortSystems();
        _commit.SortSystems();
        synchronization.SortSystems();
        _simulation.SortSystems();
    }

    [Test]
    public void ApprovedPlacement_PublishesTasksAtEndSimulation_AndSchedulesThemOnTheNextDecision()
    {
        Entity config = _entityManager.CreateEntity(typeof(BuildingConfig));
        _entityManager.AddBuffer<BuildingConfigElement>(config).Add(
            new BuildingConfigElement(BuildingTypeEnum.Storage, 1f, 4, true, default, new int2(1, 1)));
        var materials = _entityManager.AddBuffer<BuildingConstructionMaterialElement>(config);
        materials.Add(new BuildingConstructionMaterialElement(BuildingTypeEnum.Storage, ItemTypeEnum.Iron, 3));
        materials.Add(new BuildingConstructionMaterialElement(BuildingTypeEnum.Storage, ItemTypeEnum.Copper, 2));
        Entity request = _entityManager.CreateEntity(typeof(BuildingPlacementRequest));
        _entityManager.SetComponentData(request, new BuildingPlacementRequest());
        _entityManager.AddBuffer<PlacementRequestCandidateElement>(request).Add(
            new PlacementRequestCandidateElement(BuildingTypeEnum.Storage, new int2(1, 1), new int2(8, 4)));

        CreateStorage(new int2(1, 1), 1, ItemTypeEnum.Iron, 3);
        CreateWorker(3);

        // 배치 Command는 현장만 생성한다. Decision은 생성 의도만 만들고 Execution이 ECB에 기록한다.
        _command.Update();

        Assert.IsFalse(_entityManager.Exists(request));
        Entity site = SingleEntity<ConstructionSite>();
        Assert.AreEqual(0, QueryEntities<DroneLogisticsTask>().Length);
        _decision.Update();
        _reservation.Update();
        Assert.AreEqual(0, QueryEntities<DroneLogisticsTask>().Length);
        Assert.AreEqual(0, QueryEntities<DroneRouteEvaluationRequest>().Length);
        Entity sequenceEntity = SingleEntity<DroneTaskSequence>();
        Assert.AreEqual(1UL, _entityManager.GetComponentData<DroneTaskSequence>(sequenceEntity).NextValue,
            "Decision은 작업 생성 순번을 발급하지 않는다.");
        _execution.Update();
        Assert.AreEqual(3UL, _entityManager.GetComponentData<DroneTaskSequence>(sequenceEntity).NextValue);
        _execution.Update();
        Assert.AreEqual(3UL, _entityManager.GetComponentData<DroneTaskSequence>(sequenceEntity).NextValue,
            "소비한 생성 의도는 순번도 다시 발급하지 않는다.");
        Assert.AreEqual(0, QueryEntities<DroneLogisticsTask>().Length,
            "Execution이 두 번 실행되어도 작업은 EndSimulation 이전에 공개되지 않는다.");
        ApplyDroneAndCommit();

        Entity[] tasks = QueryEntities<DroneLogisticsTask>();
        Assert.AreEqual(2, tasks.Length, "소비한 생성 의도를 다시 실행하여 작업을 중복 생성하지 않는다.");
        var itemTypes = new HashSet<ItemTypeEnum>();
        var sequences = new HashSet<ulong>();
        foreach (Entity entity in tasks)
        {
            DroneLogisticsTask task = _entityManager.GetComponentData<DroneLogisticsTask>(entity);
            Assert.AreEqual(site, task.Target);
            Assert.AreEqual(DroneLogisticsTaskKindEnum.ConstructionSupply, task.Kind);
            Assert.AreEqual(DroneLogisticsTaskStateEnum.Open, task.State);
            Assert.Greater(task.CreationSequence, 0UL);
            Assert.IsTrue(sequences.Add(task.CreationSequence));
            itemTypes.Add(task.ItemType);
        }
        CollectionAssert.AreEquivalent(new[] { ItemTypeEnum.Iron, ItemTypeEnum.Copper }, itemTypes);
        Assert.AreEqual(0, QueryEntities<DroneRouteEvaluationRequest>().Length,
            "이번 StateApply에 공개된 작업은 이전 Decision에서 배정하지 않는다.");

        // 다음 Decision의 경로 의도도 Execution이 기록하고 EndSimulation에 공개한다.
        _decision.Update();
        _world.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>().Update();
        Assert.AreEqual(0, QueryEntities<DroneRouteEvaluationRequest>().Length);
        _reservation.Update();
        _execution.Update();
        _execution.Update();
        Assert.AreEqual(0, QueryEntities<DroneRouteEvaluationRequest>().Length);
        ApplyDroneAndCommit();
        Assert.AreEqual(1, QueryEntities<DroneRouteEvaluationRequest>().Length);
        Assert.AreEqual(2, QueryEntities<DroneLogisticsTask>().Length, "다음 StateApply가 공급 작업을 중복 생성하지 않는다.");
        AssertNoAssignments();
    }

    [Test]
    public void GroundRecovery_CreatesOneTaskPerItem_AndDoesNotSweepOrdinaryWorldOrBeltItems()
    {
        CreateSite(new int2(4, 4), 1, ItemTypeEnum.Iron, 0);
        Entity ground = CreateItem(ItemTypeEnum.Copper, Entity.Null, new int2(4, 4));
        Entity unrelated = CreateItem(ItemTypeEnum.Iron, Entity.Null, new int2(40, 40));
        Entity beltItem = CreateItem(ItemTypeEnum.Iron, Entity.Null, new int2(41, 40));
        _entityManager.AddComponentData(beltItem, new BeltMovementState(0.5f));
        Assert.AreEqual(0, QueryEntities<DroneCapacityState>().Length,
            "공통 적재 능력과 수행자가 없어도 회수 작업 생성은 진행한다.");

        Tick();
        Tick();

        Entity[] tasks = QueryEntities<DroneLogisticsTask>();
        Assert.AreEqual(1, tasks.Length);
        DroneLogisticsTask task = _entityManager.GetComponentData<DroneLogisticsTask>(tasks[0]);
        Assert.AreEqual(DroneLogisticsTaskKindEnum.WorldItemRecovery, task.Kind);
        Assert.AreEqual(DroneRecoveryReasonEnum.SiteClearance, task.RecoveryReason);
        Assert.AreEqual(ground, task.Target);
        Assert.AreNotEqual(unrelated, task.Target);
        Assert.AreNotEqual(beltItem, task.Target);
        Assert.AreEqual(Entity.Null, _entityManager.GetComponentData<ItemOwnership>(ground).Owner);
        AssertNoAssignments();
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void RecoveryTargetChangesAfterExecution_NextDecisionRejectsStaleTaskAndRoute(
        bool afterRouteCommand, bool becomesStored)
    {
        CreateSite(new int2(4, 4), 1, ItemTypeEnum.Iron, 0);
        Entity item = CreateItem(ItemTypeEnum.Copper, Entity.Null, new int2(4, 4));
        Entity storage = CreateStorage(new int2(10, 4), 1, ItemTypeEnum.Copper, 0);
        CreateWorker(1);
        if (afterRouteCommand)
        {
            Tick();
            Assert.AreEqual(1, QueryEntities<DroneLogisticsTask>().Length);
        }

        _command.Update();
        _decision.Update();
        _reservation.Update();
        _execution.Update();
        Assert.AreEqual(0, QueryEntities<DroneRouteEvaluationRequest>().Length,
            "이번 Execution의 경로 요청은 아직 EndSimulation에 공개되지 않았다.");

        // 이번 StateApply에서 벨트 입고 또는 이동이 적용된 상황만 준비한다.
        // 드론의 실제 수집/인계 기능을 실행한 것으로 간주하지 않는다.
        if (becomesStored)
        {
            _entityManager.SetComponentData(item, new ItemOwnership(storage));
            _entityManager.SetComponentData(item, new GridPosition(new int2(10, 4)));
            _entityManager.GetBuffer<StoredItemElement>(storage).Add(
                new StoredItemElement(item, ItemTypeEnum.Copper, 0));
        }
        else
        {
            _entityManager.SetComponentData(item, new GridPosition(new int2(40, 40)));
        }

        ApplyDroneAndCommit();
        if (afterRouteCommand)
        {
            Assert.AreEqual(1, QueryEntities<DroneRouteEvaluationRequest>().Length);
            AnswerRoutes(route => 14f);
        }
        else
        {
            Assert.AreEqual(1, QueryEntities<DroneLogisticsTask>().Length,
                "Execution에 기록한 생성 명령은 기존 대상 상태를 기준으로 EndSimulation에 공개된다.");
        }
        AssertNoAssignments();

        Tick();

        AssertNoAssignments();
        Assert.AreEqual(0, QueryEntities<DroneLogisticsTask>().Length);
        Assert.AreEqual(0, QueryEntities<DroneRouteEvaluationRequest>().Length);
        Assert.AreEqual(becomesStored ? storage : Entity.Null,
            _entityManager.GetComponentData<ItemOwnership>(item).Owner);
        Assert.AreEqual(becomesStored ? 1 : 0, _entityManager.GetBuffer<StoredItemElement>(storage).Length);
    }

    [Test]
    public void NoRouteResult_KeepsWorkPendingWithoutDuplicateRequestsOrMaterialChanges()
    {
        Entity site = CreateSite(new int2(10, 0), 1, ItemTypeEnum.Iron, 2);
        CreateSupplyTask(site, ItemTypeEnum.Iron, 1);
        Entity source = CreateStorage(new int2(1, 0), 1, ItemTypeEnum.Iron, 2);
        Entity worker = CreateWorker(2);

        Tick();
        Entity[] requests = QueryEntities<DroneRouteEvaluationRequest>();
        Assert.AreEqual(1, requests.Length);
        DroneRouteEvaluationRequest route = _entityManager.GetComponentData<DroneRouteEvaluationRequest>(requests[0]);
        Assert.AreEqual(worker, route.Worker);
        Assert.AreEqual(source, route.Source);
        Assert.AreEqual(site, route.Destination);
        Assert.AreEqual(DroneRouteKindEnum.ViaSource, route.Kind);
        Assert.AreEqual(Entity.Null, route.Assignment);
        Assert.Greater(route.EvaluationRevision, 0U);

        Tick();
        CollectionAssert.AreEquivalent(requests, QueryEntities<DroneRouteEvaluationRequest>());
        AssertNoAssignments();
        Assert.AreEqual(0, Requirement(site).ReservedQuantity);
        Assert.AreEqual(2, _entityManager.GetBuffer<StoredItemElement>(source).Length);
        Assert.AreEqual(0, _entityManager.GetBuffer<StoredItemElement>(worker).Length);
    }

    [TestCase("result-revision")]
    [TestCase("worker-observation")]
    [TestCase("destination-position")]
    public void StaleRouteSnapshot_IsNotUsedForAssignment(string stalePart)
    {
        Entity site = CreateSite(new int2(10, 0), 1, ItemTypeEnum.Iron, 2);
        CreateSupplyTask(site, ItemTypeEnum.Iron, 1);
        CreateStorage(new int2(1, 0), 1, ItemTypeEnum.Iron, 2);
        Entity worker = CreateWorker(2);
        Tick();
        Entity requestEntity = SingleEntity<DroneRouteEvaluationRequest>();
        DroneRouteEvaluationRequest request = _entityManager.GetComponentData<DroneRouteEvaluationRequest>(requestEntity);
        PublishRoute(requestEntity, DroneRouteEvaluationStatusEnum.Reachable, 12f);
        if (stalePart == "result-revision")
        {
            _entityManager.SetComponentData(requestEntity, new DroneRouteEvaluationResult
            {
                EvaluationRevision = request.EvaluationRevision + 1,
                Status = DroneRouteEvaluationStatusEnum.Reachable,
                TotalDistance = 12f
            });
        }
        else if (stalePart == "worker-observation")
        {
            DroneWorkerObservation observation = _entityManager.GetComponentData<DroneWorkerObservation>(worker);
            observation.Revision++;
            _entityManager.SetComponentData(worker, observation);
        }
        else
        {
            _entityManager.SetComponentData(site, new GridPosition(new int2(11, 0)));
        }

        Tick();

        AssertNoAssignments();
        Assert.AreEqual(0, Requirement(site).ReservedQuantity);
    }

    [TestCase(DroneRouteEvaluationStatusEnum.None, 0f)]
    [TestCase(DroneRouteEvaluationStatusEnum.Unreachable, 0f)]
    [TestCase(DroneRouteEvaluationStatusEnum.Reachable, -1f)]
    [TestCase(DroneRouteEvaluationStatusEnum.Reachable, float.NaN)]
    [TestCase(DroneRouteEvaluationStatusEnum.Reachable, float.PositiveInfinity)]
    public void UnknownUnreachableOrInvalidDistance_DoesNotBecomeFeasible(
        DroneRouteEvaluationStatusEnum status, float distance)
    {
        Entity site = CreateSite(new int2(10, 0), 1, ItemTypeEnum.Iron, 1);
        CreateSupplyTask(site, ItemTypeEnum.Iron, 1);
        CreateStorage(new int2(1, 0), 1, ItemTypeEnum.Iron, 1);
        CreateWorker(1);
        Tick();
        PublishRoute(SingleEntity<DroneRouteEvaluationRequest>(), status, distance);

        Tick();

        AssertNoAssignments();
        Assert.AreEqual(0, Requirement(site).ReservedQuantity);
    }

    [TestCase("zero-capacity")]
    [TestCase("missing-capacity")]
    [TestCase("zero-observation")]
    [TestCase("unavailable")]
    [TestCase("missing-cargo-contract")]
    public void IncompleteOrUnavailableWorker_DoesNotReceiveWork(string invalidPart)
    {
        Entity site = CreateSite(new int2(10, 0), 1, ItemTypeEnum.Iron, 1);
        CreateSupplyTask(site, ItemTypeEnum.Iron, 1);
        CreateStorage(new int2(1, 0), 1, ItemTypeEnum.Iron, 1);
        Entity worker = CreateWorker(1);
        if (invalidPart == "zero-capacity")
        {
            _entityManager.SetComponentData(SingleEntity<DroneCapacityState>(),
                new DroneCapacityState { CarryingCapacity = 0 });
        }
        else if (invalidPart == "missing-capacity")
        {
            _entityManager.DestroyEntity(SingleEntity<DroneCapacityState>());
        }
        else if (invalidPart == "missing-cargo-contract")
        {
            _entityManager.RemoveComponent<DroneCargoState>(worker);
        }
        else
        {
            DroneWorkerObservation observation = _entityManager.GetComponentData<DroneWorkerObservation>(worker);
            observation.Revision = invalidPart == "zero-observation" ? 0U : 1U;
            observation.CanAcceptTask = invalidPart != "unavailable";
            _entityManager.SetComponentData(worker, observation);
        }

        Tick();

        AssertNoAssignments();
        Assert.AreEqual(0, QueryEntities<DroneRouteEvaluationRequest>().Length);
    }

    [Test]
    public void OldestCurrentlyInfeasibleWork_DoesNotBlockYoungerFeasibleWork()
    {
        Entity noStock = CreateSite(new int2(10, 0), 1, ItemTypeEnum.Copper, 1);
        CreateSupplyTask(noStock, ItemTypeEnum.Copper, 1);
        Entity unknownRoute = CreateSite(new int2(20, 0), 2, ItemTypeEnum.Iron, 1);
        CreateSupplyTask(unknownRoute, ItemTypeEnum.Iron, 2);
        Entity reachable = CreateSite(new int2(30, 0), 3, ItemTypeEnum.Iron, 1);
        Entity chosenTask = CreateSupplyTask(reachable, ItemTypeEnum.Iron, 3);
        CreateStorage(new int2(1, 0), 1, ItemTypeEnum.Iron, 1);
        CreateWorker(1);
        Tick();
        AnswerRoutes(route => route.Destination == reachable ? 30f : (float?)null);

        Tick();

        DroneTaskAssignment assignment = SingleAssignment();
        Assert.AreEqual(chosenTask, assignment.Task);
        Assert.AreEqual(reachable, assignment.Destination);
        Assert.AreEqual(0, Requirement(noStock).ReservedQuantity);
        Assert.AreEqual(0, Requirement(unknownRoute).ReservedQuantity);
        Assert.AreEqual(1, Requirement(reachable).ReservedQuantity);
    }

    [Test]
    public void InitialAssignment_PrefersEarlierSiteOverCloserSite()
    {
        Entity older = CreateSite(new int2(100, 0), 1, ItemTypeEnum.Iron, 1);
        Entity olderTask = CreateSupplyTask(older, ItemTypeEnum.Iron, 1);
        Entity younger = CreateSite(new int2(2, 0), 2, ItemTypeEnum.Iron, 1);
        CreateSupplyTask(younger, ItemTypeEnum.Iron, 2);
        CreateStorage(new int2(1, 0), 1, ItemTypeEnum.Iron, 2);
        CreateWorker(1);
        Tick();
        AnswerRoutes(route => route.Destination == older ? 100f : 2f);

        Tick();

        Assert.AreEqual(olderTask, SingleAssignment().Task);
        Assert.AreEqual(1, Requirement(older).ReservedQuantity);
        Assert.AreEqual(0, Requirement(younger).ReservedQuantity);
    }

    [Test]
    public void SourceSelection_UsesCompleteRouteDistance_ThenPlacementStamp()
    {
        Entity site = CreateSite(new int2(20, 0), 1, ItemTypeEnum.Iron, 1);
        CreateSupplyTask(site, ItemTypeEnum.Iron, 1);
        Entity visuallyNearest = CreateStorage(new int2(1, 0), 1, ItemTypeEnum.Iron, 1);
        Entity tiedYounger = CreateStorage(new int2(5, 0), 3, ItemTypeEnum.Iron, 1);
        Entity tiedOlder = CreateStorage(new int2(6, 0), 2, ItemTypeEnum.Iron, 1);
        CreateWorker(1);
        Tick();
        AnswerRoutes(route => route.Source == visuallyNearest ? 40f : 25f);

        Tick();

        Assert.AreEqual(tiedOlder, SingleAssignment().Source);
        Assert.AreNotEqual(tiedYounger, SingleAssignment().Source);
    }

    [Test]
    public void MultipleWorkers_ReserveOnlySiteRemainder_WithoutReservingOrMovingSourceItems()
    {
        Entity site = CreateSite(new int2(10, 0), 1, ItemTypeEnum.Iron, 5);
        CreateSupplyTask(site, ItemTypeEnum.Iron, 1);
        Entity source = CreateStorage(new int2(1, 0), 1, ItemTypeEnum.Iron, 3);
        Entity firstWorker = CreateWorker(3);
        Entity secondWorker = CreateWorker(3);
        Entity[] originalItems = StoredEntities(source);
        Tick();
        AnswerRoutes(route => 12f);

        Tick();

        Entity[] assignments = QueryEntities<DroneTaskAssignment>();
        Assert.AreEqual(2, assignments.Length, "공급원 실물은 선점하지 않아 두 드론 모두 현장 잔량에 배정된다.");
        int assignedQuantity = 0;
        int reservedQuantity = 0;
        foreach (Entity entity in assignments)
        {
            DroneTaskAssignment assignment = _entityManager.GetComponentData<DroneTaskAssignment>(entity);
            ConstructionSupplyReservation reservation = _entityManager.GetComponentData<ConstructionSupplyReservation>(entity);
            assignedQuantity += assignment.AssignedQuantity;
            reservedQuantity += reservation.RemainingQuantity;
            Assert.AreEqual(site, reservation.Site);
            Assert.AreEqual(assignment.Revision, reservation.AssignmentRevision);
            Assert.Greater(assignment.Revision, 0U);
            Assert.AreEqual(entity, _entityManager.GetComponentData<DroneWorkerAssignment>(assignment.Worker).Assignment);
        }
        Assert.AreEqual(5, assignedQuantity);
        Assert.AreEqual(5, reservedQuantity);
        Assert.AreEqual(5, Requirement(site).ReservedQuantity);
        Assert.AreEqual(0, Requirement(site).DeliveredQuantity);
        CollectionAssert.AreEquivalent(originalItems, StoredEntities(source));
        foreach (Entity item in originalItems)
        {
            Assert.AreEqual(source, _entityManager.GetComponentData<ItemOwnership>(item).Owner);
        }
        Assert.AreEqual(0, _entityManager.GetBuffer<StoredItemElement>(firstWorker).Length);
        Assert.AreEqual(0, _entityManager.GetBuffer<StoredItemElement>(secondWorker).Length);
        Tick();
        Assert.AreEqual(2, QueryEntities<DroneTaskAssignment>().Length, "활성 배정에 다시 배정하지 않는다.");
        Assert.AreEqual(5, Requirement(site).ReservedQuantity);
    }

    [Test]
    public void CommonCapacityIncrease_AffectsNewAssignments_AndPreservesExistingAssignmentReservationAndCargo()
    {
        Entity site = CreateSite(new int2(10, 0), 1, ItemTypeEnum.Iron, 10);
        CreateSupplyTask(site, ItemTypeEnum.Iron, 1);
        Entity source = CreateStorage(new int2(1, 0), 1, ItemTypeEnum.Iron, 10);
        Entity firstWorker = CreateWorker(2);
        Tick();
        AnswerRoutes(route => 12f);
        Tick();

        Entity firstAssignmentEntity = SingleEntity<DroneTaskAssignment>();
        DroneTaskAssignment firstAssignment = SingleAssignment();
        Assert.AreEqual(2, firstAssignment.AssignedQuantity);
        Assert.AreEqual(2, Requirement(site).ReservedQuantity);

        // 기존 배정이 수집을 마친 상태를 준비한다. 실제 인계 기능을 실행한 것은 아니다.
        // 연구에 따른 공통량 변경이 이미 운반 중인 실물과 예약에 영향을 주지 않는지 확인한다.
        Entity[] cargo = new Entity[2];
        for (int i = 0; i < cargo.Length; i++)
        {
            var sourceItems = _entityManager.GetBuffer<StoredItemElement>(source);
            StoredItemElement entry = sourceItems[0];
            sourceItems.RemoveAt(0);
            cargo[i] = entry.ItemEntity;
            _entityManager.GetBuffer<StoredItemElement>(firstWorker).Add(entry);
            _entityManager.SetComponentData(entry.ItemEntity, new ItemOwnership(firstWorker));
        }
        _entityManager.SetComponentData(firstWorker, new DroneCargoState { Origin = DroneCargoOriginEnum.Supply });
        firstAssignment.State = DroneTaskAssignmentStateEnum.MovingToDestination;
        firstAssignment.NextAction = DroneActionKindEnum.SupplyConstructionSite;
        _entityManager.SetComponentData(firstAssignmentEntity, firstAssignment);

        _entityManager.SetComponentData(SingleEntity<DroneCapacityState>(),
            new DroneCapacityState { CarryingCapacity = 5 });
        Entity secondWorker = CreateWorker(5);
        Tick();
        AnswerRoutes(route => 12f);
        Tick();

        Entity secondAssignmentEntity = _entityManager.GetComponentData<DroneWorkerAssignment>(secondWorker).Assignment;
        Assert.AreNotEqual(Entity.Null, secondAssignmentEntity);
        DroneTaskAssignment secondAssignment = _entityManager.GetComponentData<DroneTaskAssignment>(secondAssignmentEntity);
        Assert.AreEqual(5, secondAssignment.AssignedQuantity);
        Assert.AreEqual(5, _entityManager.GetComponentData<ConstructionSupplyReservation>(secondAssignmentEntity).RemainingQuantity);
        Assert.AreEqual(2, QueryEntities<DroneTaskAssignment>().Length);
        Assert.AreEqual(7, Requirement(site).ReservedQuantity);
        Assert.AreEqual(0, Requirement(site).DeliveredQuantity);

        DroneTaskAssignment unchangedAssignment = _entityManager.GetComponentData<DroneTaskAssignment>(firstAssignmentEntity);
        Assert.AreEqual(2, unchangedAssignment.AssignedQuantity);
        Assert.AreEqual(firstAssignment.Revision, unchangedAssignment.Revision);
        Assert.AreEqual(DroneTaskAssignmentStateEnum.MovingToDestination, unchangedAssignment.State);
        Assert.AreEqual(2, _entityManager.GetComponentData<ConstructionSupplyReservation>(firstAssignmentEntity).RemainingQuantity);
        Assert.AreEqual(firstAssignmentEntity, _entityManager.GetComponentData<DroneWorkerAssignment>(firstWorker).Assignment);
        CollectionAssert.AreEquivalent(cargo, StoredEntities(firstWorker));
        foreach (Entity item in cargo)
        {
            Assert.AreEqual(firstWorker, _entityManager.GetComponentData<ItemOwnership>(item).Owner);
        }
        Assert.AreEqual(DroneCargoOriginEnum.Supply, _entityManager.GetComponentData<DroneCargoState>(firstWorker).Origin);
        Assert.AreEqual(0, _entityManager.GetBuffer<StoredItemElement>(secondWorker).Length);
        Assert.AreEqual(8, _entityManager.GetBuffer<StoredItemElement>(source).Length);
    }

    [Test]
    public void ApprovedAssignment_IsPublishedOnlyAtEndSimulation()
    {
        Entity site = CreateSite(new int2(10, 0), 1, ItemTypeEnum.Iron, 1);
        CreateSupplyTask(site, ItemTypeEnum.Iron, 1);
        CreateStorage(new int2(1, 0), 1, ItemTypeEnum.Iron, 1);
        Entity worker = CreateWorker(1);
        Tick();
        AnswerRoutes(route => 10f);

        _command.Update();
        _decision.Update();
        _reservation.Update();
        AssertNoAssignments();
        Assert.AreEqual(Entity.Null, _entityManager.GetComponentData<DroneWorkerAssignment>(worker).Assignment);
        using var publicationQuery = _entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<DroneTaskPendingPublicationElement>());
        Entity publicationOwner = publicationQuery.GetSingletonEntity();
        var pending = _entityManager.GetBuffer<DroneTaskPendingPublicationElement>(publicationOwner, true);
        Assert.AreEqual(1, pending.Length);
        Assert.AreEqual(1, pending[0].CommittedQuantity);
        Assert.IsFalse(pending[0].PublicationQueued);
        _execution.Update();
        AssertNoAssignments();
        _apply.Update();
        AssertNoAssignments();
        Assert.AreEqual(Entity.Null, _entityManager.GetComponentData<DroneWorkerAssignment>(worker).Assignment);
        pending = _entityManager.GetBuffer<DroneTaskPendingPublicationElement>(publicationOwner, true);
        Assert.IsTrue(pending[0].PublicationQueued, "공개 기록과 실제 ECB 공개를 구분한다.");
        _commit.Update();

        Entity assignmentEntity = SingleEntity<DroneTaskAssignment>();
        Assert.AreEqual(assignmentEntity, _entityManager.GetComponentData<DroneWorkerAssignment>(worker).Assignment);
        Assert.AreEqual(DroneTaskAssignmentStateEnum.MovingToSource, SingleAssignment().State);
        Tick();
        Assert.AreEqual(0, _entityManager.GetBuffer<DroneTaskPendingPublicationElement>(publicationOwner).Length);
        Assert.AreEqual(1, Requirement(site).ReservedQuantity, "공개 대기 기록 정리는 활성 개별 예약량을 해제하지 않는다.");
    }

    [Test]
    public void RecoveryAssignment_IsExclusivePerWorldItem_AndKeepsActualItemInTheWorld()
    {
        CreateSite(new int2(4, 4), 1, ItemTypeEnum.Iron, 0);
        Entity item = CreateItem(ItemTypeEnum.Copper, Entity.Null, new int2(4, 4));
        Entity storage = CreateStorage(new int2(10, 4), 1, ItemTypeEnum.Copper, 0);
        CreateWorker(3);
        CreateWorker(3);
        Tick();
        Assert.AreEqual(0, QueryEntities<DroneRouteEvaluationRequest>().Length,
            "회수 작업을 만든 틱에는 경로 요청을 배정하지 않는다.");
        Tick();
        AnswerRoutes(route => 14f);

        Tick();

        DroneTaskAssignment assignment = SingleAssignment();
        Assert.AreEqual(item, assignment.Source);
        Assert.AreEqual(storage, assignment.Destination);
        Assert.AreEqual(1, assignment.AssignedQuantity);
        ConstructionSupplyReservation reservation = _entityManager.GetComponentData<ConstructionSupplyReservation>(SingleEntity<DroneTaskAssignment>());
        Assert.AreEqual(Entity.Null, reservation.Site);
        Assert.AreEqual(0, reservation.RemainingQuantity);
        Assert.AreEqual(Entity.Null, _entityManager.GetComponentData<ItemOwnership>(item).Owner);
        Assert.AreEqual(new int2(4, 4), _entityManager.GetComponentData<GridPosition>(item).Value);
        Assert.AreEqual(0, _entityManager.GetBuffer<StoredItemElement>(storage).Length);
        Tick();
        Assert.AreEqual(1, QueryEntities<DroneTaskAssignment>().Length);
    }

    [Test]
    public void RecoveryAndSupply_CompeteByTaskCreationOrder()
    {
        Entity supplySite = CreateSite(new int2(20, 0), 1, ItemTypeEnum.Iron, 1);
        CreateSupplyTask(supplySite, ItemTypeEnum.Iron, 2);
        CreateSite(new int2(4, 4), 2, ItemTypeEnum.Copper, 1);
        Entity recoveryItem = CreateItem(ItemTypeEnum.Copper, Entity.Null, new int2(4, 4));
        Entity recoveryTask = CreateTask(new DroneLogisticsTask
        {
            Kind = DroneLogisticsTaskKindEnum.WorldItemRecovery,
            State = DroneLogisticsTaskStateEnum.Open,
            Target = recoveryItem,
            ItemType = ItemTypeEnum.Copper,
            CreationSequence = 1,
            RecoveryReason = DroneRecoveryReasonEnum.SiteClearance
        });
        CreateStorage(new int2(1, 0), 1, ItemTypeEnum.Iron, 1);
        CreateWorker(1);
        Tick();
        AnswerRoutes(route => route.Source == recoveryItem ? 40f : 20f);

        Tick();

        Assert.AreEqual(recoveryTask, SingleAssignment().Task);
        Assert.AreEqual(0, Requirement(supplySite).ReservedQuantity);
    }

    [Test]
    public void MixedWorkPriority_ComparesRecoveryAgainstTheSupplyHeadChosenByPlacementStamp()
    {
        // 공급 A(seq 1, stamp 20)는 공급 B(seq 3, stamp 10)보다 뒤다.
        // 따라서 공급 선두 B와 회수(seq 2)를 비교하여 회수가 먼저 배정된다.
        Entity laterPlacedSite = CreateSite(new int2(20, 0), 20, ItemTypeEnum.Iron, 1);
        CreateSupplyTask(laterPlacedSite, ItemTypeEnum.Iron, 1);
        Entity earlierPlacedSite = CreateSite(new int2(30, 0), 10, ItemTypeEnum.Iron, 1);
        CreateSupplyTask(earlierPlacedSite, ItemTypeEnum.Iron, 3);
        CreateSite(new int2(4, 4), 30, ItemTypeEnum.Copper, 1);
        Entity item = CreateItem(ItemTypeEnum.Copper, Entity.Null, new int2(4, 4));
        Entity recoveryTask = CreateTask(new DroneLogisticsTask
        {
            Kind = DroneLogisticsTaskKindEnum.WorldItemRecovery,
            State = DroneLogisticsTaskStateEnum.Open,
            Target = item,
            ItemType = ItemTypeEnum.Copper,
            CreationSequence = 2,
            RecoveryReason = DroneRecoveryReasonEnum.SiteClearance
        });
        CreateStorage(new int2(1, 0), 1, ItemTypeEnum.Iron, 2);
        CreateWorker(1);
        Tick();
        AnswerRoutes(route => route.Source == item ? 40f : 20f);

        Tick();

        Assert.AreEqual(recoveryTask, SingleAssignment().Task);
        Assert.AreEqual(0, Requirement(laterPlacedSite).ReservedQuantity);
        Assert.AreEqual(0, Requirement(earlierPlacedSite).ReservedQuantity);
    }

    [Test]
    public void SiteSatisfiedBeforePublication_RollsBackTheWholePendingReservation()
    {
        Entity site = CreateSite(new int2(10, 0), 1, ItemTypeEnum.Iron, 5);
        CreateSupplyTask(site, ItemTypeEnum.Iron, 1);
        CreateStorage(new int2(1, 0), 1, ItemTypeEnum.Iron, 5);
        Entity worker = CreateWorker(5);
        Tick();
        AnswerRoutes(route => 10f);
        _command.Update();
        _decision.Update();
        _reservation.Update();
        Assert.AreEqual(5, Requirement(site).ReservedQuantity);
        AssertNoAssignments();

        // Reservation 이후 다른 반영자가 필요량을 모두 충족한 상태를 준비한다.
        // 실제 인계 기능을 호출하거나 성공한 것으로 검증하지 않는다.
        ConstructionMaterialRequirementElement requirement = Requirement(site);
        requirement.DeliveredQuantity = requirement.RequiredQuantity;
        var requirements = _entityManager.GetBuffer<ConstructionMaterialRequirementElement>(site);
        requirements[0] = requirement;
        _execution.Update();
        ApplyDroneAndCommit();

        AssertNoAssignments();
        Assert.AreEqual(0, Requirement(site).ReservedQuantity);
        Assert.AreEqual(Entity.Null, _entityManager.GetComponentData<DroneWorkerAssignment>(worker).Assignment);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CommonCapacityUnavailableBeforePublication_RollsBackTheWholePendingReservation(bool removeCapacity)
    {
        Entity site = CreateSite(new int2(10, 0), 1, ItemTypeEnum.Iron, 5);
        CreateSupplyTask(site, ItemTypeEnum.Iron, 1);
        Entity source = CreateStorage(new int2(1, 0), 1, ItemTypeEnum.Iron, 5);
        Entity worker = CreateWorker(5);
        Tick();
        AnswerRoutes(route => 10f);
        _command.Update();
        _decision.Update();
        _reservation.Update();
        Assert.AreEqual(5, Requirement(site).ReservedQuantity);
        AssertNoAssignments();

        Entity capacity = SingleEntity<DroneCapacityState>();
        if (removeCapacity)
        {
            _entityManager.DestroyEntity(capacity);
        }
        else
        {
            _entityManager.SetComponentData(capacity, new DroneCapacityState { CarryingCapacity = 0 });
        }
        _execution.Update();
        ApplyDroneAndCommit();

        AssertNoAssignments();
        Assert.AreEqual(0, Requirement(site).ReservedQuantity);
        Assert.AreEqual(Entity.Null, _entityManager.GetComponentData<DroneWorkerAssignment>(worker).Assignment);
        Assert.AreEqual(0, _entityManager.GetBuffer<StoredItemElement>(worker).Length);
        Assert.AreEqual(5, _entityManager.GetBuffer<StoredItemElement>(source).Length);
        Assert.AreEqual(1, QueryEntities<DroneLogisticsTask>().Length,
            "공통 능력의 일시 부재는 미충족 공급 작업을 제거하지 않는다.");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void InvalidExistingAssignment_ReleasesReservationInReservation_AndChangesLifecycleOnlyInStateApply(bool removeSource)
    {
        Entity site = CreateSite(new int2(10, 0), 1, ItemTypeEnum.Iron, 2);
        Entity task = CreateSupplyTask(site, ItemTypeEnum.Iron, 1);
        Entity source = CreateStorage(new int2(1, 0), 1, ItemTypeEnum.Iron, 2);
        Entity worker = CreateWorker(2);
        Tick();
        AnswerRoutes(route => 10f);
        Tick();
        Entity oldAssignment = SingleEntity<DroneTaskAssignment>();
        Assert.AreEqual(2, Requirement(site).ReservedQuantity);
        if (removeSource)
        {
            _entityManager.DestroyEntity(source);
        }
        else
        {
            ConstructionSite siteState = _entityManager.GetComponentData<ConstructionSite>(site);
            siteState.Flags |= ConstructionSiteFlags.Cancelled;
            _entityManager.SetComponentData(site, siteState);
        }

        _decision.Update();
        // ECB만 재생해도 작업/배정이 남아야 Decision의 삭제 기록까지 배제할 수 있다.
        _world.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>().Update();
        Assert.AreEqual(2, Requirement(site).ReservedQuantity,
            "Decision은 다른 도메인의 현장 예약량을 변경하지 않는다.");
        Assert.AreEqual(2, _entityManager.GetComponentData<ConstructionSupplyReservation>(oldAssignment).RemainingQuantity);
        Assert.AreEqual(DroneLogisticsTaskStateEnum.Open, _entityManager.GetComponentData<DroneLogisticsTask>(task).State);
        Assert.AreEqual(DroneTaskAssignmentStateEnum.MovingToSource,
            _entityManager.GetComponentData<DroneTaskAssignment>(oldAssignment).State);
        Assert.AreEqual(oldAssignment, _entityManager.GetComponentData<DroneWorkerAssignment>(worker).Assignment);
        Assert.IsTrue(_entityManager.Exists(oldAssignment));

        _reservation.Update();
        Assert.AreEqual(0, Requirement(site).ReservedQuantity);
        Assert.AreEqual(0, _entityManager.GetComponentData<ConstructionSupplyReservation>(oldAssignment).RemainingQuantity);
        Assert.AreEqual(DroneLogisticsTaskStateEnum.Open, _entityManager.GetComponentData<DroneLogisticsTask>(task).State);
        Assert.AreEqual(DroneTaskAssignmentStateEnum.MovingToSource,
            _entityManager.GetComponentData<DroneTaskAssignment>(oldAssignment).State);
        Assert.AreEqual(oldAssignment, _entityManager.GetComponentData<DroneWorkerAssignment>(worker).Assignment);
        Assert.IsTrue(_entityManager.Exists(oldAssignment));

        _execution.Update();
        Assert.AreEqual(DroneLogisticsTaskStateEnum.Open, _entityManager.GetComponentData<DroneLogisticsTask>(task).State);
        Assert.AreEqual(DroneTaskAssignmentStateEnum.MovingToSource,
            _entityManager.GetComponentData<DroneTaskAssignment>(oldAssignment).State);
        Assert.AreEqual(oldAssignment, _entityManager.GetComponentData<DroneWorkerAssignment>(worker).Assignment);
        ApplyDroneAndCommit();
        Assert.IsFalse(_entityManager.Exists(oldAssignment));
        Assert.AreEqual(0, Requirement(site).ReservedQuantity);
        Assert.AreEqual(Entity.Null, _entityManager.GetComponentData<DroneWorkerAssignment>(worker).Assignment);
        Assert.AreEqual(0, _entityManager.GetBuffer<StoredItemElement>(worker).Length);
        AssertNoAssignments();
    }

    private void Tick()
    {
        // 그룹과 두 ECB 경계를 한 틱씩 통과시켜 작업 생성 틱과 다음 배정 판단 틱을 구분한다.
        _elapsedTime += 0.1;
        Simulation.SetDeltaTime(0.1f, _elapsedTime);
        _simulation.Update();
    }

    [Test]
    public void ClosedClearanceTask_RemovesRouteWhileItemAndStorageStillExist()
    {
        Entity site = CreateSite(new int2(4, 4), 1, ItemTypeEnum.Iron, 0);
        Entity item = CreateItem(ItemTypeEnum.Iron, Entity.Null, new int2(4, 4));
        Entity storage = CreateStorage(new int2(10, 4), 1, ItemTypeEnum.Copper, 0);
        CreateWorker(1);
        Tick();
        Tick();
        Assert.AreEqual(1, QueryEntities<DroneRouteEvaluationRequest>().Length);

        _entityManager.DestroyEntity(site);
        Tick();

        Assert.AreEqual(0, QueryEntities<DroneRouteEvaluationRequest>().Length);
        Assert.AreEqual(0, QueryEntities<DroneLogisticsTask>().Length);
        Assert.IsTrue(_entityManager.Exists(item));
        Assert.IsTrue(_entityManager.Exists(storage));
        Assert.IsTrue(_entityManager.GetComponentData<ItemOwnership>(item).IsWorldItem);
        AssertNoAssignments();
    }

    [Test]
    public void Lifecycle_RechecksCapturedTaskClosureAfterBuildingOwnershipReturnsItemToWorld()
    {
        // 이전에 기록된 무효화 의도도 건물에서 확정한 현재 소유권으로 최종 재검사한다.
        _buildingApply.AddSystemToUpdateList(_world.GetOrCreateSystem<ItemOwnershipApplySystem>());
        _buildingApply.SortSystems();
        CreateSite(new int2(4, 4), 1, ItemTypeEnum.Iron, 0);
        Entity storage = CreateStorage(new int2(10, 4), 1, ItemTypeEnum.Iron, 1);
        Entity item = StoredEntities(storage)[0];
        _entityManager.AddComponent<DisableRendering>(item);
        Entity task = CreateTask(new DroneLogisticsTask
        {
            Kind = DroneLogisticsTaskKindEnum.WorldItemRecovery,
            State = DroneLogisticsTaskStateEnum.Open,
            Target = item,
            ItemType = ItemTypeEnum.Iron,
            CreationSequence = 1,
            RecoveryReason = DroneRecoveryReasonEnum.SiteClearance
        });

        _decision.Update();

        using (EntityQuery decisionsQuery = _entityManager.CreateEntityQuery(
                   ComponentType.ReadOnly<DroneTaskInvalidationDecisionElement>()))
        {
            var decisions = _entityManager.GetBuffer<DroneTaskInvalidationDecisionElement>(
                decisionsQuery.GetSingletonEntity(), true);
            Assert.AreEqual(1, decisions.Length, "보관된 아이템의 회수 작업에 종료 의도가 필요하다.");
            Assert.AreEqual(DroneTaskInvalidationDecisionKindEnum.CloseTask, decisions[0].Kind);
            Assert.AreEqual(task, decisions[0].Target);
        }
        Assert.AreEqual(storage, _entityManager.GetComponentData<ItemOwnership>(item).Owner);
        Assert.AreEqual(DroneLogisticsTaskStateEnum.Open,
            _entityManager.GetComponentData<DroneLogisticsTask>(task).State);

        // 보관 반영자가 버퍼 제거/위치 갱신 후 소유권 요청을 발행한 상태를 준비한다.
        // 실제 소유권과 렌더 변경은 건물 그룹과 건물 종료 ECB가 처리한다.
        _entityManager.GetBuffer<StoredItemElement>(storage).Clear();
        _entityManager.SetComponentData(item, new GridPosition(new int2(4, 4)));
        _entityManager.SetComponentData(item, LocalTransform.FromPosition(new float3(4f, 4f, 0f)));
        _entityManager.AddComponentData(item, new TransferOwnershipRequest(Entity.Null));
        _entityManager.SetComponentEnabled<TransferOwnershipRequest>(item, true);

        _building.Update();
        ApplyDroneAndCommit();

        Assert.IsTrue(_entityManager.GetComponentData<ItemOwnership>(item).IsWorldItem);
        Assert.IsFalse(_entityManager.IsComponentEnabled<TransferOwnershipRequest>(item));
        Assert.IsFalse(_entityManager.HasComponent<DisableRendering>(item));
        Assert.IsTrue(_entityManager.Exists(task),
            "Lifecycle은 Ownership이 반영한 월드 소유권을 읽어 유효해진 작업을 삭제하지 않아야 한다.");
        Assert.AreEqual(DroneLogisticsTaskStateEnum.Open,
            _entityManager.GetComponentData<DroneLogisticsTask>(task).State);
        Assert.AreEqual(0, _entityManager.GetBuffer<StoredItemElement>(storage).Length);
        AssertNoAssignments();
    }

    private void ApplyDroneAndCommit()
    {
        _apply.Update();
        _commit.Update();
    }

    private Entity CreateSite(int2 position, ulong stamp, ItemTypeEnum itemType, int quantity)
    {
        Entity entity = _entityManager.CreateEntity();
        _entityManager.AddComponentData(entity, new ConstructionSite(BuildingTypeEnum.Storage));
        _entityManager.AddComponentData(entity, new BuildingType(BuildingTypeEnum.ConstructionSite));
        _entityManager.AddComponentData(entity, new GridPosition(position));
        _entityManager.AddComponentData(entity, new BuildingFootprint(new int2(1, 1)));
        _entityManager.AddComponentData(entity, new Direction(DirectionEnum.Up));
        _entityManager.AddComponentData(entity, new PlacementStamp(stamp, 0));
        _entityManager.AddBuffer<StoredItemElement>(entity);
        _entityManager.AddBuffer<ConstructionMaterialRequirementElement>(entity).Add(
            new ConstructionMaterialRequirementElement(itemType, quantity));
        return entity;
    }

    private Entity CreateSupplyTask(Entity site, ItemTypeEnum type, ulong sequence)
    {
        return CreateTask(new DroneLogisticsTask
        {
            Kind = DroneLogisticsTaskKindEnum.ConstructionSupply,
            State = DroneLogisticsTaskStateEnum.Open,
            Target = site,
            ItemType = type,
            CreationSequence = sequence
        });
    }

    private Entity CreateTask(DroneLogisticsTask task)
    {
        Entity entity = _entityManager.CreateEntity(typeof(DroneLogisticsTask));
        _entityManager.SetComponentData(entity, task);
        return entity;
    }

    private Entity CreateStorage(int2 position, ulong stamp, ItemTypeEnum type, int quantity)
    {
        Entity entity = _entityManager.CreateEntity();
        _entityManager.AddComponentData(entity, new BuildingType(BuildingTypeEnum.Storage));
        _entityManager.AddComponentData(entity, new GridPosition(position));
        _entityManager.AddComponentData(entity, new BuildingFootprint(new int2(1, 1)));
        _entityManager.AddComponentData(entity, new Direction(DirectionEnum.Up));
        _entityManager.AddComponentData(entity, new PlacementStamp(stamp, 0));
        _entityManager.AddComponentData(entity, new Storage(2));
        _entityManager.AddComponentData(entity, new StorageFilter(StorageFilterMode.AllowAll));
        _entityManager.AddBuffer<StoredItemElement>(entity);
        for (int i = 0; i < quantity; i++)
        {
            Entity item = CreateItem(type, entity, position);
            _entityManager.GetBuffer<StoredItemElement>(entity).Add(new StoredItemElement(item, type, 0));
        }
        return entity;
    }

    private Entity CreateItem(ItemTypeEnum type, Entity owner, int2 position)
    {
        Entity item = _entityManager.CreateEntity();
        _entityManager.AddComponentData(item, new ItemIdentity(type));
        _entityManager.AddComponentData(item, new ItemOwnership(owner));
        _entityManager.AddComponentData(item, new GridPosition(position));
        _entityManager.AddComponentData(item, LocalTransform.FromPosition(new float3(position.x, position.y, 0f)));
        return item;
    }

    private Entity CreateWorker(int commonCapacity)
    {
        // 수행자별 능력이 아니라 테스트 World의 공통 능력 상태를 최초 한 번 준비한다.
        // 능력 변경 검증은 이 helper 대신 공통 상태를 명시적으로 수정한다.
        using (EntityQuery capacityQuery = _entityManager.CreateEntityQuery(
                   ComponentType.ReadOnly<DroneCapacityState>()))
        {
            if (capacityQuery.IsEmptyIgnoreFilter)
            {
                Entity capacityEntity = _entityManager.CreateEntity(typeof(DroneCapacityState));
                _entityManager.SetComponentData(capacityEntity,
                    new DroneCapacityState { CarryingCapacity = commonCapacity });
            }
            else
            {
                Assert.AreEqual(commonCapacity,
                    _entityManager.GetComponentData<DroneCapacityState>(capacityQuery.GetSingletonEntity()).CarryingCapacity,
                    "동일 World의 수행자는 같은 공통 적재 능력을 사용해야 한다.");
            }
        }
        Entity entity = _entityManager.CreateEntity();
        _entityManager.AddComponent<DroneWorker>(entity);
        _entityManager.AddComponentData(entity, new DroneWorkerObservation
        {
            Position = float3.zero,
            Revision = 1,
            CanAcceptTask = true
        });
        _entityManager.AddComponentData(entity, new DroneWorkerAssignment());
        _entityManager.AddComponentData(entity, new DroneCargoState());
        _entityManager.AddBuffer<StoredItemElement>(entity);
        return entity;
    }

    private void AnswerRoutes(Func<DroneRouteEvaluationRequest, float?> distance)
    {
        foreach (Entity entity in QueryEntities<DroneRouteEvaluationRequest>())
        {
            DroneRouteEvaluationRequest route = _entityManager.GetComponentData<DroneRouteEvaluationRequest>(entity);
            float? value = distance(route);
            if (value.HasValue)
            {
                PublishRoute(entity, DroneRouteEvaluationStatusEnum.Reachable, value.Value);
            }
        }
    }

    private void PublishRoute(Entity entity, DroneRouteEvaluationStatusEnum status, float distance)
    {
        DroneRouteEvaluationRequest request = _entityManager.GetComponentData<DroneRouteEvaluationRequest>(entity);
        var result = new DroneRouteEvaluationResult
        {
            EvaluationRevision = request.EvaluationRevision,
            Status = status,
            TotalDistance = distance
        };
        if (_entityManager.HasComponent<DroneRouteEvaluationResult>(entity))
        {
            _entityManager.SetComponentData(entity, result);
            return;
        }
        _entityManager.AddComponentData(entity, result);
    }

    private ConstructionMaterialRequirementElement Requirement(Entity site)
    {
        return _entityManager.GetBuffer<ConstructionMaterialRequirementElement>(site)[0];
    }

    private Entity[] StoredEntities(Entity owner)
    {
        DynamicBuffer<StoredItemElement> stored = _entityManager.GetBuffer<StoredItemElement>(owner);
        var items = new Entity[stored.Length];
        for (int i = 0; i < items.Length; i++)
        {
            items[i] = stored[i].ItemEntity;
        }
        return items;
    }

    private DroneTaskAssignment SingleAssignment()
    {
        return _entityManager.GetComponentData<DroneTaskAssignment>(SingleEntity<DroneTaskAssignment>());
    }

    private Entity SingleEntity<T>() where T : unmanaged, IComponentData
    {
        Entity[] entities = QueryEntities<T>();
        Assert.AreEqual(1, entities.Length, typeof(T).Name);
        return entities[0];
    }

    private Entity[] QueryEntities<T>() where T : unmanaged, IComponentData
    {
        using EntityQuery query = _entityManager.CreateEntityQuery(ComponentType.ReadOnly<T>());
        using NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
        return entities.ToArray();
    }

    private void AssertNoAssignments()
    {
        Assert.AreEqual(0, QueryEntities<DroneTaskAssignment>().Length);
    }
}
