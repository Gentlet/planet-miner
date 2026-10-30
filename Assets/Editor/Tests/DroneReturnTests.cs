using System;
using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>실제 귀환 이동과 정거장 요청/공개를 연결해 수납과 수요 기반 재출고를 검사한다.</summary>
public class DroneReturnTests : EcsWorldTestFixture
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
    public void NormalReturn_UsesActualMovementAndConsumption_ThenDocksWithoutInventingCharge()
    {
        Entity station = Station(int2.zero);
        Entity worker = Worker(new float3(4.5f, 0.5f, 0), 40);
        Tick(0.25f);
        Assert.AreEqual(3.5f, Position(worker).x, 0.0001f);
        Assert.AreEqual(39f, Charge(worker), 0.0001f);
        Assert.IsFalse(_entityManager.HasComponent<DroneDocked>(worker));
        Until(() => _entityManager.HasComponent<DroneDocked>(worker), 1f);
        Assert.AreEqual(new float3(0.5f, 0.5f, 0), Position(worker));
        Assert.AreEqual(36f, Charge(worker), 0.0001f);
        Assert.AreEqual(station, _entityManager.GetComponentData<DroneDocked>(worker).Station);
        Assert.IsTrue(_entityManager.HasComponent<DroneWorker>(worker));
        Assert.IsFalse(_entityManager.HasComponent<ItemIdentity>(worker));
        for (int i = 0; i < 4; i++) Tick(1f);
        Assert.AreEqual(36f, Charge(worker), 0.0001f);
        Assert.AreEqual(1, _entityManager.GetBuffer<DockedDroneElement>(station).Length);
    }

    [TestCase(0)]
    [TestCase(49)]
    public void FullChargeAtStation_StoresSameEntityOnlyAtCommit_AndStaysStoredWithoutDemand(int stackedDrones)
    {
        Entity station = Station(int2.zero, 1);
        for (int i = 0; i < stackedDrones; i++) Entities.CreateStoredItem(station, ItemTypeEnum.Drone);
        Entity worker = Worker(new float3(0.5f, 0.5f, 0), 100);
        // 실제 도착 위치만 준비하고 정거장 요청과 역할 전환은 시스템이 처리한다.
        for (int i = 0; i < 6 && _entityManager.HasComponent<DroneWorker>(worker); i++)
        {
            TickWithoutCommit();
            Assert.IsTrue(_entityManager.HasComponent<DroneWorker>(worker));
            Assert.IsFalse(_entityManager.HasComponent<ItemIdentity>(worker));
            _end.Update();
        }
        Assert.IsTrue(_entityManager.HasComponent<ItemIdentity>(worker));
        Assert.AreEqual(ItemTypeEnum.Drone, _entityManager.GetComponentData<ItemIdentity>(worker).Type);
        Assert.AreEqual(station, _entityManager.GetComponentData<ItemOwnership>(worker).Owner);
        Assert.AreEqual(stackedDrones + 1, _entityManager.GetBuffer<StoredItemElement>(station).Length);
        Assert.IsFalse(_entityManager.HasComponent<DroneWorker>(worker));
        for (int i = 0; i < 6; i++) Tick();
        Assert.IsTrue(_entityManager.HasComponent<ItemIdentity>(worker));
        Assert.AreEqual(stackedDrones + 1, _entityManager.GetBuffer<StoredItemElement>(station).Length);
        using var automatic = _entityManager.CreateEntityQuery(typeof(DroneAutomaticStationRequest));
        Assert.AreEqual(0, automatic.CalculateEntityCount());
    }

    [Test]
    public void ReturnSelection_PrefersCurrentNetworkBeforeCloserOtherNetwork_ThenFallsBackWhenFull()
    {
        Entity same = Station(new int2(10, 0), 1);
        Entity other = Station(new int2(1, 0), 1, bounds: new DroneStationActivityBounds
        {
            MinX = 1, MinY = 0, MaxX = 1, MaxY = 0
        });
        Entity worker = Worker(new float3(0.5f, 0.5f, 0), 40);
        Tick();
        Assert.AreEqual(same, _entityManager.GetComponentData<DroneMovementState>(worker).ReturnStation);
        Entities.CreateStoredItem(same, ItemTypeEnum.Drone);
        Tick();
        Assert.AreEqual(other, _entityManager.GetComponentData<DroneMovementState>(worker).ReturnStation);
        Assert.AreEqual(40, Charge(worker));
    }

    [Test]
    public void EqualDistanceStations_UsePlacementStamp_AndReplaceDisappearedDestination()
    {
        Entity left = Station(new int2(-4, 0), 1, stamp: 2);
        Entity right = Station(new int2(4, 0), 1, network: left, stamp: 1);
        Entity worker = Worker(new float3(0.5f, 0.5f, 0), 40);
        Tick();
        Assert.AreEqual(right, _entityManager.GetComponentData<DroneMovementState>(worker).ReturnStation);
        _entityManager.DestroyEntity(right);
        Tick();
        Assert.AreEqual(left, _entityManager.GetComponentData<DroneMovementState>(worker).ReturnStation);
        Until(() => _entityManager.HasComponent<DroneDocked>(worker), 1f);
        Assert.AreEqual(left, _entityManager.GetComponentData<DroneDocked>(worker).Station);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void NoAvailableStation_PreservesActiveWorkerPositionAndBattery(bool fullStation)
    {
        if (fullStation)
        {
            Entity station = Station(int2.zero, 1);
            Entities.CreateStoredItem(station, ItemTypeEnum.Drone);
        }
        Entity worker = Worker(new float3(4.5f, 0.5f, 0), 40);
        for (int i = 0; i < 5; i++) Tick(1f);
        Assert.AreEqual(new float3(4.5f, 0.5f, 0), Position(worker));
        Assert.AreEqual(40, Charge(worker));
        Assert.IsTrue(_entityManager.HasComponent<DroneWorker>(worker));
        Assert.IsFalse(_entityManager.HasComponent<DroneDocked>(worker));
        Assert.IsFalse(_entityManager.HasComponent<ItemIdentity>(worker));
    }

    [Test]
    public void ReturningWorkers_CompeteForOneDockSlot_WithoutOverbookingOrLosingEitherWorker()
    {
        Entity station = Station(int2.zero, 1);
        Entity first = Worker(new float3(1.5f, 0.5f, 0), 40);
        Entity second = Worker(new float3(-0.5f, 0.5f, 0), 40);
        for (int i = 0; i < 8; i++) Tick(1f);
        Assert.AreEqual(1, _entityManager.GetBuffer<DockedDroneElement>(station).Length);
        Assert.AreEqual(1, (_entityManager.HasComponent<DroneDocked>(first) ? 1 : 0) +
            (_entityManager.HasComponent<DroneDocked>(second) ? 1 : 0));
        Assert.IsTrue(_entityManager.HasComponent<DroneWorker>(first));
        Assert.IsTrue(_entityManager.HasComponent<DroneWorker>(second));
        Assert.AreEqual(0, _entityManager.GetBuffer<StoredItemElement>(station).Length);
    }

    [Test]
    public void EmergencyArrival_LosingItsDockSlotBeforeRequest_SelectsAnotherStationWithoutCharging()
    {
        Entity first = Station(int2.zero, 1);
        Entity alternate = Station(new int2(4, 0), 1, network: first, stamp: 2);
        Entity worker = Worker(new float3(2.5f, 0.5f, 0), 0);
        Tick(4f);
        Assert.AreEqual(first, _entityManager.GetComponentData<DroneMovementState>(worker).ChargingStation);
        Assert.AreEqual(new float3(0.5f, 0.5f, 0), Position(worker));
        Assert.IsFalse(_entityManager.HasComponent<DroneDocked>(worker));
        // 도착 공개 뒤 정박 요청 전 슬롯을 다른 정상 보관 드론이 점유한다.
        Entity occupant = Entities.CreateStoredItem(first, ItemTypeEnum.Drone);
        Tick();
        Assert.AreEqual(alternate, _entityManager.GetComponentData<DroneMovementState>(worker).ReturnStation,
            DescribeMovement(worker));
        Assert.AreEqual(0f, Charge(worker));
        Until(() => _entityManager.HasComponent<DroneDocked>(worker), 2f);
        Assert.AreEqual(alternate, _entityManager.GetComponentData<DroneDocked>(worker).Station);
        Assert.AreEqual(0f, Charge(worker));
        Assert.AreEqual(first, _entityManager.GetComponentData<ItemOwnership>(occupant).Owner);
        Assert.IsTrue(_entityManager.HasComponent<DroneWorker>(worker));
    }

    [Test]
    public void TaskAppearingDuringNormalReturn_IsAssignedBeforeDocking_AtTheCurrentPosition()
    {
        Station(new int2(-10, 0));
        Entity worker = Worker(new float3(0.5f, 0.5f, 0), 80);
        Tick(0.25f);
        float3 returningPosition = Position(worker);
        Assert.AreEqual(-0.5f, returningPosition.x, 0.0001f);
        Storage(new int2(3, 0), 3);
        Entity destination = Storage(new int2(7, 0));
        DroneTaskRequestUtility.SubmitCreate(_entityManager, DroneLogisticsTaskKindEnum.StorageImport,
            destination, ItemTypeEnum.Iron, 3);
        Until(() => Assignment(worker) != Entity.Null, 0.25f);
        Entity assignment = Assignment(worker);
        float3 assignedPosition = Position(worker);
        float assignedCharge = Charge(worker);
        Assert.Greater(assignedPosition.x, -9.5f, "Return must yield before reaching the station.");
        Assert.IsFalse(_entityManager.HasComponent<DroneDocked>(worker));
        Tick(0.25f);
        Assert.Greater(Position(worker).x, assignedPosition.x);
        Assert.AreEqual(assignment, Assignment(worker));
        Assert.AreEqual(Entity.Null, _entityManager.GetComponentData<DroneMovementState>(worker).ReturnStation);
        Assert.AreEqual(assignedCharge - 1f, Charge(worker), 0.0001f);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void StoredDroneDemand_ActivatesOnlyRequiredCount_ThenPublishesAnAssignmentOnALaterTick(bool multipleStations)
    {
        Entity station = Station(int2.zero, 2);
        Entity first = Entities.CreateStoredItem(station, ItemTypeEnum.Drone);
        Entity second = Entities.CreateStoredItem(station, ItemTypeEnum.Drone);
        Entity third = Entities.CreateStoredItem(station, ItemTypeEnum.Drone);
        Entity alternate = multipleStations ? Station(new int2(10, 0), 2, network: station) : Entity.Null;
        Entity fourth = multipleStations ? Entities.CreateStoredItem(alternate, ItemTypeEnum.Drone) : Entity.Null;
        Entity fifth = multipleStations ? Entities.CreateStoredItem(alternate, ItemTypeEnum.Drone) : Entity.Null;
        int totalStored = multipleStations ? 5 : 3;
        for (int i = 0; i < 4; i++) Tick();
        Assert.AreEqual(3, _entityManager.GetBuffer<StoredItemElement>(station).Length);
        Assert.AreEqual(0, WorkerCount());
        Storage(new int2(3, 0), 3);
        Entity destination = Storage(new int2(7, 0));
        DroneTaskRequestUtility.SubmitCreate(_entityManager, DroneLogisticsTaskKindEnum.StorageImport,
            destination, ItemTypeEnum.Iron, 3);
        Until(() => WorkerCount() == 1);
        Entity activated = ActiveOf(first, second, third, fourth, fifth);
        Assert.AreEqual(Entity.Null, Assignment(activated));
        Assert.IsFalse(_entityManager.GetComponentData<DroneWorkerObservation>(activated).CanAcceptTask);
        Assert.IsFalse(_entityManager.HasComponent<ItemIdentity>(activated));
        Assert.AreEqual(totalStored - 1, StoredDroneCount(station, alternate));
        Until(() => Assignment(activated) != Entity.Null);
        for (int i = 0; i < 5; i++) Tick();
        Assert.AreEqual(1, WorkerCount());
        Assert.AreEqual(totalStored - 1, StoredDroneCount(station, alternate));
        Entity assignment = Assignment(activated);
        Assert.AreEqual(3, _entityManager.GetComponentData<DroneTransportReservation>(assignment).RemainingQuantity);
    }

    [Test]
    public void ReturningCargo_RetargetsToAnAvailableReceiver_WithoutLosingIdentityOrRestartingItsRevision()
    {
        Entity station = Station(new int2(-10, 0));
        Entity worker = Worker(new float3(0.5f, 0.5f, 0), 80);
        Entity item = Entities.CreateStoredItem(worker, ItemTypeEnum.Iron);
        _entityManager.SetComponentData(worker, new DroneCargoState { Origin = DroneCargoOriginEnum.Recovery });
        Entity task = _entityManager.CreateEntity(typeof(DroneLogisticsTask));
        _entityManager.SetComponentData(task, new DroneLogisticsTask
        {
            Kind = DroneLogisticsTaskKindEnum.WorldItemRecovery,
            State = DroneLogisticsTaskStateEnum.Closed,
            CreationSequence = 1,
            ItemType = ItemTypeEnum.Iron,
            Target = item
        });
        Entity assignment = _entityManager.CreateEntity(typeof(DroneTaskAssignment));
        _entityManager.SetComponentData(assignment, new DroneTaskAssignment
        {
            Worker = worker, Task = task, OriginalTaskCreationSequence = 1, Revision = 1,
            ItemType = ItemTypeEnum.Iron, State = DroneTaskAssignmentStateEnum.Retargeting
        });
        _entityManager.SetComponentData(worker, new DroneWorkerAssignment { Assignment = assignment });
        // 이전 작업 정산으로 보존된 화물과 진행 중 정상 귀환을 입력으로 준비한다.
        _entityManager.SetComponentData(worker, new DroneMovementState { ReturnStation = station, IsNormalReturn = true });
        Entity receiver = Storage(new int2(4, 0));
        Until(() => _entityManager.GetComponentData<DroneTaskAssignment>(assignment).State ==
            DroneTaskAssignmentStateEnum.MovingToDestination, 0.25f);
        var retargeted = _entityManager.GetComponentData<DroneTaskAssignment>(assignment);
        Assert.AreEqual(receiver, retargeted.Destination);
        Assert.AreEqual(DroneActionKindEnum.StoreCargo, retargeted.NextAction);
        Assert.Greater(retargeted.Revision, 1);
        Assert.AreEqual(worker, _entityManager.GetComponentData<ItemOwnership>(item).Owner);
        Assert.AreEqual(item, _entityManager.GetBuffer<StoredItemElement>(worker)[0].ItemEntity);
        Tick(0.25f);
        Assert.AreEqual(retargeted.Revision, _entityManager.GetComponentData<DroneTaskAssignment>(assignment).Revision);
        Assert.AreEqual(assignment, Assignment(worker));
        Assert.AreEqual(Entity.Null, _entityManager.GetComponentData<DroneMovementState>(worker).ReturnStation);
        Assert.AreEqual(worker, _entityManager.GetComponentData<ItemOwnership>(item).Owner);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DockedCargo_InvalidatedDestination_ReactivatesAndRetargetsWithoutCharging(bool disabled)
    {
        var bounds = new DroneStationActivityBounds { MinX = -100, MinY = -100, MaxX = 250, MaxY = 100 };
        Entity station = Station(new int2(60, 0), bounds: bounds);
        Station(new int2(120, 0), network: station, stamp: 2, bounds: bounds);
        Storage(int2.zero, 3);
        Entity destination = Storage(new int2(180, 0));
        Entity worker = Worker(new float3(0.5f, 0.5f, 0), 100f);
        DroneTaskRequestUtility.SubmitCreate(_entityManager, DroneLogisticsTaskKindEnum.StorageImport,
            destination, ItemTypeEnum.Iron, 3);
        Until(() => Assignment(worker) != Entity.Null);
        Entity assignment = Assignment(worker);
        Until(() => _entityManager.HasComponent<DroneDocked>(worker), 100f);
        Assert.AreEqual(station, _entityManager.GetComponentData<DroneDocked>(worker).Station);
        Assert.AreEqual(DroneTaskAssignmentStateEnum.MovingToDestination,
            _entityManager.GetComponentData<DroneTaskAssignment>(assignment).State);
        Assert.AreEqual(40f, Charge(worker), 0.0001f);
        using var cargo = _entityManager.GetBuffer<StoredItemElement>(worker).ToNativeArray(Unity.Collections.Allocator.Temp);
        Assert.AreEqual(3, cargo.Length);

        if (disabled)
        {
            _entityManager.AddComponent<Disabled>(destination);
        }
        else
        {
            _entityManager.AddComponent<PendingBuildingDemolition>(destination);
        }
        Entity receiver = Storage(new int2(61, 0));
        Tick();
        Assert.AreEqual(DroneTaskAssignmentStateEnum.Retargeting,
            _entityManager.GetComponentData<DroneTaskAssignment>(assignment).State);
        Assert.IsTrue(_entityManager.HasComponent<DroneDocked>(worker));
        Until(() => !_entityManager.HasComponent<DroneDocked>(worker));
        Assert.AreEqual(new float3(60.5f, 0.5f, 0), Position(worker));
        Assert.AreEqual(40f, Charge(worker), 0.0001f);
        Assert.AreEqual(assignment, Assignment(worker));
        Until(() => _entityManager.GetComponentData<DroneTaskAssignment>(assignment).State ==
            DroneTaskAssignmentStateEnum.MovingToDestination);
        var retargeted = _entityManager.GetComponentData<DroneTaskAssignment>(assignment);
        Assert.AreEqual(receiver, retargeted.Destination);
        Assert.AreEqual(DroneActionKindEnum.StoreCargo, retargeted.NextAction);
        foreach (var item in cargo)
        {
            Assert.AreEqual(worker, _entityManager.GetComponentData<ItemOwnership>(item.ItemEntity).Owner);
            Assert.AreEqual(ItemTypeEnum.Iron, _entityManager.GetComponentData<ItemIdentity>(item.ItemEntity).Type);
        }
        Tick(0.1f);
        Assert.AreEqual(retargeted.Revision, _entityManager.GetComponentData<DroneTaskAssignment>(assignment).Revision);
        Assert.AreEqual(assignment, Assignment(worker));
        Tick(1f);
        Until(() => _entityManager.GetBuffer<StoredItemElement>(worker).IsEmpty);
        Assert.AreEqual(3, DroneSchedulingUtility.CountInventory(_entityManager, receiver, ItemTypeEnum.Iron));
        Assert.AreEqual(39f, Charge(worker), 0.0001f);
        foreach (var item in cargo)
        {
            Assert.AreEqual(receiver, _entityManager.GetComponentData<ItemOwnership>(item.ItemEntity).Owner);
        }
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
