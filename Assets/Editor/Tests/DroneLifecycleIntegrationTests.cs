using System;
using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

/// <summary>
/// 역할·목적: 정렬된 건물→드론 그룹과 세 ECB 경계의 취소·철거·완공 이후 드론 수명주기 EditMode 회귀 검증.
/// 입력·검사: 경로·관측·행동 신호는 테스트 입력이고 인계/예약 전이는 제품 시스템이 반영한다. 완공 후 예약 0 적재품은 방어 fixture이며 정상 유효 예약을 남긴 선완공/실제 이동의 증거는 아니다.
/// 수명: EcsWorldTestFixture가 각 사례의 독립 World를 준비하고 종료 시 해제한다.
/// </summary>
public class DroneLifecycleIntegrationTests : EcsWorldTestFixture
{
    private GameSimulationGroup _simulation;
    private CommandGroup _command;
    private DroneDecisionGroup _decision;
    private DroneReservationGroup _reservation;
    private DroneExecutionGroup _execution;
    private DroneStateApplyGroup _apply;
    private BuildingSimulationGroup _building;
    private BuildingStateApplyGroup _buildingApply;
    private DroneSimulationGroup _drone;
    private SimulationCommitGroup _commit;
    private SynchronizationGroup _synchronization;
    private double _elapsed;

    [SetUp]
    public override void SetUp()
    {
        base.SetUp();
        CreateGameplayPrefabDatabases();
        ItemConfigInitSystem.InitializeItemRegistry(_entityManager);
        Entity capacity = _entityManager.CreateEntity(typeof(DroneCapacityState));
        _entityManager.SetComponentData(capacity, new DroneCapacityState { CarryingCapacity = 2 });
        _simulation = _world.GetOrCreateSystemManaged<GameSimulationGroup>();
        _command = _world.GetOrCreateSystemManaged<CommandGroup>();
        _decision = _world.GetOrCreateSystemManaged<DroneDecisionGroup>();
        _reservation = _world.GetOrCreateSystemManaged<DroneReservationGroup>();
        _execution = _world.GetOrCreateSystemManaged<DroneExecutionGroup>();
        _apply = _world.GetOrCreateSystemManaged<DroneStateApplyGroup>();
        _building = _world.GetOrCreateSystemManaged<BuildingSimulationGroup>();
        var buildingDecision = _world.GetOrCreateSystemManaged<BuildingDecisionGroup>();
        var buildingReservation = _world.GetOrCreateSystemManaged<BuildingReservationGroup>();
        var buildingExecution = _world.GetOrCreateSystemManaged<BuildingExecutionGroup>();
        _buildingApply = _world.GetOrCreateSystemManaged<BuildingStateApplyGroup>();
        _drone = _world.GetOrCreateSystemManaged<DroneSimulationGroup>();
        _commit = _world.GetOrCreateSystemManaged<SimulationCommitGroup>();
        _synchronization = _world.GetOrCreateSystemManaged<SynchronizationGroup>();
        _simulation.AddSystemToUpdateList(_command);
        _simulation.AddSystemToUpdateList(_building);
        _simulation.AddSystemToUpdateList(_drone);
        _simulation.AddSystemToUpdateList(_commit);
        _simulation.AddSystemToUpdateList(_synchronization);
        _building.AddSystemToUpdateList(buildingDecision);
        _building.AddSystemToUpdateList(buildingReservation);
        _building.AddSystemToUpdateList(buildingExecution);
        _building.AddSystemToUpdateList(_buildingApply);
        _building.AddSystemToUpdateList(_world.GetOrCreateSystemManaged<EndBuildingEntityCommandBufferSystem>());
        _drone.AddSystemToUpdateList(_decision);
        _drone.AddSystemToUpdateList(_reservation);
        _drone.AddSystemToUpdateList(_execution);
        _drone.AddSystemToUpdateList(_apply);
        _commit.AddSystemToUpdateList(_world.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>());

        _command.AddSystemToUpdateList(_world.GetOrCreateSystem<ConstructionCancelCommandSystem>());
        _command.AddSystemToUpdateList(_world.GetOrCreateSystem<BuildingDemolitionCommandSystem>());
        _command.AddSystemToUpdateList(_world.GetOrCreateSystemManaged<EndCommandEntityCommandBufferSystem>());
        _decision.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneTaskDecisionSystem>());
        _decision.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneItemTransferDecisionSystem>());
        buildingDecision.AddSystemToUpdateList(_world.GetOrCreateSystem<ItemSpawnAdmissionDecisionSystem>());
        _reservation.AddSystemToUpdateList(_world.GetOrCreateSystem<ConstructionSupplyReservationSystem>());
        _execution.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneTaskExecutionSystem>());
        _execution.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneItemTransferExecutionSystem>());
        _buildingApply.AddSystemToUpdateList(_world.GetOrCreateSystem<ConstructionLifecycleApplySystem>());
        _apply.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneTaskAssignmentPublishSystem>());
        _apply.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneTaskLifecycleApplySystem>());
        _buildingApply.AddSystemToUpdateList(_world.GetOrCreateSystem<BuildingLifecycleApplySystem>());
        _buildingApply.AddSystemToUpdateList(_world.GetOrCreateSystem<ItemLifecycleApplySystem>());
        _buildingApply.AddSystemToUpdateList(_world.GetOrCreateSystem<ItemOwnershipApplySystem>());
        _buildingApply.AddSystemToUpdateList(_world.GetOrCreateSystem<BuildingItemStorageApplySystem>());
        _synchronization.AddSystemToUpdateList(_world.GetOrCreateSystem<BuildingSpatialSyncSystem>());
        _synchronization.AddSystemToUpdateList(_world.GetOrCreateSystem<ItemSpatialSyncSystem>());
        _synchronization.AddSystemToUpdateList(_world.GetOrCreateSystem<BeltSpatialSyncSystem>());
        _synchronization.AddSystemToUpdateList(_world.GetOrCreateSystem<ResourceSpatialSyncSystem>());
        _command.SortSystems();
        _decision.SortSystems();
        _reservation.SortSystems();
        _execution.SortSystems();
        _apply.SortSystems();
        buildingDecision.SortSystems();
        buildingReservation.SortSystems();
        buildingExecution.SortSystems();
        _buildingApply.SortSystems();
        _building.SortSystems();
        _drone.SortSystems();
        _commit.SortSystems();
        _synchronization.SortSystems();
        _simulation.SortSystems();
    }

    [Test]
    public void BuildingDeposit_IsCollectedByAnExistingAssignmentInTheSameSortedTick()
    {
        Entity source = CreateStorage(new int2(1, 0), 1, 1);
        Entity site = CreateSite(new int2(10, 0), 1, 2);
        Entity worker = CreateWorker();
        Entity assignment = WaitForInitialAssignment(worker, site, source);
        Entity previousStock = Stored(source)[0];
        using (var ecb = new EntityCommandBuffer(Allocator.TempJob))
        {
            Assert.IsTrue(ItemOwnershipApplySystem.TryTransferItem(_entityManager, previousStock,
                ItemTypeEnum.Iron, source, Entity.Null, 0, new int2(50, 0), ecb));
            ecb.Playback(_entityManager);
        }
        Entity incoming = CreateItem(Entity.Null, new int2(1, 0));
        _entityManager.AddComponentData(incoming, new BuildingItemInputDecision(source, true, 0));
        _entityManager.SetComponentEnabled<BuildingItemInputDecision>(incoming, true);
        _entityManager.SetComponentEnabled<BeltMovementState>(incoming, true);

        Entity collection = PerformCurrentAction(assignment);

        AssertResult(collection, DroneItemTransferStatusEnum.Completed, 1);
        AssertStored(worker, new[] { incoming });
        Assert.AreEqual(0, Stored(source).Length);
        Assert.AreEqual(1, Requirement(site).ReservedQuantity);
        Assert.AreEqual(0, Requirement(site).DeliveredQuantity);
        Assert.IsFalse(_entityManager.IsComponentEnabled<BuildingItemInputDecision>(incoming));
        Assert.IsFalse(_entityManager.IsComponentEnabled<TransferOwnershipRequest>(incoming));
        Assert.IsFalse(_entityManager.IsComponentEnabled<BeltMovementState>(incoming));
        Assert.IsTrue(_entityManager.HasComponent<DisableRendering>(incoming));
        Assert.IsTrue(_entityManager.GetComponentData<ItemOwnership>(previousStock).IsWorldItem);
    }

    [Test]
    public void BuildingOutputSpace_IsUsedByRecoveryCargoInTheSameSortedTick()
    {
        using (var registryQuery = _entityManager.CreateEntityQuery(ComponentType.ReadOnly<ItemRegistry>()))
        {
            var configs = _entityManager.GetBuffer<ItemConfigElement>(registryQuery.GetSingletonEntity());
            configs[(int)ItemTypeEnum.Iron] = new ItemConfigElement(ItemTypeEnum.Iron, 2);
        }
        Entity item = CreateItem(Entity.Null, new int2(5, 0));
        CreateSite(new int2(5, 0), 1, 1);
        Entity storage = CreateStorage(new int2(1, 0), 1, 0);
        _entityManager.SetComponentData(storage, new Storage(1));
        Entity worker = CreateWorker();
        Entity assignment = WaitForInitialAssignment(worker, storage, item);
        AssertResult(PerformCurrentAction(assignment), DroneItemTransferStatusEnum.Completed, 1);
        Entity outgoing = AddItem(storage, new int2(1, 0));
        Entity retained = AddItem(storage, new int2(1, 0));
        int2 outputCell = new int2(2, 0);
        Entities.CreateBelt(outputCell, DirectionEnum.Right);
        Simulation.UpdateAndComplete(_world.GetOrCreateSystem<BeltSpatialSyncSystem>());
        _entityManager.AddComponentData(storage, new BuildingItemOutputDecision(true, outgoing, outputCell));
        _entityManager.SetComponentEnabled<BuildingItemOutputDecision>(storage, true);

        Entity stored = PerformCurrentAction(assignment);

        AssertResult(stored, DroneItemTransferStatusEnum.Completed, 1);
        AssertStored(storage, new[] { retained, item });
        Assert.AreEqual(0, Stored(worker).Length);
        Assert.IsTrue(_entityManager.GetComponentData<ItemOwnership>(outgoing).IsWorldItem);
        Assert.AreEqual(outputCell, _entityManager.GetComponentData<GridPosition>(outgoing).Value);
        Assert.AreEqual(DroneTaskAssignmentStateEnum.Completed, Assignment(assignment).State);
    }

    [Test]
    public void CancelCommand_ReturnsArrivedMaterial_AndRetargetsActuallyCollectedCargoToAnotherSite()
    {
        Entity source = CreateStorage(new int2(1, 0), 1, 2);
        Entity cancelledSite = CreateSite(new int2(10, 0), 1, 3);
        Entity arrived = AddItem(cancelledSite, new int2(10, 0));
        var initialRequirement = Requirement(cancelledSite);
        initialRequirement.DeliveredQuantity = 1;
        var initialRows = _entityManager.GetBuffer<ConstructionMaterialRequirementElement>(cancelledSite);
        initialRows[0] = initialRequirement;
        Entity nextSite = CreateSite(new int2(20, 0), 2, 3);
        Entity worker = CreateWorker();
        Entity assignment = WaitForInitialAssignment(worker, cancelledSite, source);
        Entity collectResult = PerformCurrentAction(assignment);
        AssertResult(collectResult, DroneItemTransferStatusEnum.Completed, 2);
        Entity[] cargo = Stored(worker);
        Assert.AreEqual(2, cargo.Length);
        uint oldRevision = Assignment(assignment).Revision;
        Entity cancel = _entityManager.CreateEntity(typeof(CancelConstructionRequest));
        _entityManager.SetComponentData(cancel, new CancelConstructionRequest(cancelledSite));

        RunCommandBoundary();

        Assert.IsFalse(_entityManager.Exists(cancel));
        Assert.IsFalse(_entityManager.Exists(cancelledSite));
        Assert.IsTrue(_entityManager.GetComponentData<ItemOwnership>(arrived).IsWorldItem);
        Assert.AreEqual(new int2(10, 0), _entityManager.GetComponentData<GridPosition>(arrived).Value);
        Assert.IsFalse(_entityManager.HasComponent<DisableRendering>(arrived));
        AssertCargo(worker, cargo, DroneCargoOriginEnum.Supply);
        FinishTickAfterCommand();
        Assert.AreEqual(0, Reservation(assignment).RemainingQuantity);
        Assert.AreEqual(DroneTaskAssignmentStateEnum.Retargeting, Assignment(assignment).State);
        Assert.AreEqual(assignment, WorkerAssignment(worker));

        WaitForRetarget(assignment, nextSite, DroneActionKindEnum.SupplyConstructionSite, oldRevision);
        Assert.AreEqual(2, Requirement(nextSite).ReservedQuantity);
        AssertCargo(worker, cargo, DroneCargoOriginEnum.Supply);
        Entity supplyResult = PerformCurrentAction(assignment);

        AssertResult(supplyResult, DroneItemTransferStatusEnum.Completed, 2);
        AssertStored(nextSite, cargo);
        Assert.AreEqual(2, Requirement(nextSite).DeliveredQuantity);
        Assert.AreEqual(0, Requirement(nextSite).ReservedQuantity);
        Assert.AreEqual(0, Reservation(assignment).RemainingQuantity);
        Assert.AreEqual(0, Stored(worker).Length);
        Assert.AreEqual(Entity.Null, WorkerAssignment(worker));
        AssertResult(collectResult, DroneItemTransferStatusEnum.Completed, 2);
        Assert.AreEqual(oldRevision, _entityManager.GetComponentData<DroneItemTransferResult>(collectResult).Action.AssignmentRevision);
        Assert.AreEqual(oldRevision + 1, _entityManager.GetComponentData<DroneItemTransferResult>(supplyResult).Action.AssignmentRevision);
        Assert.IsTrue(_entityManager.GetComponentData<ItemOwnership>(arrived).IsWorldItem);
        Tick();
        Assert.IsTrue(_entityManager.Exists(assignment), "미소비 결과는 완료 배정의 삭제를 보류한다.");
        AssertResult(collectResult, DroneItemTransferStatusEnum.Completed, 2);
        AssertResult(supplyResult, DroneItemTransferStatusEnum.Completed, 2);
    }

    [Test]
    public void ActualCompletion_DefensivelyRetargetsAnExistingCargoAssignmentWhoseReservationWasAlreadyZero()
    {
        Entity source = CreateStorage(new int2(1, 0), 1, 1);
        Entity completingSite = CreateSite(new int2(10, 0), 1, 1);
        Entity nextSite = CreateSite(new int2(20, 0), 2, 3);
        Entity regularWorker = CreateWorker();
        Entity heldWorker = CreateWorker();
        Entity heldItem = AddItem(heldWorker, int2.zero);
        _entityManager.SetComponentData(heldWorker, new DroneCargoState { Origin = DroneCargoOriginEnum.Supply });
        Tick(); // 공급 작업 자체는 실제 Decision/Execution/EndSimulation에서 생성한다.

        // 예약 0인 기존 운반 배정의 방어 연결만 초기 fixture로 준비한다.
        // 정상 D/R/P의 미충족량 계약에서는 다른 유효 예약이 남은 현장의 선완공은 발생하지 않는다.
        Entity heldAssignment = CreateExistingUnreservedCargoAssignment(heldWorker, SupplyTask(completingSite), completingSite);
        Entity regularAssignment = WaitForInitialAssignment(regularWorker, completingSite, source);
        Entity collected = PerformCurrentAction(regularAssignment);
        AssertResult(collected, DroneItemTransferStatusEnum.Completed, 1);
        Entity completingItem = Stored(regularWorker)[0];
        Entity completed = PerformCurrentAction(regularAssignment);

        AssertResult(completed, DroneItemTransferStatusEnum.Completed, 1);
        Assert.IsTrue(_entityManager.Exists(completingSite), "이번 드론 공급은 다음 건물 단계에서 완공에 반영한다.");
        Assert.IsTrue(_entityManager.Exists(completingItem));
        Assert.AreEqual(1, Requirement(completingSite).DeliveredQuantity);
        Assert.AreEqual(0, Requirement(completingSite).ReservedQuantity);
        AssertCargo(heldWorker, new[] { heldItem }, DroneCargoOriginEnum.Supply);
        Tick(); // 건물 종료 ECB에서 현장 삭제를 확정한 뒤 같은 틱 드론이 무효화를 판단한다.
        Assert.IsFalse(_entityManager.Exists(completingSite));
        Assert.IsFalse(_entityManager.Exists(completingItem));
        Assert.IsTrue(HasCompletedStorageAt(new int2(10, 0)));
        AssertCargo(heldWorker, new[] { heldItem }, DroneCargoOriginEnum.Supply);
        Assert.AreEqual(0, Reservation(heldAssignment).RemainingQuantity);
        Assert.AreEqual(DroneTaskAssignmentStateEnum.Retargeting, Assignment(heldAssignment).State);

        WaitForRetarget(heldAssignment, nextSite, DroneActionKindEnum.SupplyConstructionSite, 1);
        Assert.AreEqual(1, Requirement(nextSite).ReservedQuantity);
        AssertCargo(heldWorker, new[] { heldItem }, DroneCargoOriginEnum.Supply);
        Entity supplied = PerformCurrentAction(heldAssignment);

        AssertResult(supplied, DroneItemTransferStatusEnum.Completed, 1);
        AssertStored(nextSite, new[] { heldItem });
        Assert.AreEqual(1, Requirement(nextSite).DeliveredQuantity);
        Assert.AreEqual(0, Requirement(nextSite).ReservedQuantity);
        Assert.AreEqual(0, Reservation(heldAssignment).RemainingQuantity);
        AssertResult(completed, DroneItemTransferStatusEnum.Completed, 1);
    }

    [Test]
    public void DemolitionCommandBeforeCollection_ReleasesReservationAndSchedulesANewSource()
    {
        Entity source = CreateStorage(new int2(1, 0), 1, 2);
        Entity[] returnedItems = Stored(source);
        Entity alternative = CreateStorage(new int2(3, 0), 2, 2);
        Entity site = CreateSite(new int2(10, 0), 1, 3);
        Entity worker = CreateWorker();
        Entity oldAssignment = WaitForInitialAssignment(worker, site, source);
        Assert.AreEqual(2, Requirement(site).ReservedQuantity);
        Assert.AreEqual(0, Stored(worker).Length);
        Entity demolition = RequestDemolition(source);

        RunCommandBoundary();

        Assert.IsFalse(_entityManager.Exists(demolition));
        Assert.IsTrue(_entityManager.Exists(source));
        Assert.IsTrue(_entityManager.HasComponent<PendingBuildingDemolition>(source));
        FinishTickAfterCommand();
        Assert.IsFalse(_entityManager.Exists(source));
        Assert.IsFalse(_entityManager.Exists(oldAssignment));
        Assert.AreEqual(0, Requirement(site).ReservedQuantity);
        Assert.AreEqual(Entity.Null, WorkerAssignment(worker));
        foreach (Entity item in returnedItems)
        {
            Assert.IsTrue(_entityManager.GetComponentData<ItemOwnership>(item).IsWorldItem);
            Assert.AreEqual(new int2(1, 0), _entityManager.GetComponentData<GridPosition>(item).Value);
        }

        Entity newAssignment = WaitForInitialAssignment(worker, site, alternative);
        Assert.AreNotEqual(oldAssignment, newAssignment);
        Entity collected = PerformCurrentAction(newAssignment);

        AssertResult(collected, DroneItemTransferStatusEnum.Completed, 2);
        Assert.AreEqual(alternative, Assignment(newAssignment).Source);
        Assert.AreEqual(2, Stored(worker).Length);
        Assert.AreEqual(0, Stored(alternative).Length);
        Assert.AreEqual(2, Requirement(site).ReservedQuantity);
        foreach (Entity item in returnedItems) Assert.IsTrue(_entityManager.GetComponentData<ItemOwnership>(item).IsWorldItem);
    }

    [Test]
    public void DemolitionCommandDuringRecoveryTransport_PreservesCargoAndRetargetsAnotherStorage()
    {
        Entity item = CreateItem(Entity.Null, new int2(5, 0));
        Entity clearanceSite = CreateSite(new int2(5, 0), 1, 1);
        Entity storage = CreateStorage(new int2(1, 0), 1, 0);
        Entity alternative = CreateStorage(new int2(20, 0), 2, 0);
        Entity worker = CreateWorker();
        Entity assignment = WaitForInitialAssignment(worker, storage, item,
            route => route.Destination == storage ? 1f : 20f);
        Assert.AreEqual(DroneActionKindEnum.RecoverWorldItem, Assignment(assignment).NextAction);
        Entity recovered = PerformCurrentAction(assignment);
        AssertResult(recovered, DroneItemTransferStatusEnum.Completed, 1);
        AssertCargo(worker, new[] { item }, DroneCargoOriginEnum.Recovery);
        Assert.AreEqual(DroneActionKindEnum.StoreCargo, Assignment(assignment).NextAction);
        uint oldRevision = Assignment(assignment).Revision;
        Entity demolition = RequestDemolition(storage);

        RunCommandBoundary();

        Assert.IsFalse(_entityManager.Exists(demolition));
        Assert.IsTrue(_entityManager.HasComponent<PendingBuildingDemolition>(storage));
        FinishTickAfterCommand();
        Assert.IsFalse(_entityManager.Exists(storage));
        Assert.AreEqual(DroneTaskAssignmentStateEnum.Retargeting, Assignment(assignment).State);
        AssertCargo(worker, new[] { item }, DroneCargoOriginEnum.Recovery);
        WaitForRetarget(assignment, alternative, DroneActionKindEnum.StoreCargo, oldRevision);
        Assert.AreEqual(0, Requirement(clearanceSite).ReservedQuantity, "회수품을 필요 현장에 직접 공급하지 않는다.");
        Entity stored = PerformCurrentAction(assignment);

        AssertResult(stored, DroneItemTransferStatusEnum.Completed, 1);
        AssertStored(alternative, new[] { item });
        Assert.AreEqual(0, Stored(worker).Length);
        Assert.AreEqual(Entity.Null, WorkerAssignment(worker));
        Assert.AreEqual(0, Requirement(clearanceSite).DeliveredQuantity);
        Assert.AreEqual(0, Reservation(assignment).RemainingQuantity);
        AssertResult(recovered, DroneItemTransferStatusEnum.Completed, 1);
    }

    private void Tick()
    {
        AdvanceTime();
        Entities.PrepareSimulationConfiguration();
        _simulation.Update();
    }

    private void RunCommandBoundary()
    {
        AdvanceTime();
        _command.Update();
    }

    private void FinishTickAfterCommand()
    {
        Entities.PrepareConstructionConfiguration();
        _building.Update();
        _drone.Update();
        _commit.Update();
        _synchronization.Update();
    }

    private void AdvanceTime()
    {
        _elapsed += 0.1;
        Simulation.SetDeltaTime(0.1f, _elapsed);
    }

    private Entity WaitForInitialAssignment(Entity worker, Entity destination, Entity source,
        Func<DroneRouteEvaluationRequest, float> distance = null)
    {
        for (int tick = 0; tick < 6; tick++)
        {
            PublishReachableRoutes(distance ?? (route => route.Source == source ? 1f : 20f));
            Tick();
            Entity entity = WorkerAssignment(worker);
            if (entity == Entity.Null) continue;
            var assignment = Assignment(entity);
            Assert.AreEqual(destination, assignment.Destination);
            Assert.AreEqual(source, assignment.Source);
            Assert.AreEqual(DroneTaskAssignmentStateEnum.MovingToSource, assignment.State);
            return entity;
        }
        Assert.Fail("실제 작업 생성/경로 결과/예약/공개를 거쳐 초기 배정이 생성되어야 한다.");
        return Entity.Null;
    }

    private void WaitForRetarget(Entity entity, Entity destination, DroneActionKindEnum action, uint oldRevision)
    {
        for (int tick = 0; tick < 6; tick++)
        {
            PublishReachableRoutes(route => route.Destination == destination ? 1f : 20f);
            Tick();
            var assignment = Assignment(entity);
            if (assignment.Revision <= oldRevision) continue;
            Assert.AreEqual(oldRevision + 1, assignment.Revision);
            Assert.AreEqual(destination, assignment.Destination);
            Assert.AreEqual(action, assignment.NextAction);
            Assert.AreEqual(DroneTaskAssignmentStateEnum.MovingToDestination, assignment.State);
            Assert.AreEqual(entity, WorkerAssignment(assignment.Worker));
            return;
        }
        Assert.Fail("적재품 재탐색/경로 평가/예약/공개가 기존 배정을 새 버전으로 갱신해야 한다.");
    }

    private void PublishReachableRoutes(Func<DroneRouteEvaluationRequest, float> distance)
    {
        // 경로 계산 대신 평가 계약의 revision/거리 응답만 제공한다. 이후 작업/예약 전이는 제품 시스템이 수행한다.
        foreach (Entity entity in Query<DroneRouteEvaluationRequest>())
        {
            var request = _entityManager.GetComponentData<DroneRouteEvaluationRequest>(entity);
            Assert.IsFalse(request.IsDropPositionSearch, "이 테스트에는 도달 가능한 대체 목적지를 명시적으로 준비했다.");
            var result = new DroneRouteEvaluationResult
            {
                EvaluationRevision = request.EvaluationRevision,
                Status = DroneRouteEvaluationStatusEnum.Reachable,
                TotalDistance = distance(request)
            };
            if (_entityManager.HasComponent<DroneRouteEvaluationResult>(entity)) _entityManager.SetComponentData(entity, result);
            else _entityManager.AddComponentData(entity, result);
        }
    }

    private Entity PerformCurrentAction(Entity entity)
    {
        var assignment = Assignment(entity);
        bool collection = assignment.NextAction == DroneActionKindEnum.CollectFromStorage ||
                          assignment.NextAction == DroneActionKindEnum.RecoverWorldItem;
        Entity target = collection ? assignment.Source : assignment.Destination;
        int2 cell = _entityManager.GetComponentData<GridPosition>(target).Value;
        var observation = _entityManager.GetComponentData<DroneWorkerObservation>(assignment.Worker);
        observation.Position = new float3(cell.x + 0.25f, cell.y + 0.25f, 0f);
        observation.Revision++;
        _entityManager.SetComponentData(assignment.Worker, observation);
        Entity request = DroneActionRequestUtility.Submit(_entityManager, new DroneActionReadyRequest
        {
            Action = new DroneActionIdentity
            {
                Assignment = entity,
                AssignmentRevision = assignment.Revision,
                Sequence = assignment.LastAppliedActionSequence + 1,
                Worker = assignment.Worker,
                Kind = assignment.NextAction,
                Target = target
            },
            WorkerObservationRevision = observation.Revision,
            WorldPosition = cell
        });
        Tick();
        return request;
    }

    private Entity RequestDemolition(Entity building)
    {
        Entity request = _entityManager.CreateEntity(typeof(DemolishBuildingRequest));
        _entityManager.SetComponentData(request, new DemolishBuildingRequest(building));
        return request;
    }

    private Entity CreateSite(int2 position, ulong stamp, int required)
    {
        Entity site = _entityManager.CreateEntity();
        _entityManager.AddComponentData(site, new ConstructionSite(BuildingTypeEnum.Storage));
        _entityManager.AddComponentData(site, new BuildingType(BuildingTypeEnum.ConstructionSite));
        _entityManager.AddComponentData(site, new GridPosition(position));
        _entityManager.AddComponentData(site, new BuildingFootprint(new int2(1, 1)));
        _entityManager.AddComponentData(site, new Direction(DirectionEnum.Up));
        _entityManager.AddComponentData(site, new PlacementStamp(stamp, 0));
        _entityManager.AddBuffer<StoredItemElement>(site);
        _entityManager.AddBuffer<ConstructionMaterialRequirementElement>(site).Add(
            new ConstructionMaterialRequirementElement(ItemTypeEnum.Iron, required));
        return site;
    }

    private Entity CreateStorage(int2 position, ulong stamp, int count)
    {
        Entity storage = _entityManager.CreateEntity();
        _entityManager.AddComponentData(storage, new BuildingType(BuildingTypeEnum.Storage));
        _entityManager.AddComponentData(storage, new GridPosition(position));
        _entityManager.AddComponentData(storage, new BuildingFootprint(new int2(1, 1)));
        _entityManager.AddComponentData(storage, new Direction(DirectionEnum.Up));
        _entityManager.AddComponentData(storage, new PlacementStamp(stamp, 0));
        _entityManager.AddComponentData(storage, new Storage(4));
        _entityManager.AddComponentData(storage, new StorageFilter(StorageFilterMode.AllowAll));
        _entityManager.AddBuffer<StoredItemElement>(storage);
        for (int i = 0; i < count; i++) AddItem(storage, position);
        return storage;
    }

    private Entity CreateWorker()
    {
        Entity worker = _entityManager.CreateEntity(typeof(DroneWorker));
        _entityManager.AddComponentData(worker, new DroneWorkerObservation
        {
            Position = float3.zero,
            Revision = 1,
            CanAcceptTask = true
        });
        _entityManager.AddComponentData(worker, new DroneWorkerAssignment());
        _entityManager.AddComponentData(worker, new DroneCargoState());
        _entityManager.AddBuffer<StoredItemElement>(worker);
        return worker;
    }

    private Entity AddItem(Entity owner, int2 position)
    {
        Entity item = CreateItem(owner, position);
        _entityManager.GetBuffer<StoredItemElement>(owner).Add(new StoredItemElement(item, ItemTypeEnum.Iron, 0));
        return item;
    }

    private Entity CreateItem(Entity owner, int2 position)
    {
        Entity item = _entityManager.CreateEntity();
        _entityManager.AddComponentData(item, new ItemIdentity(ItemTypeEnum.Iron));
        _entityManager.AddComponentData(item, new ItemOwnership(owner));
        _entityManager.AddComponentData(item, new GridPosition(position));
        _entityManager.AddComponentData(item, new Direction(DirectionEnum.Up));
        _entityManager.AddComponentData(item, LocalTransform.FromPosition(new float3(position.x, position.y, 0f)));
        _entityManager.AddComponentData(item, new TransferOwnershipRequest());
        _entityManager.SetComponentEnabled<TransferOwnershipRequest>(item, false);
        _entityManager.AddComponentData(item, new DestroyItemRequest());
        _entityManager.SetComponentEnabled<DestroyItemRequest>(item, false);
        _entityManager.AddComponentData(item, new BeltMovementState());
        _entityManager.SetComponentEnabled<BeltMovementState>(item, false);
        if (owner != Entity.Null) _entityManager.AddComponent<DisableRendering>(item);
        return item;
    }

    private Entity CreateExistingUnreservedCargoAssignment(Entity worker, Entity task, Entity site)
    {
        // 예약 0인 기존 적재품을 직접 준비하는 방어 사례다. 유효 예약을 남긴 정상 현장의 선완공 흐름이 아니다.
        Entity entity = _entityManager.CreateEntity(typeof(DroneTaskAssignment), typeof(ConstructionSupplyReservation));
        _entityManager.SetComponentData(entity, new DroneTaskAssignment
        {
            Worker = worker,
            Task = task,
            OriginalTaskCreationSequence = _entityManager.GetComponentData<DroneLogisticsTask>(task).CreationSequence,
            Destination = site,
            ItemType = ItemTypeEnum.Iron,
            AssignedQuantity = 1,
            Revision = 1,
            State = DroneTaskAssignmentStateEnum.MovingToDestination,
            NextAction = DroneActionKindEnum.SupplyConstructionSite
        });
        _entityManager.SetComponentData(entity, new ConstructionSupplyReservation
        {
            Site = site,
            ItemType = ItemTypeEnum.Iron,
            AssignmentRevision = 1,
            RemainingQuantity = 0
        });
        _entityManager.SetComponentData(worker, new DroneWorkerAssignment { Assignment = entity });
        return entity;
    }

    private Entity SupplyTask(Entity site)
    {
        foreach (Entity entity in Query<DroneLogisticsTask>())
        {
            var task = _entityManager.GetComponentData<DroneLogisticsTask>(entity);
            if (task.Kind == DroneLogisticsTaskKindEnum.ConstructionSupply && task.Target == site) return entity;
        }
        Assert.Fail("현장의 공급 작업이 실제 생성 단계에서 공개되어야 한다.");
        return Entity.Null;
    }

    private bool HasCompletedStorageAt(int2 position)
    {
        foreach (Entity entity in Query<BuildingType>())
        {
            if (_entityManager.GetComponentData<BuildingType>(entity).Type != BuildingTypeEnum.Storage) continue;
            if (!_entityManager.HasComponent<Storage>(entity) || !_entityManager.HasComponent<GridPosition>(entity)) continue;
            if (math.all(_entityManager.GetComponentData<GridPosition>(entity).Value == position)) return true;
        }
        return false;
    }

    private void AssertCargo(Entity worker, Entity[] expected, DroneCargoOriginEnum origin)
    {
        AssertStored(worker, expected);
        Assert.AreEqual(origin, _entityManager.GetComponentData<DroneCargoState>(worker).Origin);
    }

    private void AssertStored(Entity owner, Entity[] expected)
    {
        CollectionAssert.AreEquivalent(expected, Stored(owner));
        foreach (Entity item in expected)
        {
            Assert.IsTrue(_entityManager.Exists(item));
            Assert.AreEqual(owner, _entityManager.GetComponentData<ItemOwnership>(item).Owner);
            Assert.IsTrue(_entityManager.HasComponent<DisableRendering>(item));
        }
    }

    private void AssertResult(Entity entity, DroneItemTransferStatusEnum status, int quantity)
    {
        Assert.IsTrue(_entityManager.Exists(entity));
        Assert.IsFalse(_entityManager.HasComponent<DroneActionReadyRequest>(entity));
        var result = _entityManager.GetComponentData<DroneItemTransferResult>(entity);
        Assert.AreEqual(status, result.Status);
        Assert.AreEqual(quantity, result.MovedQuantity);
    }

    private Entity[] Stored(Entity owner)
    {
        var buffer = _entityManager.GetBuffer<StoredItemElement>(owner, true);
        var items = new Entity[buffer.Length];
        for (int i = 0; i < items.Length; i++) items[i] = buffer[i].ItemEntity;
        return items;
    }

    private Entity[] Query<T>() where T : unmanaged, IComponentData
    {
        using var query = _entityManager.CreateEntityQuery(ComponentType.ReadOnly<T>());
        using var entities = query.ToEntityArray(Allocator.Temp);
        return entities.ToArray();
    }

    private Entity WorkerAssignment(Entity worker) => _entityManager.GetComponentData<DroneWorkerAssignment>(worker).Assignment;
    private DroneTaskAssignment Assignment(Entity entity) => _entityManager.GetComponentData<DroneTaskAssignment>(entity);
    private ConstructionSupplyReservation Reservation(Entity entity) => _entityManager.GetComponentData<ConstructionSupplyReservation>(entity);
    private ConstructionMaterialRequirementElement Requirement(Entity site) =>
        _entityManager.GetBuffer<ConstructionMaterialRequirementElement>(site, true)[0];
}
