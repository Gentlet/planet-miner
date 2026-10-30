using System;
using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>실제 배차/이동/인계와 취소 승인/정산/EndSimulation 공개 경계를 연결한다.</summary>
public class DroneCancellationTests : EcsWorldTestFixture
{
    private SimulationPreparationGroup _preparation;
    private DroneDecisionGroup _decision;
    private DroneReservationGroup _reservation;
    private DroneExecutionGroup _execution;
    private DroneStateApplyGroup _apply;
    private EndSimulationEntityCommandBufferSystem _end;
    private double _time;

    public override void SetUp()
    {
        base.SetUp();
        _time = 0;
        Entities.PrepareSimulationConfiguration();
        using (var registry = _entityManager.CreateEntityQuery(typeof(ItemRegistry)))
        {
            var items = _entityManager.GetBuffer<ItemConfigElement>(registry.GetSingletonEntity());
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i].ItemType == ItemTypeEnum.Drone)
                    items[i] = new ItemConfigElement(ItemTypeEnum.Drone, 50);
            }
        }
        CreateGameplayPrefabDatabases();
        TestPrefabDatabaseFactory.CreateDrones(_entityManager);
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
        _decision.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneStationAutomationDecisionSystem>());
        _decision.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneStationLifecycleDecisionSystem>());
        _reservation.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneTaskReservationSystem>());
        _reservation.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneStationActivationReservationSystem>());
        _reservation.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneReturnReservationSystem>());
        _reservation.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneStationLifecycleReservationSystem>());
        _execution.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneItemTransferExecutionSystem>());
        _execution.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneTaskExecutionSystem>());
        _execution.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneMovementExecutionSystem>());
        _apply.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneTaskLifecycleApplySystem>());
        _apply.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneMovementApplySystem>());
        _apply.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneTaskAssignmentPublishSystem>());
        _apply.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneStationLifecycleApplySystem>());
        _preparation.SortSystems();
        _decision.SortSystems();
        _reservation.SortSystems();
        _execution.SortSystems();
        _apply.SortSystems();
    }

    [Test]
    public void CancelBeforeAssignment_RemovesPendingRouteWithoutPublishingAnAssignment()
    {
        Station(new int2(-10, 0));
        Entity source = Storage(new int2(4, 0), 3);
        Entity destination = Storage(new int2(8, 0));
        Entity worker = Worker(float3.zero, 100f);
        Entity root = CreateTransport(DroneLogisticsTaskKindEnum.StorageImport, source, destination);
        Tick();
        Assert.AreEqual(Entity.Null, Assignment(worker));
        Entity cancel = DroneTaskRequestUtility.SubmitCancel(_entityManager, root);
        Tick(1f);
        Assert.AreEqual(DroneTaskRequestStatusEnum.Cancelled, TaskResult(cancel).Status);
        Assert.AreEqual(Entity.Null, Assignment(worker));
        Assert.AreEqual(0, _entityManager.GetBuffer<StoredItemElement>(worker).Length);
        Assert.AreEqual(3, DroneSchedulingUtility.CountInventory(_entityManager, source, ItemTypeEnum.Iron));
        Assert.IsTrue(IsBlocked(DroneLogisticsTaskKindEnum.StorageImport, destination));
    }

    [TestCase(DroneLogisticsTaskKindEnum.StorageImport)]
    [TestCase(DroneLogisticsTaskKindEnum.StorageExport)]
    public void CancelMovingToSource_StopsThisTickAndReturnsQuantity(DroneLogisticsTaskKindEnum kind)
    {
        Station(new int2(-10, 0));
        Entity source = Storage(new int2(4, 0), 3);
        Entity destination = Storage(new int2(8, 0));
        Entity worker = Worker(float3.zero, 100f);
        Entity root = CreateTransport(kind, source, destination);
        Until(() => Assignment(worker) != Entity.Null);
        var before = _entityManager.GetComponentData<DroneBatteryState>(worker);
        float3 position = Position(worker);
        Entity cancel = DroneTaskRequestUtility.SubmitCancel(_entityManager, root);
        Entity duplicate = DroneTaskRequestUtility.SubmitCancel(_entityManager, root);
        Tick(1f);
        Assert.AreEqual(DroneTaskRequestStatusEnum.Cancelled, TaskResult(cancel).Status);
        Assert.AreEqual(DroneTaskRequestStatusEnum.Rejected, TaskResult(duplicate).Status);
        Assert.AreEqual(position, Position(worker));
        Assert.AreEqual(before.CurrentCharge, Charge(worker));
        Assert.AreEqual(before.FractionalCharge, _entityManager.GetComponentData<DroneBatteryState>(worker).FractionalCharge);
        Assert.AreEqual(Entity.Null, Assignment(worker));
        Assert.AreEqual(3, DroneSchedulingUtility.CountInventory(_entityManager, source, ItemTypeEnum.Iron));
        Assert.AreEqual(0, DroneSchedulingUtility.CountInventory(_entityManager, destination, ItemTypeEnum.Iron));
        if (_entityManager.Exists(root)) Assert.AreEqual(0, _entityManager.GetComponentData<DroneTransportProgress>(root).ReservedQuantity);
        Assert.IsTrue(IsBlocked(kind, kind == DroneLogisticsTaskKindEnum.StorageImport ? destination : source));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CancelAtCollection_PreventsAutomaticAndPreviouslySubmittedPickup(bool external)
    {
        Station(new int2(-10, 0));
        Entity source = Storage(new int2(4, 0), 3);
        Entity destination = Storage(new int2(8, 0));
        Entity worker = Worker(float3.zero, 100f);
        Entity root = CreateTransport(DroneLogisticsTaskKindEnum.StorageImport, source, destination);
        Until(() => Assignment(worker) != Entity.Null);
        Entity assignment = Assignment(worker);
        Until(() => _entityManager.GetComponentData<DroneTaskAssignment>(assignment).State ==
            DroneTaskAssignmentStateEnum.AwaitingCollection, 100f);
        _preparation.Update();
        var current = _entityManager.GetComponentData<DroneTaskAssignment>(assignment);
        Entity action = external ? SubmitAction(worker, assignment, current.Revision) : Entity.Null;
        float charge = Charge(worker);
        Entity cancel = DroneTaskRequestUtility.SubmitCancel(_entityManager, root);
        Tick(1f);
        Assert.AreEqual(DroneTaskRequestStatusEnum.Cancelled, TaskResult(cancel).Status);
        Assert.AreEqual(charge, Charge(worker));
        Assert.AreEqual(0, _entityManager.GetBuffer<StoredItemElement>(worker).Length);
        Assert.AreEqual(3, DroneSchedulingUtility.CountInventory(_entityManager, source, ItemTypeEnum.Iron));
        if (external)
        {
            Assert.IsTrue(DroneActionRequestUtility.TryConsumeResult(_entityManager, action, out var result));
            Assert.AreEqual(DroneItemTransferStatusEnum.Rejected, result.Status);
        }
    }

    [Test]
    public void UnrelatedCreate_DoesNotCancelExistingRootlessCargoDelivery()
    {
        Station(new int2(-10, 0));
        Entity destination = Storage(new int2(4, 0));
        Entity other = Storage(new int2(8, 0));
        Entity worker = Worker(new float3(4.5f, 0.5f, 0), 100f);
        Entity item = Entities.CreateStoredItem(worker, ItemTypeEnum.Iron);
        _entityManager.SetComponentData(worker, new DroneCargoState { Origin = DroneCargoOriginEnum.Recovery });
        Entity assignment = _entityManager.CreateEntity(typeof(DroneTaskAssignment));
        _entityManager.SetComponentData(assignment, new DroneTaskAssignment
        {
            Worker = worker, Destination = destination, ItemType = ItemTypeEnum.Iron,
            State = DroneTaskAssignmentStateEnum.AwaitingDelivery, NextAction = DroneActionKindEnum.StoreCargo,
            Revision = 1, OriginalTaskCreationSequence = 1, AssignedQuantity = 1
        });
        _entityManager.SetComponentData(worker, new DroneWorkerAssignment { Assignment = assignment });
        Entity create = DroneTaskRequestUtility.SubmitCreate(_entityManager,
            DroneLogisticsTaskKindEnum.StorageImport, other, ItemTypeEnum.Iron, 1);
        Tick();
        Assert.AreEqual(DroneTaskRequestStatusEnum.Created, TaskResult(create).Status);
        Assert.AreEqual(destination, _entityManager.GetComponentData<ItemOwnership>(item).Owner);
        Assert.AreEqual(DroneTaskAssignmentStateEnum.Completed,
            _entityManager.GetComponentData<DroneTaskAssignment>(assignment).State);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CancelAtDelivery_PreventsAutomaticAndPreviouslySubmittedDelivery(bool external)
    {
        Station(new int2(-10, 0));
        Entity source = Storage(int2.zero, 3);
        Entity destination = Storage(new int2(4, 0));
        Entity worker = Worker(new float3(0.5f, 0.5f, 0), 100f);
        Entity root = CreateTransport(DroneLogisticsTaskKindEnum.StorageImport, source, destination);
        Until(() => Assignment(worker) != Entity.Null);
        Entity assignment = Assignment(worker);
        Until(() => _entityManager.GetComponentData<DroneTaskAssignment>(assignment).State ==
            DroneTaskAssignmentStateEnum.AwaitingDelivery, 1f);
        _preparation.Update();
        Entity action = external ? SubmitAction(worker, assignment,
            _entityManager.GetComponentData<DroneTaskAssignment>(assignment).Revision) : Entity.Null;
        Entity cancel = DroneTaskRequestUtility.SubmitCancel(_entityManager, root);
        Tick();
        Assert.AreEqual(DroneTaskRequestStatusEnum.Cancelled, TaskResult(cancel).Status);
        Assert.AreEqual(0, DroneSchedulingUtility.CountInventory(_entityManager, destination, ItemTypeEnum.Iron));
        Assert.AreEqual(3, _entityManager.GetBuffer<StoredItemElement>(worker).Length);
        Assert.AreEqual(0, _entityManager.GetComponentData<DroneTransportProgress>(root).ReservedQuantity);
        Assert.AreEqual(0, _entityManager.GetComponentData<DroneTransportProgress>(root).DeliveredQuantity);
        if (external)
        {
            Assert.IsTrue(DroneActionRequestUtility.TryConsumeResult(_entityManager, action, out var result));
            Assert.AreEqual(DroneItemTransferStatusEnum.Rejected, result.Status);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CancelConstructionSupply_ReturnsReservationAndPreservesDeliveredMaterials(bool pickedUp)
    {
        Station(new int2(-10, 0));
        Storage(int2.zero, 3);
        Entity site = _entityManager.CreateEntity(typeof(ConstructionSite), typeof(BuildingType), typeof(GridPosition),
            typeof(BuildingFootprint), typeof(Direction), typeof(PlacementStamp));
        _entityManager.SetComponentData(site, new ConstructionSite(BuildingTypeEnum.Storage, 1));
        _entityManager.SetComponentData(site, new BuildingType(BuildingTypeEnum.ConstructionSite, 0));
        _entityManager.SetComponentData(site, new GridPosition(new int2(8, 0)));
        _entityManager.SetComponentData(site, new BuildingFootprint(new int2(1, 1)));
        _entityManager.SetComponentData(site, new Direction(DirectionEnum.Up));
        _entityManager.SetComponentData(site, new PlacementStamp(1, 0));
        _entityManager.AddBuffer<StoredItemElement>(site);
        _entityManager.AddBuffer<ConstructionMaterialRequirementElement>(site).Add(
            new ConstructionMaterialRequirementElement(ItemTypeEnum.Iron, 10));
        Entity worker = Worker(new float3(0.5f, 0.5f, 0), 100f);
        Entity create = DroneTaskRequestUtility.SubmitCreate(_entityManager,
            DroneLogisticsTaskKindEnum.ConstructionSupply, site, ItemTypeEnum.Iron);
        Tick();
        Entity root = TaskResult(create).Task;
        Until(() => Assignment(worker) != Entity.Null);
        if (pickedUp) Until(() => _entityManager.GetBuffer<StoredItemElement>(worker).Length == 3);
        Assert.AreEqual(3, _entityManager.GetBuffer<ConstructionMaterialRequirementElement>(site)[0].ReservedQuantity);
        Entity cancel = DroneTaskRequestUtility.SubmitCancel(_entityManager, root);
        Tick();
        Assert.AreEqual(DroneTaskRequestStatusEnum.Cancelled, TaskResult(cancel).Status);
        var requirement = _entityManager.GetBuffer<ConstructionMaterialRequirementElement>(site)[0];
        Assert.AreEqual(10, requirement.RequiredQuantity);
        Assert.AreEqual(0, requirement.DeliveredQuantity);
        Assert.AreEqual(0, requirement.ReservedQuantity);
        Assert.AreEqual(pickedUp ? 3 : 0, _entityManager.GetBuffer<StoredItemElement>(worker).Length);
    }

    [Test]
    public void CancelCarriedCargo_ReusesOriginalDestinationWithoutCreditingCancelledGoal_AndRejectsOldRevision()
    {
        Station(new int2(-10, 0));
        Entity source = Storage(int2.zero, 3);
        Entity destination = Storage(new int2(4, 0));
        Entity worker = Worker(new float3(0.5f, 0.5f, 0), 100f);
        Entity root = CreateTransport(DroneLogisticsTaskKindEnum.StorageImport, source, destination);
        Until(() => _entityManager.GetBuffer<StoredItemElement>(worker).Length == 3);
        Entity assignment = Assignment(worker);
        var original = _entityManager.GetComponentData<DroneTaskAssignment>(assignment);
        Tick(0.75f);
        float3 position = Position(worker);
        float charge = Charge(worker);
        Entity cancel = DroneTaskRequestUtility.SubmitCancel(_entityManager, root);
        Tick(1f);
        Assert.AreEqual(DroneTaskRequestStatusEnum.Cancelled, TaskResult(cancel).Status);
        Assert.AreEqual(position, Position(worker));
        Assert.AreEqual(charge, Charge(worker));
        Assert.AreEqual(3, _entityManager.GetBuffer<StoredItemElement>(worker).Length);
        Assert.AreEqual(DroneTaskAssignmentStateEnum.Retargeting,
            _entityManager.GetComponentData<DroneTaskAssignment>(assignment).State);
        Assert.AreEqual(0, _entityManager.GetComponentData<DroneTransportProgress>(root).ReservedQuantity);
        Assert.AreEqual(0, _entityManager.GetComponentData<DroneTransportProgress>(root).DeliveredQuantity);
        Until(() => _entityManager.GetComponentData<DroneTaskAssignment>(assignment).State ==
            DroneTaskAssignmentStateEnum.MovingToDestination);
        var retargeted = _entityManager.GetComponentData<DroneTaskAssignment>(assignment);
        Assert.AreEqual(destination, retargeted.Destination);
        Assert.Greater(retargeted.Revision, original.Revision);
        Assert.IsFalse(_entityManager.HasComponent<DroneTransportReservation>(assignment));
        Until(() => _entityManager.GetComponentData<DroneTaskAssignment>(assignment).State ==
            DroneTaskAssignmentStateEnum.AwaitingDelivery, 100f);
        _preparation.Update();
        Entity stale = SubmitAction(worker, assignment, original.Revision);
        Tick();
        Assert.IsTrue(DroneActionRequestUtility.TryConsumeResult(_entityManager, stale, out var result));
        Assert.AreEqual(DroneItemTransferStatusEnum.Rejected, result.Status);
        Assert.AreEqual(3, DroneSchedulingUtility.CountInventory(_entityManager, destination, ItemTypeEnum.Iron));
        Assert.AreEqual(0, _entityManager.GetBuffer<StoredItemElement>(worker).Length);
        if (_entityManager.Exists(root)) Assert.AreEqual(0, _entityManager.GetComponentData<DroneTransportProgress>(root).DeliveredQuantity);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RepeatedCancel_DoesNotStopCargoRecoveryOrRestoreAClearedBlock(bool recreate)
    {
        Station(new int2(-10, 0));
        Entity source = Storage(int2.zero, 3);
        Entity destination = Storage(new int2(4, 0));
        Entity worker = Worker(new float3(0.5f, 0.5f, 0), 100f);
        Entity root = CreateTransport(DroneLogisticsTaskKindEnum.StorageImport, source, destination);
        Until(() => _entityManager.GetBuffer<StoredItemElement>(worker).Length == 3);
        Tick(0.75f);
        Entity assignment = Assignment(worker);
        uint oldRevision = _entityManager.GetComponentData<DroneTaskAssignment>(assignment).Revision;
        Entity cancel = DroneTaskRequestUtility.SubmitCancel(_entityManager, root);
        Entity create = recreate ? DroneTaskRequestUtility.SubmitCreate(_entityManager,
            DroneLogisticsTaskKindEnum.StorageImport, destination, ItemTypeEnum.Iron, 1) : Entity.Null;
        Tick();
        Assert.AreEqual(DroneTaskRequestStatusEnum.Cancelled, TaskResult(cancel).Status);
        Assert.AreEqual(DroneLogisticsTaskStateEnum.Cancelled, _entityManager.GetComponentData<DroneLogisticsTask>(root).State);
        if (recreate)
        {
            var created = TaskResult(create);
            Assert.AreEqual(DroneTaskRequestStatusEnum.Created, created.Status);
            Assert.AreNotEqual(root, created.Task);
        }
        Assert.AreEqual(!recreate, IsBlocked(DroneLogisticsTaskKindEnum.StorageImport, destination));

        Entity duplicate = DroneTaskRequestUtility.SubmitCancel(_entityManager, root);
        Tick();
        Assert.AreEqual(DroneTaskRequestStatusEnum.Rejected, TaskResult(duplicate).Status);
        Until(() => _entityManager.GetComponentData<DroneTaskAssignment>(assignment).State ==
            DroneTaskAssignmentStateEnum.MovingToDestination);
        uint recoveryRevision = _entityManager.GetComponentData<DroneTaskAssignment>(assignment).Revision;
        Assert.AreEqual(destination, _entityManager.GetComponentData<DroneTaskAssignment>(assignment).Destination);
        Assert.Greater(recoveryRevision, oldRevision);
        float3 position = Position(worker);
        Entity delayed = DroneTaskRequestUtility.SubmitCancel(_entityManager, root);
        Tick(0.05f);
        Assert.AreEqual(DroneTaskRequestStatusEnum.Rejected, TaskResult(delayed).Status);
        Assert.AreEqual(recoveryRevision, _entityManager.GetComponentData<DroneTaskAssignment>(assignment).Revision);
        Assert.Greater(math.distance(position, Position(worker)), 0f);
        Assert.AreEqual(!recreate, IsBlocked(DroneLogisticsTaskKindEnum.StorageImport, destination));
        Until(() => _entityManager.GetBuffer<StoredItemElement>(worker).Length == 0, 100f);
        Assert.AreEqual(3, DroneSchedulingUtility.CountInventory(_entityManager, destination, ItemTypeEnum.Iron));
        if (_entityManager.Exists(root))
            Assert.AreEqual(0, _entityManager.GetComponentData<DroneTransportProgress>(root).DeliveredQuantity);
    }

    [Test]
    public void ClosedRecoveryRoot_WithCarriedItem_CanStillBeCancelled()
    {
        Station(new int2(-10, 0));
        Storage(new int2(5, 0));
        Entity item = _entityManager.CreateEntity(typeof(ItemIdentity), typeof(ItemOwnership), typeof(GridPosition),
            typeof(LocalTransform), typeof(DroneRecoveryPending));
        _entityManager.SetComponentData(item, new ItemIdentity(ItemTypeEnum.Iron));
        _entityManager.SetComponentData(item, ItemOwnership.WorldItem);
        _entityManager.SetComponentData(item, new GridPosition(int2.zero));
        _entityManager.SetComponentData(item, LocalTransform.Identity);
        Entity worker = Worker(float3.zero, 100f);
        Entity create = DroneTaskRequestUtility.SubmitCreate(_entityManager,
            DroneLogisticsTaskKindEnum.WorldItemRecovery, item, ItemTypeEnum.Iron);
        Tick();
        Entity root = TaskResult(create).Task;
        Until(() => _entityManager.GetBuffer<StoredItemElement>(worker).Length == 1);
        Tick();
        Assert.AreEqual(DroneLogisticsTaskStateEnum.Closed, _entityManager.GetComponentData<DroneLogisticsTask>(root).State);
        Entity cancel = DroneTaskRequestUtility.SubmitCancel(_entityManager, root);
        float3 position = Position(worker);
        Tick(1f);
        Assert.AreEqual(DroneTaskRequestStatusEnum.Cancelled, TaskResult(cancel).Status);
        Assert.AreEqual(position, Position(worker));
        Assert.AreEqual(worker, _entityManager.GetComponentData<ItemOwnership>(item).Owner);
        Assert.AreEqual(DroneTaskAssignmentStateEnum.Retargeting,
            _entityManager.GetComponentData<DroneTaskAssignment>(Assignment(worker)).State);
    }

    [Test]
    public void AlreadyDeliveredGoal_RejectsLateCancelWithoutBlockingOrUndoingDelivery()
    {
        Station(new int2(-10, 0));
        Entity source = Storage(int2.zero, 1);
        Entity destination = Storage(new int2(4, 0));
        Entity worker = Worker(new float3(0.5f, 0.5f, 0), 100f);
        Entity root = CreateTransport(DroneLogisticsTaskKindEnum.StorageImport, source, destination, 1);
        Until(() => DroneSchedulingUtility.CountInventory(_entityManager, destination, ItemTypeEnum.Iron) == 1, 1f);
        Entity cancel = DroneTaskRequestUtility.SubmitCancel(_entityManager, root);
        Tick();
        Assert.AreEqual(DroneTaskRequestStatusEnum.Rejected, TaskResult(cancel).Status);
        Assert.AreEqual(1, DroneSchedulingUtility.CountInventory(_entityManager, destination, ItemTypeEnum.Iron));
        Assert.IsFalse(IsBlocked(DroneLogisticsTaskKindEnum.StorageImport, destination));
    }

    [Test]
    public void CancelThenCreate_ReplacesOnlyTheOldRootAndClearsItsGenerationBlock()
    {
        Station(new int2(-10, 0));
        Entity source = Storage(int2.zero, 3);
        Entity destination = Storage(new int2(4, 0));
        Entity worker = Worker(new float3(0.5f, 0.5f, 0), 100f);
        Entity oldRoot = CreateTransport(DroneLogisticsTaskKindEnum.StorageImport, source, destination);
        Until(() => Assignment(worker) != Entity.Null);
        Entity cancel = DroneTaskRequestUtility.SubmitCancel(_entityManager, oldRoot);
        Entity create = DroneTaskRequestUtility.SubmitCreate(_entityManager,
            DroneLogisticsTaskKindEnum.StorageImport, destination, ItemTypeEnum.Iron, 1);
        Tick();
        Assert.AreEqual(DroneTaskRequestStatusEnum.Cancelled, TaskResult(cancel).Status);
        var created = TaskResult(create);
        Assert.AreEqual(DroneTaskRequestStatusEnum.Created, created.Status);
        Assert.AreNotEqual(oldRoot, created.Task);
        Assert.AreEqual(1, _entityManager.GetComponentData<DroneTransportRequirement>(created.Task).Quantity);
        Assert.IsFalse(IsBlocked(DroneLogisticsTaskKindEnum.StorageImport, destination));
        Assert.AreEqual(0, _entityManager.GetBuffer<StoredItemElement>(worker).Length);
    }

    [Test]
    public void CancelDockedCargo_ReleasesOldGoalBeforeNextTickStationResume()
    {
        var bounds = new DroneStationActivityBounds { MinX = -100, MinY = -100, MaxX = 250, MaxY = 100 };
        Entity station = Station(new int2(60, 0), bounds: bounds);
        Station(new int2(120, 0), network: station, stamp: 2, bounds: bounds);
        Entity source = Storage(int2.zero, 3);
        Entity destination = Storage(new int2(180, 0));
        Entity worker = Worker(new float3(0.5f, 0.5f, 0), 100f);
        Entity root = CreateTransport(DroneLogisticsTaskKindEnum.StorageImport, source, destination);
        Until(() => Assignment(worker) != Entity.Null);
        Until(() => _entityManager.HasComponent<DroneDocked>(worker), 100f);
        Entity assignment = Assignment(worker);
        Storage(new int2(61, 0));
        Entity cancel = DroneTaskRequestUtility.SubmitCancel(_entityManager, root);
        Tick();
        Assert.AreEqual(DroneTaskRequestStatusEnum.Cancelled, TaskResult(cancel).Status);
        Assert.IsTrue(_entityManager.HasComponent<DroneDocked>(worker));
        Assert.AreEqual(40f, Charge(worker));
        Assert.AreEqual(0, _entityManager.GetComponentData<DroneTransportProgress>(root).ReservedQuantity);
        Until(() => !_entityManager.HasComponent<DroneDocked>(worker));
        Assert.AreEqual(assignment, Assignment(worker));
        Assert.AreEqual(3, _entityManager.GetBuffer<StoredItemElement>(worker).Length);
    }

    private Entity CreateTransport(DroneLogisticsTaskKindEnum kind, Entity source, Entity destination, int quantity = 3)
    {
        Entity request = DroneTaskRequestUtility.SubmitCreate(_entityManager, kind,
            kind == DroneLogisticsTaskKindEnum.StorageImport ? destination : source, ItemTypeEnum.Iron, quantity);
        Tick();
        var result = TaskResult(request);
        Assert.AreEqual(DroneTaskRequestStatusEnum.Created, result.Status);
        return result.Task;
    }

    private DroneTaskRequestResult TaskResult(Entity request)
    {
        Assert.IsTrue(DroneTaskRequestUtility.TryConsumeResult(_entityManager, request, out var result));
        return result;
    }

    private bool IsBlocked(DroneLogisticsTaskKindEnum kind, Entity target)
    {
        using var query = _entityManager.CreateEntityQuery(typeof(DroneTaskGenerationBlockElement));
        var key = DroneTaskGenerationKey.From(kind, target, ItemTypeEnum.Iron);
        foreach (var block in _entityManager.GetBuffer<DroneTaskGenerationBlockElement>(query.GetSingletonEntity()))
            if (block.Key.Equals(key)) return true;
        return false;
    }

    private Entity SubmitAction(Entity worker, Entity assignment, uint revision)
    {
        var current = _entityManager.GetComponentData<DroneTaskAssignment>(assignment);
        bool collecting = current.NextAction == DroneActionKindEnum.CollectFromStorage ||
            current.NextAction == DroneActionKindEnum.RecoverWorldItem;
        return DroneActionRequestUtility.Submit(_entityManager, new DroneActionReadyRequest
        {
            Action = new DroneActionIdentity { Assignment = assignment, AssignmentRevision = revision, Worker = worker,
                Kind = current.NextAction, Target = collecting ? current.Source : current.Destination,
                Sequence = current.LastAppliedActionSequence + 1 },
            WorkerObservationRevision = _entityManager.GetComponentData<DroneWorkerObservation>(worker).Revision,
            WorldPosition = (int2)math.floor(Position(worker).xy)
        });
    }

    private Entity Station(int2 position, int slots = 2, Entity network = default,
        ulong stamp = 1, DroneStationActivityBounds? bounds = null)
    {
        Entity station = Entities.CreateStorage(position, new int2(1, 1), slotCount: slots);
        _entityManager.SetComponentData(station, new BuildingType(BuildingTypeEnum.DroneStation, 1));
        _entityManager.AddComponent<DroneStation>(station);
        _entityManager.AddComponentData(station, new DroneStationStorage { DedicatedSlotCount = slots });
        _entityManager.AddBuffer<DockedDroneElement>(station);
        _entityManager.AddComponentData(station, new PlacementStamp(stamp, 0));
        _entityManager.AddComponentData(station, new DroneStationNetworkDecision
        {
            RepresentativeStation = network == Entity.Null ? station : network,
            ActivityBounds = bounds ?? new DroneStationActivityBounds { MinX = -100, MinY = -100, MaxX = 100, MaxY = 100 }
        });
        return station;
    }

    private Entity Storage(int2 position, int quantity = 0)
    {
        Entity storage = Entities.CreateStorage(position, new int2(1, 1), slotCount: 1);
        for (int i = 0; i < quantity; i++) Entities.CreateStoredItem(storage, ItemTypeEnum.Iron);
        return storage;
    }

    private Entity Worker(float3 position, float charge)
    {
        Entity worker = _entityManager.CreateEntity(typeof(LocalTransform), typeof(LocalToWorld), typeof(DroneBatteryState));
        _entityManager.SetComponentData(worker, LocalTransform.FromPosition(position));
        _entityManager.SetComponentData(worker, new DroneBatteryState { CurrentCharge = charge });
        var ecb = _end.CreateCommandBuffer();
        Assert.IsTrue(DroneSpawnUtility.TryInitializeWorkerRole(_entityManager, ref ecb, worker, position, 100, out string error), error);
        _end.Update();
        return worker;
    }

    private Entity Assignment(Entity worker) => _entityManager.GetComponentData<DroneWorkerAssignment>(worker).Assignment;
    private float3 Position(Entity worker) => _entityManager.GetComponentData<LocalTransform>(worker).Position;
    private float Charge(Entity worker) => _entityManager.GetComponentData<DroneBatteryState>(worker).CurrentCharge;

    private string DescribeMovement(Entity worker)
    {
        var decision = _entityManager.GetComponentData<DroneMovementDecision>(worker);
        var movement = _entityManager.GetComponentData<DroneMovementState>(worker);
        var activity = _entityManager.GetComponentData<DroneActivityState>(worker);
        string dock = _entityManager.HasComponent<DroneDocked>(worker)
            ? _entityManager.GetComponentData<DroneDocked>(worker).Station.ToString() : "none";
        return $"Decision={decision.Purpose}, Station={decision.Station}, Ready={decision.Ready}; " +
            $"ChargingStation={movement.ChargingStation}, ReturnStation={movement.ReturnStation}, " +
            $"IsNormalReturn={movement.IsNormalReturn}, Activity={activity.Activity}, Dock={dock}";
    }

    private int WorkerCount()
    {
        using var query = _entityManager.CreateEntityQuery(typeof(DroneWorker));
        return query.CalculateEntityCount();
    }

    private Entity ActiveOf(params Entity[] drones)
    {
        foreach (Entity drone in drones)
            if (drone != Entity.Null && _entityManager.HasComponent<DroneWorker>(drone)) return drone;
        Assert.Fail("Expected an activated original drone Entity.");
        return Entity.Null;
    }


    private int StoredDroneCount(Entity first, Entity second)
    {
        return _entityManager.GetBuffer<StoredItemElement>(first).Length +
            (second == Entity.Null ? 0 : _entityManager.GetBuffer<StoredItemElement>(second).Length);
    }

    private void Until(Func<bool> condition, float dt = 0f)
    {
        for (int i = 0; i < 20 && !condition(); i++) Tick(dt);
        Assert.IsTrue(condition(), "The actual simulation pipeline did not reach the expected state.");
    }

    private void Tick(float dt = 0f)
    {
        TickWithoutCommit(dt);
        _end.Update();
    }

    private void TickWithoutCommit(float dt = 0f)
    {
        _time += dt;
        Simulation.SetDeltaTime(dt, _time);
        _preparation.Update();
        _decision.Update();
        _reservation.Update();
        _execution.Update();
        _apply.Update();
    }
}
