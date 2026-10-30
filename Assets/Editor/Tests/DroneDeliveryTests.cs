using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>실제 경로/이동/공개 도착/자동 인계로 일반 운반 완료와 잔여 화물/확보량을 검증한다.</summary>
public class DroneDeliveryTests : EcsWorldTestFixture
{
    private SimulationPreparationGroup _preparation;
    private DroneDecisionGroup _decision;
    private DroneReservationGroup _reservation;
    private DroneExecutionGroup _execution;
    private DroneStateApplyGroup _apply;
    private EndSimulationEntityCommandBufferSystem _end;
    private Entity _station;
    private Entity _source;
    private Entity _destination;
    private Entity _worker;
    private Entity _assignment;
    private Entity _task;
    private Entity _creation;
    private double _time;

    public override void SetUp()
    {
        base.SetUp();
        _time = 0;
        Entities.PrepareSimulationConfiguration();
        CreateGameplayPrefabDatabases();
        Assert.IsTrue(DroneConfigInitializationSystem.Initialize(_entityManager));
        _end = _world.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>();
        _preparation = _world.GetOrCreateSystemManaged<SimulationPreparationGroup>();
        _decision = _world.GetOrCreateSystemManaged<DroneDecisionGroup>();
        _reservation = _world.GetOrCreateSystemManaged<DroneReservationGroup>();
        _execution = _world.GetOrCreateSystemManaged<DroneExecutionGroup>();
        _apply = _world.GetOrCreateSystemManaged<DroneStateApplyGroup>();
        _preparation.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneWorkerObservationPreparationSystem>());
        _decision.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneFlightDecisionSystem>());
        _decision.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneTaskDecisionSystem>());
        _decision.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneActionRequestDecisionSystem>());
        _decision.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneItemTransferDecisionSystem>());
        _reservation.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneTaskReservationSystem>());
        _reservation.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneReturnReservationSystem>());
        _execution.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneItemTransferExecutionSystem>());
        _execution.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneTaskExecutionSystem>());
        _execution.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneMovementExecutionSystem>());
        _apply.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneTaskLifecycleApplySystem>());
        _apply.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneMovementApplySystem>());
        _apply.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneTaskAssignmentPublishSystem>());
        _preparation.SortSystems();
        _decision.SortSystems();
        _reservation.SortSystems();
        _execution.SortSystems();
        _apply.SortSystems();
        _station = Storage(new int2(-40, -40));
        _entityManager.SetComponentData(_station, new BuildingType(BuildingTypeEnum.DroneStation, 1));
        _entityManager.AddComponent<DroneStation>(_station);
        _entityManager.AddBuffer<DockedDroneElement>(_station);
        _entityManager.AddComponentData(_station, new DroneStationNetworkDecision
        {
            RepresentativeStation = _station,
            ActivityBounds = new DroneStationActivityBounds { MinX = -100, MinY = -100, MaxX = 100, MaxY = 100 }
        });
    }

    [Test]
    public void PartialDelivery_WaitsWithCargoAndClaim_ThenCompletesOnlyActualStorage()
    {
        PrepareTransport(DroneLogisticsTaskKindEnum.StorageImport);
        ReachDestinationWithCargo();
        FillDestinationLeaving(1);
        Tick();
        AssertProgress(1, 2);
        Assert.AreEqual(2, Cargo(_worker));
        Assert.AreEqual(2, _entityManager.GetComponentData<DroneTransportReservation>(_assignment).RemainingQuantity);
        Assert.AreEqual(DroneTaskAssignmentStateEnum.AwaitingDelivery, Assignment().State);
        Tick();
        AssertProgress(1, 2);
        Assert.AreEqual(2, Cargo(_worker));
        FreeDestinationSpace(2);
        Tick();
        AssertProgress(3, 0);
        Assert.AreEqual(0, Cargo(_worker));
        Assert.AreEqual(DroneTaskAssignmentStateEnum.Completed, Assignment().State);
        Assert.AreEqual(Entity.Null, _entityManager.GetComponentData<DroneWorkerAssignment>(_worker).Assignment);
        Tick();
        Assert.AreEqual(DroneLogisticsTaskStateEnum.Closed, _entityManager.GetComponentData<DroneLogisticsTask>(_task).State);
        Tick();
        Assert.IsFalse(_entityManager.Exists(_task));
    }

    [Test]
    public void CompetingDeliveries_ShareActualSpace_AndKeepOnlyTheirOwnRemainingClaims()
    {
        PrepareTransport(DroneLogisticsTaskKindEnum.StorageImport, 5);
        Entity second = Worker(new float3(0f, 1f, 0f), 100f);
        _assignment = WaitForAssignment(_worker);
        Entity secondAssignment = WaitForAssignment(second);
        _task = Assignment().Task;
        Assert.IsTrue(DroneTaskRequestUtility.TryConsumeResult(_entityManager, _creation, out _));
        Tick(100f);
        Tick();
        Assert.AreEqual(5, Cargo(_worker) + Cargo(second));
        Tick(100f);
        FillDestinationLeaving(2);
        Tick();
        AssertProgress(2, 3, 5);
        int firstRemaining = _entityManager.GetComponentData<DroneTransportReservation>(_assignment).RemainingQuantity;
        int secondRemaining = _entityManager.GetComponentData<DroneTransportReservation>(secondAssignment).RemainingQuantity;
        Assert.AreEqual(3, firstRemaining + secondRemaining);
        Assert.AreEqual(firstRemaining, Cargo(_worker));
        Assert.AreEqual(secondRemaining, Cargo(second));
        FreeDestinationSpace(3);
        Tick();
        AssertProgress(5, 0, 5);
        Assert.AreEqual(0, Cargo(_worker) + Cargo(second));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(3)]
    public void ConfirmedArrivalAtZeroBattery_DeliversAvailableItems_ThenPreservesAnyRemainder(int accepted)
    {
        PrepareTransport(DroneLogisticsTaskKindEnum.StorageExport, startAtSource: true);
        ReachDestinationWithCargo();
        Assert.AreEqual(0f, _entityManager.GetComponentData<DroneBatteryState>(_worker).CurrentCharge);
        FillDestinationLeaving(accepted);
        Tick();
        AssertProgress(accepted, 0);
        Assert.AreEqual(3 - accepted, Cargo(_worker));
        Assert.AreEqual(0, _entityManager.GetComponentData<DroneTransportReservation>(_assignment).RemainingQuantity);
        Assert.AreEqual(accepted == 3 ? DroneTaskAssignmentStateEnum.Completed : DroneTaskAssignmentStateEnum.Retargeting,
            Assignment().State);
        Assert.AreEqual(accepted == 3 ? Entity.Null : _assignment,
            _entityManager.GetComponentData<DroneWorkerAssignment>(_worker).Assignment);
        Tick();
        Assert.IsFalse(_entityManager.GetComponentData<DroneWorkerObservation>(_worker).CanAcceptTask);
        Assert.AreEqual(3 - accepted, Cargo(_worker));
    }

    [Test]
    public void ActualDelivery_IsCreditedOnlyToTheTaskInItsReservation()
    {
        PrepareTransport(DroneLogisticsTaskKindEnum.StorageExport);
        ReachDestinationWithCargo();
        Entity otherRequest = DroneTaskRequestUtility.SubmitCreate(_entityManager,
            DroneLogisticsTaskKindEnum.StorageImport, _destination, ItemTypeEnum.Iron, 3);
        Tick();
        AssertProgress(3, 0);
        Assert.IsTrue(DroneTaskRequestUtility.TryConsumeResult(_entityManager, otherRequest, out var other));
        Assert.AreNotEqual(_task, other.Task);
        var otherProgress = _entityManager.GetComponentData<DroneTransportProgress>(other.Task);
        Assert.AreEqual(0, otherProgress.DeliveredQuantity);
        Assert.AreEqual(0, otherProgress.ReservedQuantity);
        Assert.AreEqual(3, _entityManager.GetComponentData<DroneTransportRequirement>(other.Task).Quantity);
    }

    [Test]
    public void LostDestination_ReleasesOldClaimAndPreservesCargo_ThenDeliversTheRetargetedCargo()
    {
        PrepareTransport(DroneLogisticsTaskKindEnum.StorageImport);
        ReachDestinationWithCargo();
        _entityManager.DestroyEntity(_destination);
        Tick();
        AssertProgress(0, 0);
        Assert.AreEqual(3, Cargo(_worker));
        Assert.AreEqual(DroneTaskAssignmentStateEnum.Retargeting, Assignment().State);
        for (int i = 0; i < 12 && Cargo(_worker) != 0; i++)
        {
            Tick(100f);
        }
        Assert.AreEqual(0, Cargo(_worker));
        Assert.AreEqual(3, _entityManager.GetBuffer<StoredItemElement>(_source).Length);
    }

    [Test]
    public void ReceiverRefusal_DoesNotCreditDeliveryOrDiscardCargo()
    {
        PrepareTransport(DroneLogisticsTaskKindEnum.StorageImport);
        ReachDestinationWithCargo();
        _entityManager.SetComponentData(_destination, new StorageFilter(StorageFilterMode.Whitelist));
        Tick();
        AssertProgress(0, 0);
        Assert.AreEqual(3, Cargo(_worker));
        Assert.AreEqual(DroneTaskAssignmentStateEnum.Retargeting, Assignment().State);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AutomaticDrop_WaitsForPublishedArrival_AndPreservesItemIdentity(bool exhaustBattery)
    {
        PrepareDropPlan(exhaustBattery);
        Entity[] items = CargoItems();
        int2 dropPosition = Assignment().DropPosition;
        Tick();
        Assert.AreEqual(DroneTaskAssignmentStateEnum.MovingToDestination, Assignment().State);
        Assert.AreEqual(3, Cargo(_worker));
        Tick(100f);
        Assert.AreEqual(DroneTaskAssignmentStateEnum.AwaitingDelivery, Assignment().State);
        Assert.AreEqual(3, Cargo(_worker));
        float charge = _entityManager.GetComponentData<DroneBatteryState>(_worker).CurrentCharge;
        if (exhaustBattery) Assert.AreEqual(0f, charge);

        Tick();
        Assert.AreEqual(0, Cargo(_worker));
        Assert.AreEqual(charge, _entityManager.GetComponentData<DroneBatteryState>(_worker).CurrentCharge);
        Assert.AreEqual(Entity.Null, _entityManager.GetComponentData<DroneWorkerAssignment>(_worker).Assignment);
        Assert.AreEqual(DroneTaskAssignmentStateEnum.Completed, Assignment().State);
        foreach (Entity item in items)
        {
            Assert.IsTrue(_entityManager.Exists(item));
            Assert.AreEqual(ItemTypeEnum.Iron, _entityManager.GetComponentData<ItemIdentity>(item).Type);
            Assert.IsTrue(_entityManager.GetComponentData<ItemOwnership>(item).IsWorldItem);
            Assert.AreEqual(dropPosition, _entityManager.GetComponentData<GridPosition>(item).Value);
            Assert.IsTrue(_entityManager.HasComponent<DroneRecoveryPending>(item));
        }
        if (_entityManager.Exists(_task)) AssertProgress(0, 0);
    }

    [Test]
    public void DroppedItems_CreateOneRecoveryTaskEach_AndResumeWhenStorageReturns()
    {
        PrepareDropPlan();
        Entity[] items = CargoItems();
        Tick(100f);
        Tick();
        Assert.AreEqual(0, Cargo(_worker));
        for (int i = 0; i < 4; i++) Tick();
        foreach (Entity item in items) Assert.AreEqual(1, RecoveryTaskCount(item));

        Entity receiver = Storage(new int2(9, 0));
        for (int i = 0; i < 32 && _entityManager.GetBuffer<StoredItemElement>(receiver).Length < 3; i++) Tick(100f);
        Assert.AreEqual(3, _entityManager.GetBuffer<StoredItemElement>(receiver).Length);
        foreach (Entity item in items)
        {
            Assert.AreEqual(receiver, _entityManager.GetComponentData<ItemOwnership>(item).Owner);
            Assert.IsFalse(_entityManager.HasComponent<DroneRecoveryPending>(item));
        }
        Assert.AreEqual(0, Cargo(_worker));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CancelAtDrop_PreventsAutomaticAndPreviouslySubmittedDrop(bool external)
    {
        PrepareDropPlan();
        Tick(100f);
        Entity[] items = CargoItems();
        Entity action = external ? SubmitDrop() : Entity.Null;
        Entity cancel = DroneTaskRequestUtility.SubmitCancel(_entityManager, _task);
        Tick();
        Assert.IsTrue(DroneTaskRequestUtility.TryConsumeResult(_entityManager, cancel, out var result));
        Assert.AreEqual(DroneTaskRequestStatusEnum.Cancelled, result.Status);
        Assert.AreEqual(DroneTaskAssignmentStateEnum.Retargeting, Assignment().State);
        Assert.AreEqual(3, Cargo(_worker));
        foreach (Entity item in items)
        {
            Assert.AreEqual(_worker, _entityManager.GetComponentData<ItemOwnership>(item).Owner);
            Assert.IsFalse(_entityManager.HasComponent<DroneRecoveryPending>(item));
        }
        if (external)
        {
            Assert.IsTrue(DroneActionRequestUtility.TryConsumeResult(_entityManager, action, out var rejected));
            Assert.AreEqual(DroneItemTransferStatusEnum.Rejected, rejected.Status);
        }
    }

    [TestCase(DirectionEnum.Up)]
    [TestCase(DirectionEnum.Right)]
    public void NewSiteBlockingPublishedDrop_PreservesCargoAndUsesRealOutsideSearch(DirectionEnum direction)
    {
        PrepareDropPlan();
        Tick(100f);
        Entity[] items = CargoItems();
        int2 blockedPosition = Assignment().DropPosition;
        uint oldRevision = Assignment().Revision;
        Entity site = _entityManager.CreateEntity();
        _entityManager.AddComponentData(site, new BuildingType(BuildingTypeEnum.ConstructionSite, 0));
        _entityManager.AddComponentData(site, new ConstructionSite(BuildingTypeEnum.Storage, 1));
        _entityManager.AddComponentData(site, new GridPosition(blockedPosition));
        _entityManager.AddComponentData(site, new BuildingFootprint(new int2(2, 1)));
        _entityManager.AddComponentData(site, new Direction(direction));
        _entityManager.AddComponentData(site, new PlacementStamp(100, 0));
        _entityManager.AddBuffer<StoredItemElement>(site);
        _entityManager.AddBuffer<ConstructionMaterialRequirementElement>(site).Add(
            new ConstructionMaterialRequirementElement(ItemTypeEnum.Copper, 1));
        Tick();
        Assert.AreEqual(3, Cargo(_worker));
        Assert.AreEqual(DroneTaskAssignmentStateEnum.Retargeting, Assignment().State);
        foreach (Entity item in items) Assert.AreEqual(_worker, _entityManager.GetComponentData<ItemOwnership>(item).Owner);

        for (int i = 0; i < 20 && Assignment().Revision == oldRevision; i++) Tick();
        Assert.Greater(Assignment().Revision, oldRevision);
        Assert.AreEqual(DroneActionKindEnum.DropCargo, Assignment().NextAction);
        int2 position = Assignment().DropPosition;
        int2 size = BuildingFootprintUtility.GetEffectiveSize(new int2(2, 1), direction);
        Assert.IsFalse(position.x >= blockedPosition.x && position.x < blockedPosition.x + size.x &&
            position.y >= blockedPosition.y && position.y < blockedPosition.y + size.y);
        Tick(100f);
        Assert.AreEqual(DroneTaskAssignmentStateEnum.AwaitingDelivery, Assignment().State);
        Assert.AreEqual(3, Cargo(_worker));
        Tick();
        Assert.AreEqual(0, Cargo(_worker));
        foreach (Entity item in items)
        {
            Assert.IsTrue(_entityManager.GetComponentData<ItemOwnership>(item).IsWorldItem);
            Assert.AreEqual(position, _entityManager.GetComponentData<GridPosition>(item).Value);
        }
    }

    [Test]
    public void ZeroBatteryWithoutADropPlan_PreservesCargoInsteadOfStartingDrop()
    {
        PrepareTransport(DroneLogisticsTaskKindEnum.StorageImport, startAtSource: true);
        ReachDestinationWithCargo();
        Assert.AreEqual(0f, _entityManager.GetComponentData<DroneBatteryState>(_worker).CurrentCharge);
        Entity[] items = CargoItems();
        FillStorage(_source);
        FillStorage(_station);
        _entityManager.DestroyEntity(_destination);
        for (int i = 0; i < 6; i++) Tick();
        Assert.AreEqual(3, Cargo(_worker));
        Assert.AreNotEqual(DroneActionKindEnum.DropCargo, Assignment().NextAction);
        foreach (Entity item in items)
        {
            Assert.AreEqual(_worker, _entityManager.GetComponentData<ItemOwnership>(item).Owner);
            Assert.IsFalse(_entityManager.HasComponent<DroneRecoveryPending>(item));
        }
    }

    [Test]
    public void PendingExternalDrop_IsNotDuplicated_AndLateRepeatDoesNotMoveItemsAgain()
    {
        PrepareDropPlan();
        Tick(100f);
        Entity[] items = CargoItems();
        Entity external = SubmitDrop();
        var request = _entityManager.GetComponentData<DroneActionReadyRequest>(external);
        Tick();
        Assert.IsTrue(DroneActionRequestUtility.TryConsumeResult(_entityManager, external, out var result));
        Assert.AreEqual(DroneItemTransferStatusEnum.Completed, result.Status);
        Assert.AreEqual(3, result.MovedQuantity);
        using (var automatic = _entityManager.CreateEntityQuery(typeof(DroneAutomaticActionRequest)))
            Assert.AreEqual(0, automatic.CalculateEntityCount());
        Entity repeated = DroneActionRequestUtility.Submit(_entityManager, request);
        Tick();
        Assert.IsTrue(DroneActionRequestUtility.TryConsumeResult(_entityManager, repeated, out var rejected));
        Assert.AreEqual(DroneItemTransferStatusEnum.Rejected, rejected.Status);
        foreach (Entity item in items) Assert.IsTrue(_entityManager.GetComponentData<ItemOwnership>(item).IsWorldItem);
    }

    private Entity SubmitDrop()
    {
        _preparation.Update();
        var assignment = Assignment();
        return DroneActionRequestUtility.Submit(_entityManager, new DroneActionReadyRequest
        {
            Action = new DroneActionIdentity
            {
                Assignment = _assignment, AssignmentRevision = assignment.Revision,
                Worker = _worker, Kind = DroneActionKindEnum.DropCargo, Target = Entity.Null,
                Sequence = assignment.LastAppliedActionSequence + 1
            },
            WorkerObservationRevision = _entityManager.GetComponentData<DroneWorkerObservation>(_worker).Revision,
            WorldPosition = assignment.DropPosition
        });
    }

    private int RecoveryTaskCount(Entity item)
    {
        using var query = _entityManager.CreateEntityQuery(typeof(DroneLogisticsTask));
        using var tasks = query.ToComponentDataArray<DroneLogisticsTask>(Allocator.Temp);
        int count = 0;
        foreach (var task in tasks)
            if (task.Kind == DroneLogisticsTaskKindEnum.WorldItemRecovery && task.Target == item) count++;
        return count;
    }

    private void PrepareDropPlan(bool exhaustBattery = false)
    {
        PrepareTransport(DroneLogisticsTaskKindEnum.StorageImport, startAtSource: true);
        _entityManager.SetComponentData(_worker, new DroneBatteryState { CurrentCharge = exhaustBattery ? 4.5f : 100f });
        _assignment = WaitForAssignment(_worker);
        _task = Assignment().Task;
        Assert.IsTrue(DroneTaskRequestUtility.TryConsumeResult(_entityManager, _creation, out _));
        Tick();
        Assert.AreEqual(DroneTaskAssignmentStateEnum.AwaitingCollection, Assignment().State);
        Tick();
        Assert.AreEqual(3, Cargo(_worker));
        Tick(0.96875f);
        Assert.AreEqual(new float3(8.375f, 0.5f, 0), _entityManager.GetComponentData<LocalTransform>(_worker).Position);
        FillStorage(_source);
        FillStorage(_station);
        _entityManager.DestroyEntity(_destination);
        Tick();
        Assert.AreEqual(DroneTaskAssignmentStateEnum.Retargeting, Assignment().State);
        AssertProgress(0, 0);
        for (int i = 0; i < 16 && Assignment().NextAction != DroneActionKindEnum.DropCargo; i++) Tick();
        Assert.AreEqual(DroneActionKindEnum.DropCargo, Assignment().NextAction);
        Assert.AreEqual(DroneTaskAssignmentStateEnum.MovingToDestination, Assignment().State);
    }

    private void FillStorage(Entity storage)
    {
        using var registry = _entityManager.CreateEntityQuery(typeof(ItemRegistry));
        int maximum = ItemRegistry.GetMaxStack(_entityManager.GetBuffer<ItemConfigElement>(registry.GetSingletonEntity(), true), ItemTypeEnum.Iron);
        while (_entityManager.GetBuffer<StoredItemElement>(storage).Length < maximum)
            Entities.CreateStoredItem(storage, ItemTypeEnum.Iron);
    }

    private Entity[] CargoItems()
    {
        var cargo = _entityManager.GetBuffer<StoredItemElement>(_worker, true);
        var items = new Entity[cargo.Length];
        for (int i = 0; i < cargo.Length; i++) items[i] = cargo[i].ItemEntity;
        return items;
    }

    private void PrepareTransport(DroneLogisticsTaskKindEnum kind, int quantity = 3, bool startAtSource = false)
    {
        _source = Storage(new int2(4, 0), quantity);
        _destination = Storage(new int2(8, 0));
        _worker = Worker(startAtSource ? new float3(4.5f, 0.5f, 0f) : float3.zero, startAtSource ? 4f : 100f);
        Entity target = kind == DroneLogisticsTaskKindEnum.StorageImport ? _destination : _source;
        _creation = DroneTaskRequestUtility.SubmitCreate(_entityManager, kind, target, ItemTypeEnum.Iron, quantity);
    }

    private void ReachDestinationWithCargo()
    {
        _assignment = WaitForAssignment(_worker);
        _task = Assignment().Task;
        Assert.IsTrue(DroneTaskRequestUtility.TryConsumeResult(_entityManager, _creation, out _));
        Tick(100f);
        Assert.AreEqual(DroneTaskAssignmentStateEnum.AwaitingCollection, Assignment().State);
        Tick();
        Assert.AreEqual(DroneTaskAssignmentStateEnum.MovingToDestination, Assignment().State);
        Tick(100f);
        Assert.AreEqual(DroneTaskAssignmentStateEnum.AwaitingDelivery, Assignment().State);
        Assert.AreEqual(3, Cargo(_worker));
        Assert.AreEqual(0, _entityManager.GetBuffer<StoredItemElement>(_destination).Length);
    }

    private void FillDestinationLeaving(int free)
    {
        using var registry = _entityManager.CreateEntityQuery(typeof(ItemConfigElement));
        int maximum = ItemRegistry.GetMaxStack(_entityManager.GetBuffer<ItemConfigElement>(registry.GetSingletonEntity(), true), ItemTypeEnum.Iron);
        for (int i = 0; i < maximum - free; i++)
        {
            Entities.CreateStoredItem(_destination, ItemTypeEnum.Iron);
        }
    }

    private void FreeDestinationSpace(int count)
    {
        using var ecb = new EntityCommandBuffer(Allocator.Temp);
        for (int i = 0; i < count; i++)
        {
            Entity item = _entityManager.GetBuffer<StoredItemElement>(_destination)[0].ItemEntity;
            Assert.IsTrue(ItemOwnershipApplySystem.TryTransferItem(_entityManager, item, ItemTypeEnum.Iron,
                _destination, Entity.Null, -1, new int2(30, 30), ecb));
        }
        ecb.Playback(_entityManager);
    }

    private void AssertProgress(int delivered, int reserved, int goal = 3)
    {
        var progress = _entityManager.GetComponentData<DroneTransportProgress>(_task);
        Assert.AreEqual(delivered, progress.DeliveredQuantity);
        Assert.AreEqual(reserved, progress.ReservedQuantity);
        Assert.AreEqual(goal, _entityManager.GetComponentData<DroneTransportRequirement>(_task).Quantity);
    }

    private DroneTaskAssignment Assignment() => _entityManager.GetComponentData<DroneTaskAssignment>(_assignment);
    private int Cargo(Entity worker) => _entityManager.GetBuffer<StoredItemElement>(worker).Length;

    private Entity WaitForAssignment(Entity worker)
    {
        for (int i = 0; i < 12; i++)
        {
            Tick();
            Entity assignment = _entityManager.GetComponentData<DroneWorkerAssignment>(worker).Assignment;
            if (assignment != Entity.Null)
            {
                return assignment;
            }
        }
        Assert.Fail("실제 경로/배정이 공개되지 않았다.");
        return Entity.Null;
    }

    private void Tick(float dt = 0f)
    {
        _time += dt;
        Simulation.SetDeltaTime(dt, _time);
        _preparation.Update();
        _decision.Update();
        _reservation.Update();
        _execution.Update();
        _apply.Update();
        _end.Update();
    }

    private Entity Worker(float3 position, float charge)
    {
        Entity worker = _entityManager.CreateEntity(typeof(LocalTransform), typeof(LocalToWorld), typeof(DroneBatteryState));
        _entityManager.SetComponentData(worker, LocalTransform.FromPosition(position));
        _entityManager.SetComponentData(worker, new DroneBatteryState { CurrentCharge = charge });
        using var maximum = _entityManager.CreateEntityQuery(typeof(DroneBatteryConfig));
        var ecb = _end.CreateCommandBuffer();
        Assert.IsTrue(DroneSpawnUtility.TryInitializeWorkerRole(_entityManager, ref ecb, worker, position,
            maximum.GetSingleton<DroneBatteryConfig>().MaximumCharge, out var error), error);
        _end.Update();
        return worker;
    }

    private Entity Storage(int2 position, int quantity = 0)
    {
        Entity storage = _entityManager.CreateEntity(typeof(BuildingType), typeof(GridPosition), typeof(BuildingFootprint),
            typeof(Direction), typeof(Storage), typeof(StorageFilter), typeof(PlacementStamp));
        _entityManager.SetComponentData(storage, new BuildingType(BuildingTypeEnum.Storage, 1));
        _entityManager.SetComponentData(storage, new GridPosition(position));
        _entityManager.SetComponentData(storage, new BuildingFootprint(new int2(1, 1)));
        _entityManager.SetComponentData(storage, new Direction(DirectionEnum.Up));
        _entityManager.SetComponentData(storage, new Storage(1));
        _entityManager.SetComponentData(storage, new StorageFilter(StorageFilterMode.AllowAll));
        _entityManager.SetComponentData(storage, new PlacementStamp(1, 0));
        _entityManager.AddBuffer<StoredItemElement>(storage);
        for (int i = 0; i < quantity; i++)
        {
            Entities.CreateStoredItem(storage, ItemTypeEnum.Iron);
        }
        return storage;
    }
}
