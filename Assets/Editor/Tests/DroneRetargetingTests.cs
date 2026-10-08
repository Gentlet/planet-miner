using System;
using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

/// <summary>
/// 역할·목적: 적재품 재배정·보관·안전 방출 대기와 revision 보존에 대한 NUnit EditMode 회귀 검증.
/// 입력·검사: 적재 출처/배정·외부 경로/관측 결과로 관리 phase를 실행한다. 실제 이동/충전/경로 계산은 실행하지 않는다.
/// 수명: EcsWorldTestFixture가 각 사례의 독립 World를 준비하고 종료 시 해제한다.
/// </summary>
public class DroneRetargetingTests : EcsWorldTestFixture
{
    private DroneDecisionGroup _decision;
    private DroneReservationGroup _reservation;
    private DroneExecutionGroup _execution;
    private DroneStateApplyGroup _apply;
    private BuildingSimulationGroup _building;
    private BuildingStateApplyGroup _buildingApply;
    private SimulationCommitGroup _commit;

    [SetUp]
    public override void SetUp()
    {
        base.SetUp();
        CreateGameplayPrefabDatabases();
        Entity registry = _entityManager.CreateEntity(typeof(ItemRegistry));
        _entityManager.SetComponentData(registry, new ItemRegistry { DefaultMaxStack = 2 });
        var config = _entityManager.AddBuffer<ItemConfigElement>(registry);
        foreach (ItemTypeEnum type in Enum.GetValues(typeof(ItemTypeEnum))) config.Add(new ItemConfigElement(type, 2));
        Entity capacity = _entityManager.CreateEntity(typeof(DroneCapacityState));
        _entityManager.SetComponentData(capacity, new DroneCapacityState { CarryingCapacity = 3 });
        _decision = _world.GetOrCreateSystemManaged<DroneDecisionGroup>();
        _decision.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneTaskDecisionSystem>());
        _decision.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneItemTransferDecisionSystem>());
        _reservation = _world.GetOrCreateSystemManaged<DroneReservationGroup>();
        _reservation.AddSystemToUpdateList(_world.GetOrCreateSystem<ConstructionSupplyReservationSystem>());
        _execution = _world.GetOrCreateSystemManaged<DroneExecutionGroup>();
        _execution.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneTaskExecutionSystem>());
        _execution.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneItemTransferExecutionSystem>());
        _apply = _world.GetOrCreateSystemManaged<DroneStateApplyGroup>();
        _apply.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneTaskAssignmentPublishSystem>());
        _apply.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneTaskLifecycleApplySystem>());
        _buildingApply = _world.GetOrCreateSystemManaged<BuildingStateApplyGroup>();
        _buildingApply.AddSystemToUpdateList(_world.GetOrCreateSystem<ItemOwnershipApplySystem>());
        _building = _world.GetOrCreateSystemManaged<BuildingSimulationGroup>();
        _building.AddSystemToUpdateList(_buildingApply);
        _building.AddSystemToUpdateList(_world.GetOrCreateSystemManaged<EndBuildingEntityCommandBufferSystem>());
        _commit = _world.GetOrCreateSystemManaged<SimulationCommitGroup>();
        _commit.AddSystemToUpdateList(_world.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>());
        _decision.SortSystems();
        _reservation.SortSystems();
        _execution.SortSystems();
        _apply.SortSystems();
        _buildingApply.SortSystems();
        _building.SortSystems();
        _commit.SortSystems();
    }

    [Test]
    public void SupplyCargo_PrefersNearestSiteThenPlacementStamp_OverEvenCloserStorage()
    {
        Entity farSite = CreateSite(new int2(10, 0), 1, 2);
        Entity tiedYounger = CreateSite(new int2(20, 0), 20, 2);
        Entity tiedOlder = CreateSite(new int2(30, 0), 10, 2);
        Entity storage = CreateStorage(new int2(1, 0), 1);
        Entity assignment = CreateRetargetingAssignment(DroneCargoOriginEnum.Supply, 2, 15);
        Entity worker = Assignment(assignment).Worker;
        Entity[] cargo = Stored(worker);

        Tick();
        Entity[] requests = Query<DroneRouteEvaluationRequest>();
        Assert.AreEqual(3, requests.Length);
        foreach (Entity entity in requests)
        {
            var route = _entityManager.GetComponentData<DroneRouteEvaluationRequest>(entity);
            Assert.AreEqual(DroneRouteKindEnum.Direct, route.Kind);
            Assert.AreEqual(assignment, route.Assignment);
            Assert.AreEqual(1U, route.AssignmentRevision);
            Assert.AreEqual(Entity.Null, route.Source);
            Assert.AreNotEqual(storage, route.Destination);
        }
        AnswerRoutes(route => route.Destination == farSite ? 50f : 5f);
        Tick();

        var updated = Assignment(assignment);
        Assert.AreEqual(tiedOlder, updated.Destination);
        Assert.AreEqual(DroneActionKindEnum.SupplyConstructionSite, updated.NextAction);
        Assert.AreEqual(2U, updated.Revision);
        Assert.AreEqual(15UL, updated.OriginalTaskCreationSequence);
        Assert.AreEqual(1, Query<DroneTaskAssignment>().Length, "기존 배정 엔티티를 새 버전으로 갱신한다.");
        Assert.AreEqual(2, Requirement(tiedOlder).ReservedQuantity);
        Assert.AreEqual(0, Requirement(tiedYounger).ReservedQuantity);
        Assert.AreEqual(0, Requirement(farSite).ReservedQuantity);
        AssertCargo(worker, cargo, DroneCargoOriginEnum.Supply);
    }

    [Test]
    public void RecoveryCargo_SkipsConstructionSites_AndUsesStorageDistanceThenPlacementStamp()
    {
        Entity nearbySite = CreateSite(new int2(1, 0), 1, 2);
        Entity younger = CreateStorage(new int2(20, 0), 20);
        Entity older = CreateStorage(new int2(30, 0), 10);
        Entity assignment = CreateRetargetingAssignment(DroneCargoOriginEnum.Recovery, 1, 2);

        Tick();
        foreach (Entity entity in Query<DroneRouteEvaluationRequest>())
        {
            var route = _entityManager.GetComponentData<DroneRouteEvaluationRequest>(entity);
            Assert.AreNotEqual(nearbySite, route.Destination);
            Assert.That(route.Destination == younger || route.Destination == older);
        }
        AnswerRoutes(route => 8f);
        Tick();

        Assert.AreEqual(older, Assignment(assignment).Destination);
        Assert.AreEqual(DroneActionKindEnum.StoreCargo, Assignment(assignment).NextAction);
        Assert.AreEqual(0, Requirement(nearbySite).ReservedQuantity);
        Assert.AreEqual(0, Reservation(assignment).RemainingQuantity);
        Assert.AreEqual(DroneCargoOriginEnum.Recovery,
            _entityManager.GetComponentData<DroneCargoState>(Assignment(assignment).Worker).Origin);
    }

    [Test]
    public void UnevaluatedRouteWaits_AndOnlyConfirmedUnreachableStorageAllowsWorldDrop()
    {
        CreateStorage(new int2(10, 0), 1);
        Entity assignment = CreateRetargetingAssignment(DroneCargoOriginEnum.Recovery, 2, 1);
        Entity worker = Assignment(assignment).Worker;
        Entity[] cargo = Stored(worker);
        Tick();
        Assert.AreEqual(1, Query<DroneRouteEvaluationRequest>().Length);

        Tick();
        Assert.AreEqual(1U, Assignment(assignment).Revision);
        Assert.AreEqual(DroneTaskAssignmentStateEnum.Retargeting, Assignment(assignment).State);
        Assert.AreEqual(DroneActionKindEnum.None, Assignment(assignment).NextAction);
        AssertCargo(worker, cargo, DroneCargoOriginEnum.Recovery);

        AnswerRoutes(route => null);
        Tick();
        Assert.AreEqual(DroneActionKindEnum.DropCargo, Assignment(assignment).NextAction);
        Assert.AreEqual(Entity.Null, Assignment(assignment).Destination);
        Assert.AreEqual(int2.zero, Assignment(assignment).DropPosition,
            "현재 위치가 현장 밖이면 그 셀을 배출 위치로 유지한다.");
        Assert.AreEqual(2U, Assignment(assignment).Revision);
        AssertCargo(worker, cargo, DroneCargoOriginEnum.Recovery);
        Entity request = SubmitCurrentAction(assignment);
        Tick();

        AssertResult(request, DroneItemTransferStatusEnum.Completed, 2);
        Assert.AreEqual(0, Stored(worker).Length);
        foreach (Entity item in cargo)
        {
            Assert.IsTrue(_entityManager.Exists(item));
            Assert.IsTrue(_entityManager.GetComponentData<ItemOwnership>(item).IsWorldItem);
            Assert.IsTrue(_entityManager.HasComponent<DroneRecoveryPending>(item));
            Assert.IsFalse(_entityManager.HasComponent<DisableRendering>(item));
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public void SupplyCargo_TriesStorageAfterAllSitesAreUnreachable_BeforeChoosingDrop(bool storageReachable)
    {
        Entity site = CreateSite(new int2(2, 0), 1, 2);
        Entity storage = CreateStorage(new int2(10, 0), 1);
        Entity assignment = CreateRetargetingAssignment(DroneCargoOriginEnum.Supply, 2, 1);
        Tick();
        AnswerRoutes(route => null);
        Tick();
        Assert.AreEqual(DroneActionKindEnum.None, Assignment(assignment).NextAction,
            "현장 경로 실패만으로 미평가 보관처를 건너뛰고 방출하지 않는다.");
        bool hasStorageRoute = false;
        foreach (Entity entity in Query<DroneRouteEvaluationRequest>())
        {
            hasStorageRoute |= _entityManager.GetComponentData<DroneRouteEvaluationRequest>(entity).Destination == storage;
        }
        Assert.IsTrue(hasStorageRoute);
        AnswerRoutes(route => route.Destination == storage && storageReachable ? 10f : (float?)null);
        Tick();

        Assert.AreEqual(storageReachable ? DroneActionKindEnum.StoreCargo : DroneActionKindEnum.DropCargo,
            Assignment(assignment).NextAction);
        Assert.AreEqual(storageReachable ? storage : Entity.Null, Assignment(assignment).Destination);
        Assert.AreEqual(0, Requirement(site).ReservedQuantity);
        Assert.AreEqual(0, Reservation(assignment).RemainingQuantity);
    }

    [Test]
    public void PartialStorage_PreservesRemainingCargo_ThenRetargetsTheSameAssignmentToANewStorage()
    {
        Entity firstStorage = CreateStorage(new int2(5, 0), 1, 1, 1);
        Entity assignment = CreateRetargetingAssignment(DroneCargoOriginEnum.Recovery, 3, 7);
        Entity worker = Assignment(assignment).Worker;
        Tick();
        AnswerRoutes(route => 5f);
        Tick();
        Entity request = SubmitCurrentAction(assignment);
        Tick();

        AssertResult(request, DroneItemTransferStatusEnum.Partial, 1);
        Assert.AreEqual(2, Stored(firstStorage).Length);
        Entity[] remaining = Stored(worker);
        Assert.AreEqual(2, remaining.Length);
        AssertCargo(worker, remaining, DroneCargoOriginEnum.Recovery);
        Assert.AreEqual(DroneTaskAssignmentStateEnum.Retargeting, Assignment(assignment).State);
        Assert.IsTrue(DroneActionRequestUtility.TryConsumeResult(_entityManager, request, out _));
        Entity secondStorage = CreateStorage(new int2(20, 0), 2);
        Tick();
        AnswerRoutes(route => 20f);
        Tick();

        Assert.AreEqual(secondStorage, Assignment(assignment).Destination);
        Assert.AreEqual(DroneActionKindEnum.StoreCargo, Assignment(assignment).NextAction);
        Assert.AreEqual(3U, Assignment(assignment).Revision);
        Assert.AreEqual(7UL, Assignment(assignment).OriginalTaskCreationSequence);
        AssertCargo(worker, remaining, DroneCargoOriginEnum.Recovery);
        Entity secondRequest = SubmitCurrentAction(assignment);
        Tick();
        AssertResult(secondRequest, DroneItemTransferStatusEnum.Completed, 2);
        CollectionAssert.AreEquivalent(remaining, Stored(secondStorage));
        Assert.AreEqual(0, Stored(worker).Length);
        Tick();
        Assert.AreEqual(DroneTaskAssignmentStateEnum.Completed, Assignment(assignment).State,
            "결과가 소비되기 전 종료 정리는 Completed를 Cancelled로 바꾸지 않는다.");
        Assert.IsTrue(DroneActionRequestUtility.TryConsumeResult(_entityManager, secondRequest, out _));
        Tick();
        Assert.IsFalse(_entityManager.Exists(assignment));
    }

    [Test]
    public void LoadedAssignmentsReserveBeforeIdleWorkers_AndUseOriginalCreationOrderWithinLoadedWorkers()
    {
        Entity site = CreateSite(new int2(10, 0), 1, 2);
        CreateStorage(new int2(2, 0), 1, 2);
        // 엔티티 생성 순서와 현재 연결 작업의 순서를 의도적으로 최초 작업 순서와 반대로 둔다.
        Entity laterOrigin = CreateRetargetingAssignment(DroneCargoOriginEnum.Supply, 2, 20, 1);
        Entity earlierOrigin = CreateRetargetingAssignment(DroneCargoOriginEnum.Supply, 2, 10, 99);
        Entity idle = CreateWorker();
        Tick();
        AnswerRoutes(route => 10f);
        Tick();

        Assert.AreEqual(site, Assignment(earlierOrigin).Destination);
        Assert.AreEqual(DroneActionKindEnum.SupplyConstructionSite, Assignment(earlierOrigin).NextAction);
        Assert.AreEqual(2, Reservation(earlierOrigin).RemainingQuantity);
        Assert.AreEqual(DroneTaskAssignmentStateEnum.Retargeting, Assignment(laterOrigin).State);
        Assert.AreEqual(1U, Assignment(laterOrigin).Revision);
        Assert.AreEqual(Entity.Null, _entityManager.GetComponentData<DroneWorkerAssignment>(idle).Assignment);
        Assert.AreEqual(2, Query<DroneTaskAssignment>().Length);
        Assert.AreEqual(2, Requirement(site).ReservedQuantity);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RetargetingReleasesOldReservation_ThenPublishesOrRollsBackOnlyTheNewReservation(bool invalidateBeforePublish)
    {
        Entity oldSite = CreateSite(new int2(20, 0), 1, 3);
        var oldSiteState = _entityManager.GetComponentData<ConstructionSite>(oldSite);
        oldSiteState.Flags = ConstructionSiteFlags.Cancelled;
        _entityManager.SetComponentData(oldSite, oldSiteState);
        Entity newSite = CreateSite(new int2(10, 0), 2, 2);
        Entity assignment = CreateRetargetingAssignment(DroneCargoOriginEnum.Supply, 3, 4);
        var oldReservation = Reservation(assignment);
        oldReservation.Site = oldSite;
        oldReservation.RemainingQuantity = 3;
        _entityManager.SetComponentData(assignment, oldReservation);
        var requirement = Requirement(oldSite);
        requirement.ReservedQuantity = 3;
        var oldRequirements = _entityManager.GetBuffer<ConstructionMaterialRequirementElement>(oldSite);
        oldRequirements[0] = requirement;
        Entity worker = Assignment(assignment).Worker;
        Entity[] cargo = Stored(worker);
        Tick();
        Assert.AreEqual(0, Requirement(oldSite).ReservedQuantity);
        Assert.AreEqual(0, Reservation(assignment).RemainingQuantity);
        AnswerRoutes(route => 10f);

        _decision.Update();
        _reservation.Update();
        Assert.AreEqual(2, Requirement(newSite).ReservedQuantity);
        Assert.AreEqual(1U, Assignment(assignment).Revision, "예약 확보만으로 공개된 배정 버전을 변경하지 않는다.");
        if (invalidateBeforePublish)
        {
            var site = _entityManager.GetComponentData<ConstructionSite>(newSite);
            site.Flags = ConstructionSiteFlags.Cancelled;
            _entityManager.SetComponentData(newSite, site);
        }
        _execution.Update();
        ApplyDroneAndCommit();

        Assert.AreEqual(0, Requirement(oldSite).ReservedQuantity);
        Assert.AreEqual(invalidateBeforePublish ? 0 : 2, Requirement(newSite).ReservedQuantity);
        Assert.AreEqual(invalidateBeforePublish ? 0 : 2, Reservation(assignment).RemainingQuantity);
        Assert.AreEqual(invalidateBeforePublish ? 1U : 2U, Assignment(assignment).Revision);
        Assert.AreEqual(assignment, _entityManager.GetComponentData<DroneWorkerAssignment>(worker).Assignment);
        AssertCargo(worker, cargo, DroneCargoOriginEnum.Supply);
    }

    [Test]
    public void OldActionRevisionAfterRetargeting_IsRejectedWithoutConsumingCargoOrTheNewReservation()
    {
        Entity site = CreateSite(new int2(10, 0), 1, 1);
        Entity assignment = CreateRetargetingAssignment(DroneCargoOriginEnum.Supply, 1, 3);
        Entity worker = Assignment(assignment).Worker;
        Entity[] cargo = Stored(worker);
        Tick();
        AnswerRoutes(route => 10f);
        Tick();
        Assert.AreEqual(2U, Assignment(assignment).Revision);
        var stale = Ready(assignment);
        stale.Action.AssignmentRevision = 1;
        Entity request = DroneActionRequestUtility.Submit(_entityManager, stale);

        Tick();

        AssertResult(request, DroneItemTransferStatusEnum.Rejected, 0);
        AssertCargo(worker, cargo, DroneCargoOriginEnum.Supply);
        Assert.AreEqual(1, Requirement(site).ReservedQuantity);
        Assert.AreEqual(1, Reservation(assignment).RemainingQuantity);
        Assert.AreEqual(0, Requirement(site).DeliveredQuantity);
        Assert.AreEqual(2U, Assignment(assignment).Revision);
    }

    [Test]
    public void UnconsumedOldResult_DoesNotMakeCompletedAssignmentClaimTheWorkersNewCargo()
    {
        Entity storage = CreateStorage(new int2(10, 0), 1);
        Entity oldAssignment = CreateRetargetingAssignment(DroneCargoOriginEnum.Recovery, 1, 1);
        Entity worker = Assignment(oldAssignment).Worker;
        Entity oldTask = Assignment(oldAssignment).Task;
        Tick();
        AnswerRoutes(route => 10f);
        Tick();
        Entity oldResult = SubmitCurrentAction(oldAssignment);
        Tick();
        AssertResult(oldResult, DroneItemTransferStatusEnum.Completed, 1);
        Assert.AreEqual(DroneTaskAssignmentStateEnum.Completed, Assignment(oldAssignment).State);
        Assert.AreEqual(0, Stored(worker).Length);
        Assert.AreEqual(DroneLogisticsTaskStateEnum.Closed,
            _entityManager.GetComponentData<DroneLogisticsTask>(oldTask).State);

        // 결과 소비를 기다리는 이전 배정과 별개로 같은 수행자가 새 실물을 운반하는 상태를 준비한다.
        AddItems(worker, 1);
        Entity[] newCargo = Stored(worker);
        _entityManager.SetComponentData(worker, new DroneCargoState { Origin = DroneCargoOriginEnum.Recovery });
        Entity newTask = _entityManager.CreateEntity(typeof(DroneLogisticsTask));
        _entityManager.SetComponentData(newTask, new DroneLogisticsTask
        {
            Kind = DroneLogisticsTaskKindEnum.WorldItemRecovery,
            State = DroneLogisticsTaskStateEnum.Closed,
            Target = newCargo[0],
            ItemType = ItemTypeEnum.Iron,
            CreationSequence = 20,
            RecoveryReason = DroneRecoveryReasonEnum.DroneDrop
        });
        Entity newAssignment = _entityManager.CreateEntity(typeof(DroneTaskAssignment), typeof(ConstructionSupplyReservation));
        _entityManager.SetComponentData(newAssignment, new DroneTaskAssignment
        {
            Worker = worker,
            Task = newTask,
            OriginalTaskCreationSequence = 20,
            Source = newCargo[0],
            Destination = storage,
            ItemType = ItemTypeEnum.Iron,
            AssignedQuantity = 1,
            Revision = 1,
            State = DroneTaskAssignmentStateEnum.MovingToDestination,
            NextAction = DroneActionKindEnum.StoreCargo
        });
        _entityManager.SetComponentData(newAssignment, new ConstructionSupplyReservation
        {
            ItemType = ItemTypeEnum.Iron,
            AssignmentRevision = 1
        });
        _entityManager.SetComponentData(worker, new DroneWorkerAssignment { Assignment = newAssignment });

        Tick();

        Assert.IsTrue(_entityManager.Exists(oldAssignment));
        Assert.AreEqual(DroneTaskAssignmentStateEnum.Completed, Assignment(oldAssignment).State,
            "다른 배정의 적재품 때문에 결과 대기 중인 완료 배정을 Retargeting으로 되돌리지 않는다.");
        AssertResult(oldResult, DroneItemTransferStatusEnum.Completed, 1);
        Assert.AreEqual(newAssignment, _entityManager.GetComponentData<DroneWorkerAssignment>(worker).Assignment);
        Assert.AreEqual(DroneTaskAssignmentStateEnum.MovingToDestination, Assignment(newAssignment).State);
        AssertCargo(worker, newCargo, DroneCargoOriginEnum.Recovery);
        Assert.AreNotEqual(oldTask, Assignment(newAssignment).Task);
        Assert.IsTrue(DroneActionRequestUtility.TryConsumeResult(_entityManager, oldResult, out _));

        Tick();

        Assert.IsFalse(_entityManager.Exists(oldAssignment));
        Assert.IsFalse(_entityManager.Exists(oldResult));
        Assert.AreEqual(newAssignment, _entityManager.GetComponentData<DroneWorkerAssignment>(worker).Assignment);
        Assert.AreEqual(newTask, Assignment(newAssignment).Task);
        Assert.AreEqual(DroneTaskAssignmentStateEnum.MovingToDestination, Assignment(newAssignment).State);
        AssertCargo(worker, newCargo, DroneCargoOriginEnum.Recovery);
    }

    [TestCase(DirectionEnum.Up, 1, 0, 2, 0)]
    [TestCase(DirectionEnum.Right, 0, 1, 1, 1)]
    public void CargoInsideRotatedSite_WaitsForOutsideDestinationAndArrival_BeforeDropping(
        DirectionEnum direction, int startX, int startY, int dropX, int dropY)
    {
        _buildingApply.AddSystemToUpdateList(_world.GetOrCreateSystem<ConstructionLifecycleApplySystem>());
        _buildingApply.SortSystems();
        Entity site = CreateSite(int2.zero, 1, 1);
        _entityManager.SetComponentData(site, new BuildingFootprint(new int2(2, 1)));
        _entityManager.SetComponentData(site, new Direction(direction));
        Entity assignment = CreateRetargetingAssignment(DroneCargoOriginEnum.Recovery, 1, 5);
        Entity worker = Assignment(assignment).Worker;
        Entity[] cargo = Stored(worker);
        Entity item = cargo[0];
        ObserveWorkerAt(worker, new int2(startX, startY));
        int2 dropPosition = new int2(dropX, dropY);

        Tick();
        Entity search = DropPositionSearch(assignment);
        Assert.AreEqual(DroneActionKindEnum.None, Assignment(assignment).NextAction);
        Assert.AreEqual(DroneTaskAssignmentStateEnum.Retargeting, Assignment(assignment).State);
        AssertCargo(worker, cargo, DroneCargoOriginEnum.Recovery);
        PublishDropPosition(search, DroneRouteEvaluationStatusEnum.Reachable, dropPosition);
        Tick();

        Assert.AreEqual(DroneActionKindEnum.DropCargo, Assignment(assignment).NextAction);
        Assert.AreEqual(dropPosition, Assignment(assignment).DropPosition);
        Assert.AreEqual(DroneTaskAssignmentStateEnum.MovingToDestination, Assignment(assignment).State);
        Entity tooEarly = SubmitCurrentAction(assignment);
        Tick();

        AssertResult(tooEarly, DroneItemTransferStatusEnum.Rejected, 0);
        AssertCargo(worker, cargo, DroneCargoOriginEnum.Recovery);
        Assert.AreEqual(DroneTaskAssignmentStateEnum.MovingToDestination, Assignment(assignment).State);
        ObserveWorkerAt(worker, dropPosition);
        Entity arrived = SubmitCurrentAction(assignment);
        Tick();

        AssertResult(arrived, DroneItemTransferStatusEnum.Completed, 1);
        Assert.IsTrue(_entityManager.GetComponentData<ItemOwnership>(item).IsWorldItem);
        Assert.AreEqual(dropPosition, _entityManager.GetComponentData<GridPosition>(item).Value);
        Assert.IsFalse((_entityManager.GetComponentData<ConstructionSite>(site).Flags &
                       ConstructionSiteFlags.AwaitingItemClearance) != 0);
        Assert.AreEqual(0, Stored(worker).Length);
        Tick();
        int recoveryCount = 0;
        foreach (Entity entity in Query<DroneLogisticsTask>())
        {
            var task = _entityManager.GetComponentData<DroneLogisticsTask>(entity);
            if (task.Kind == DroneLogisticsTaskKindEnum.WorldItemRecovery && task.Target == item &&
                task.State == DroneLogisticsTaskStateEnum.Open) recoveryCount++;
        }
        Assert.AreEqual(1, recoveryCount);
    }

    [TestCase(DroneRouteEvaluationStatusEnum.None)]
    [TestCase(DroneRouteEvaluationStatusEnum.Unreachable)]
    public void NoReachableOutsideDropPosition_KeepsCargoAndWaits(DroneRouteEvaluationStatusEnum status)
    {
        CreateSite(int2.zero, 1, 1);
        Entity assignment = CreateRetargetingAssignment(DroneCargoOriginEnum.Recovery, 2, 8);
        Entity worker = Assignment(assignment).Worker;
        Entity[] cargo = Stored(worker);
        Tick();
        Entity search = DropPositionSearch(assignment);
        PublishDropPosition(search, status, new int2(1, 0));

        Tick();
        Tick();

        Assert.AreEqual(1U, Assignment(assignment).Revision);
        Assert.AreEqual(DroneTaskAssignmentStateEnum.Retargeting, Assignment(assignment).State);
        Assert.AreEqual(DroneActionKindEnum.None, Assignment(assignment).NextAction);
        AssertCargo(worker, cargo, DroneCargoOriginEnum.Recovery);
        foreach (Entity item in cargo) Assert.IsFalse(_entityManager.HasComponent<DroneRecoveryPending>(item));

        Assert.IsTrue(_entityManager.Exists(search));
        PublishDropPosition(search, DroneRouteEvaluationStatusEnum.Reachable, new int2(1, 0));
        Tick();
        Assert.AreEqual(DroneActionKindEnum.DropCargo, Assignment(assignment).NextAction);
        Assert.AreEqual(new int2(1, 0), Assignment(assignment).DropPosition);
        AssertCargo(worker, cargo, DroneCargoOriginEnum.Recovery);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void NewSiteOccupyingAssignedDropCell_RejectsTheDropAndRequestsAnotherPosition(bool afterExecution)
    {
        CreateSite(int2.zero, 1, 1);
        Entity assignment = CreateRetargetingAssignment(DroneCargoOriginEnum.Recovery, 1, 9);
        Entity worker = Assignment(assignment).Worker;
        Entity[] cargo = Stored(worker);
        int2 dropPosition = new int2(2, 0);
        Tick();
        PublishDropPosition(DropPositionSearch(assignment), DroneRouteEvaluationStatusEnum.Reachable, dropPosition);
        Tick();
        uint assignedRevision = Assignment(assignment).Revision;
        Assert.AreEqual(DroneActionKindEnum.DropCargo, Assignment(assignment).NextAction);
        Assert.AreEqual(dropPosition, Assignment(assignment).DropPosition);

        // 경로 배정 이후 새 현장이 목적 셀을 차지한 현재 상태를 준비한다.
        ObserveWorkerAt(worker, dropPosition);
        Entity request = SubmitCurrentAction(assignment);
        if (afterExecution)
        {
            _decision.Update();
            _reservation.Update();
            _execution.Update();
            CreateSite(dropPosition, 2, 1);
            ApplyDroneAndCommit();
        }
        else
        {
            CreateSite(dropPosition, 2, 1);
            Tick();
        }

        AssertResult(request, DroneItemTransferStatusEnum.Rejected, 0);
        Assert.AreEqual(DroneTaskAssignmentStateEnum.Retargeting, Assignment(assignment).State);
        AssertCargo(worker, cargo, DroneCargoOriginEnum.Recovery);
        Tick();

        Entity nextSearch = DropPositionSearch(assignment);
        var route = _entityManager.GetComponentData<DroneRouteEvaluationRequest>(nextSearch);
        Assert.AreEqual(assignedRevision, route.AssignmentRevision);
        Assert.AreEqual(_entityManager.GetComponentData<DroneWorkerObservation>(worker).Revision, route.WorkerObservationRevision);
        AssertCargo(worker, cargo, DroneCargoOriginEnum.Recovery);
    }

    private void Tick()
    {
        // 건물 반영을 확정한 다음 드론 관리 네 phase를 진행한다. 이동/관측/경로는 fixture 입력이다.
        Entities.PrepareConstructionConfiguration();
        _building.Update();
        _decision.Update();
        _reservation.Update();
        _execution.Update();
        ApplyDroneAndCommit();
    }

    private void ApplyDroneAndCommit()
    {
        _apply.Update();
        _commit.Update();
    }

    private Entity CreateSite(int2 position, ulong stamp, int required)
    {
        Entity site = _entityManager.CreateEntity();
        _entityManager.AddComponentData(site, new BuildingType(BuildingTypeEnum.ConstructionSite));
        _entityManager.AddComponentData(site, new ConstructionSite(BuildingTypeEnum.Storage));
        _entityManager.AddComponentData(site, new GridPosition(position));
        _entityManager.AddComponentData(site, new BuildingFootprint(new int2(1, 1)));
        _entityManager.AddComponentData(site, new Direction(DirectionEnum.Up));
        _entityManager.AddComponentData(site, new PlacementStamp(stamp, 0));
        _entityManager.AddBuffer<StoredItemElement>(site);
        _entityManager.AddBuffer<ConstructionMaterialRequirementElement>(site).Add(
            new ConstructionMaterialRequirementElement(ItemTypeEnum.Iron, required));
        Entity task = _entityManager.CreateEntity(typeof(DroneLogisticsTask));
        _entityManager.SetComponentData(task, new DroneLogisticsTask
        {
            Kind = DroneLogisticsTaskKindEnum.ConstructionSupply,
            State = DroneLogisticsTaskStateEnum.Open,
            Target = site,
            ItemType = ItemTypeEnum.Iron,
            CreationSequence = stamp
        });
        return site;
    }

    private Entity CreateStorage(int2 position, ulong stamp, int itemCount = 0, int slots = 4)
    {
        Entity storage = _entityManager.CreateEntity();
        _entityManager.AddComponentData(storage, new BuildingType(BuildingTypeEnum.Storage));
        _entityManager.AddComponentData(storage, new GridPosition(position));
        _entityManager.AddComponentData(storage, new PlacementStamp(stamp, 0));
        _entityManager.AddComponentData(storage, new Storage(slots));
        _entityManager.AddComponentData(storage, new StorageFilter(StorageFilterMode.AllowAll));
        _entityManager.AddBuffer<StoredItemElement>(storage);
        AddItems(storage, itemCount);
        return storage;
    }

    private Entity CreateWorker(int cargoCount = 0, DroneCargoOriginEnum origin = DroneCargoOriginEnum.None)
    {
        Entity worker = _entityManager.CreateEntity(typeof(DroneWorker));
        _entityManager.AddComponentData(worker, new DroneWorkerObservation
        {
            Position = float3.zero,
            Revision = 1,
            CanAcceptTask = true
        });
        _entityManager.AddComponentData(worker, new DroneWorkerAssignment());
        _entityManager.AddComponentData(worker, new DroneCargoState { Origin = origin });
        _entityManager.AddBuffer<StoredItemElement>(worker);
        AddItems(worker, cargoCount);
        return worker;
    }

    private Entity CreateRetargetingAssignment(DroneCargoOriginEnum origin, int quantity, ulong originalSequence,
        ulong taskSequence = 0)
    {
        Entity worker = CreateWorker(quantity, origin);
        Entity task = _entityManager.CreateEntity(typeof(DroneLogisticsTask));
        _entityManager.SetComponentData(task, new DroneLogisticsTask
        {
            Kind = origin == DroneCargoOriginEnum.Supply ? DroneLogisticsTaskKindEnum.ConstructionSupply : DroneLogisticsTaskKindEnum.WorldItemRecovery,
            State = DroneLogisticsTaskStateEnum.Closed,
            Target = origin == DroneCargoOriginEnum.Recovery ? Stored(worker)[0] : Entity.Null,
            ItemType = ItemTypeEnum.Iron,
            CreationSequence = taskSequence > 0 ? taskSequence : originalSequence,
            RecoveryReason = origin == DroneCargoOriginEnum.Recovery ? DroneRecoveryReasonEnum.DroneDrop : DroneRecoveryReasonEnum.None
        });
        Entity entity = _entityManager.CreateEntity(typeof(DroneTaskAssignment), typeof(ConstructionSupplyReservation));
        _entityManager.SetComponentData(entity, new DroneTaskAssignment
        {
            Worker = worker,
            Task = task,
            OriginalTaskCreationSequence = originalSequence,
            Source = origin == DroneCargoOriginEnum.Recovery ? Stored(worker)[0] : Entity.Null,
            Destination = Entity.Null,
            ItemType = ItemTypeEnum.Iron,
            AssignedQuantity = quantity,
            Revision = 1,
            State = DroneTaskAssignmentStateEnum.Retargeting,
            NextAction = DroneActionKindEnum.None
        });
        _entityManager.SetComponentData(entity, new ConstructionSupplyReservation
        {
            ItemType = ItemTypeEnum.Iron,
            AssignmentRevision = 1
        });
        _entityManager.SetComponentData(worker, new DroneWorkerAssignment { Assignment = entity });
        return entity;
    }

    private void AddItems(Entity owner, int quantity)
    {
        for (int i = 0; i < quantity; i++)
        {
            Entity item = _entityManager.CreateEntity();
            _entityManager.AddComponentData(item, new ItemIdentity(ItemTypeEnum.Iron));
            _entityManager.AddComponentData(item, new ItemOwnership(owner));
            _entityManager.AddComponentData(item, new GridPosition(int2.zero));
            _entityManager.AddComponentData(item, new Direction(DirectionEnum.Up));
            _entityManager.AddComponentData(item, LocalTransform.Identity);
            _entityManager.AddComponent<DisableRendering>(item);
            _entityManager.AddComponentData(item, new TransferOwnershipRequest());
            _entityManager.SetComponentEnabled<TransferOwnershipRequest>(item, false);
            _entityManager.AddComponentData(item, new DestroyItemRequest());
            _entityManager.SetComponentEnabled<DestroyItemRequest>(item, false);
            _entityManager.AddComponentData(item, new BeltMovementState());
            _entityManager.SetComponentEnabled<BeltMovementState>(item, false);
            _entityManager.GetBuffer<StoredItemElement>(owner).Add(new StoredItemElement(item, ItemTypeEnum.Iron, i / 2));
        }
    }

    private void AnswerRoutes(Func<DroneRouteEvaluationRequest, float?> distance)
    {
        foreach (Entity entity in Query<DroneRouteEvaluationRequest>())
        {
            var route = _entityManager.GetComponentData<DroneRouteEvaluationRequest>(entity);
            float? value = distance(route);
            var result = new DroneRouteEvaluationResult
            {
                EvaluationRevision = route.EvaluationRevision,
                Status = value.HasValue ? DroneRouteEvaluationStatusEnum.Reachable : DroneRouteEvaluationStatusEnum.Unreachable,
                TotalDistance = value ?? 0f
            };
            if (_entityManager.HasComponent<DroneRouteEvaluationResult>(entity)) _entityManager.SetComponentData(entity, result);
            else _entityManager.AddComponentData(entity, result);
        }
    }

    private Entity DropPositionSearch(Entity assignment)
    {
        Entity found = Entity.Null;
        foreach (Entity entity in Query<DroneRouteEvaluationRequest>())
        {
            var request = _entityManager.GetComponentData<DroneRouteEvaluationRequest>(entity);
            if (request.Assignment != assignment || !request.IsDropPositionSearch) continue;
            Assert.AreEqual(Entity.Null, found, "같은 배정의 배출 위치 검색을 중복 게시하지 않는다.");
            Assert.AreEqual(DroneRouteKindEnum.Direct, request.Kind);
            Assert.AreEqual(Entity.Null, request.Source);
            Assert.AreEqual(Entity.Null, request.Destination);
            found = entity;
        }
        Assert.AreNotEqual(Entity.Null, found, "현장 밖 배출 위치를 찾는 명시적 경로 요청이 필요하다.");
        return found;
    }

    private void PublishDropPosition(Entity requestEntity, DroneRouteEvaluationStatusEnum status, int2 position)
    {
        var request = _entityManager.GetComponentData<DroneRouteEvaluationRequest>(requestEntity);
        var result = new DroneRouteEvaluationResult
        {
            EvaluationRevision = request.EvaluationRevision,
            Status = status,
            TotalDistance = status == DroneRouteEvaluationStatusEnum.Reachable ? 2f : 0f,
            HasDropPosition = status == DroneRouteEvaluationStatusEnum.Reachable,
            DropPosition = position
        };
        if (_entityManager.HasComponent<DroneRouteEvaluationResult>(requestEntity)) _entityManager.SetComponentData(requestEntity, result);
        else _entityManager.AddComponentData(requestEntity, result);
    }

    private void ObserveWorkerAt(Entity worker, int2 cell)
    {
        // 실제 이동을 실행한 증거가 아니다. 후속 수행부가 게시할 관측 입력만 준비한다.
        var observation = _entityManager.GetComponentData<DroneWorkerObservation>(worker);
        observation.Position = new float3(cell.x + 0.25f, cell.y + 0.25f, 0f);
        observation.Revision++;
        _entityManager.SetComponentData(worker, observation);
    }

    private Entity SubmitCurrentAction(Entity assignment) => DroneActionRequestUtility.Submit(_entityManager, Ready(assignment));

    private DroneActionReadyRequest Ready(Entity entity)
    {
        var assignment = Assignment(entity);
        var observation = _entityManager.GetComponentData<DroneWorkerObservation>(assignment.Worker);
        return new DroneActionReadyRequest
        {
            Action = new DroneActionIdentity
            {
                Assignment = entity,
                AssignmentRevision = assignment.Revision,
                Sequence = assignment.LastAppliedActionSequence + 1,
                Worker = assignment.Worker,
                Kind = assignment.NextAction,
                Target = assignment.NextAction == DroneActionKindEnum.DropCargo ? Entity.Null : assignment.Destination
            },
            WorkerObservationRevision = observation.Revision,
            WorldPosition = (int2)math.floor(observation.Position.xy)
        };
    }

    private void AssertCargo(Entity worker, Entity[] items, DroneCargoOriginEnum origin)
    {
        CollectionAssert.AreEquivalent(items, Stored(worker));
        Assert.AreEqual(origin, _entityManager.GetComponentData<DroneCargoState>(worker).Origin);
        foreach (Entity item in items)
        {
            Assert.IsTrue(_entityManager.Exists(item));
            Assert.AreEqual(worker, _entityManager.GetComponentData<ItemOwnership>(item).Owner);
            Assert.IsTrue(_entityManager.HasComponent<DisableRendering>(item));
        }
    }

    private void AssertResult(Entity entity, DroneItemTransferStatusEnum status, int quantity)
    {
        Assert.IsTrue(_entityManager.Exists(entity));
        Assert.IsFalse(_entityManager.HasComponent<DroneActionReadyRequest>(entity));
        Assert.IsTrue(_entityManager.HasComponent<DroneItemTransferResult>(entity));
        var result = _entityManager.GetComponentData<DroneItemTransferResult>(entity);
        Assert.AreEqual(status, result.Status);
        Assert.AreEqual(quantity, result.MovedQuantity);
    }

    private Entity[] Stored(Entity owner)
    {
        var items = _entityManager.GetBuffer<StoredItemElement>(owner, true);
        var result = new Entity[items.Length];
        for (int i = 0; i < result.Length; i++) result[i] = items[i].ItemEntity;
        return result;
    }

    private Entity[] Query<T>() where T : unmanaged, IComponentData
    {
        using var query = _entityManager.CreateEntityQuery(ComponentType.ReadOnly<T>());
        using var entities = query.ToEntityArray(Allocator.Temp);
        return entities.ToArray();
    }

    private DroneTaskAssignment Assignment(Entity entity) => _entityManager.GetComponentData<DroneTaskAssignment>(entity);
    private ConstructionSupplyReservation Reservation(Entity entity) => _entityManager.GetComponentData<ConstructionSupplyReservation>(entity);
    private ConstructionMaterialRequirementElement Requirement(Entity site) =>
        _entityManager.GetBuffer<ConstructionMaterialRequirementElement>(site, true)[0];
}
