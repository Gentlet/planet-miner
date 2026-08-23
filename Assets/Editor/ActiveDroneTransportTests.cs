using NUnit.Framework;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

public class ActiveDroneTransportTests
{
    private World _world;
    private EntityManager _entityManager;
    private DroneTaskReservationSystem _reservationSystem;
    private DroneStationNetworkSystem _networkSystem;
    private DroneDispatchSystem _dispatchSystem;
    private DroneMovementSystem _movementSystem;
    private DroneCargoTransferSystem _cargoSystem;
    private DroneRecoverySystem _recoverySystem;
    private Entity _station;
    private double _elapsedTime;

    [SetUp]
    public void SetUp()
    {
        _world = new World(nameof(ActiveDroneTransportTests));
        _entityManager = _world.EntityManager;
        _world.GetOrCreateSystemManaged<ChunkMapSystem>();
        _world.GetOrCreateSystemManaged<ItemTrackingSystem>();
        _world.GetOrCreateSystemManaged<ItemStorageSystem>();
        _reservationSystem = _world.GetOrCreateSystemManaged<
            DroneTaskReservationSystem>();
        _networkSystem = _world.GetOrCreateSystemManaged<
            DroneStationNetworkSystem>();
        _dispatchSystem = _world.GetOrCreateSystemManaged<
            DroneDispatchSystem>();
        _movementSystem = _world.GetOrCreateSystemManaged<
            DroneMovementSystem>();
        _cargoSystem = _world.GetOrCreateSystemManaged<
            DroneCargoTransferSystem>();
        _recoverySystem = _world.GetOrCreateSystemManaged<
            DroneRecoverySystem>();
        CreateStorageLimit();
        _station = CreateStation(int2.zero);
        _networkSystem.Update();
    }

    [TearDown]
    public void TearDown()
    {
        _world.Dispose();
    }

    [Test]
    public void ValidationBootstrapCreatesOneFullyChargedStoredDrone()
    {
        Entity prefab = _entityManager.CreateEntity(
            typeof(Prefab),
            typeof(LocalTransform));
        Entity configEntity = _entityManager.CreateEntity(
            typeof(DroneConfig));
        _entityManager.SetComponentData(
            configEntity,
            CreateDroneConfig());
        Entity prefabReference = _entityManager.CreateEntity(
            typeof(DronePrefab));
        _entityManager.SetComponentData(
            prefabReference,
            new DronePrefab { value = prefab });
        DroneValidationBootstrapSystem bootstrap = _world
            .GetOrCreateSystemManaged<DroneValidationBootstrapSystem>();

        bootstrap.Update();
        bootstrap.Update();

        using EntityQuery droneQuery = _entityManager.CreateEntityQuery(
            typeof(ActiveDrone),
            typeof(ValidationDrone));
        Assert.That(droneQuery.CalculateEntityCount(), Is.EqualTo(1));
        Entity drone = droneQuery.GetSingletonEntity();
        DroneBattery battery = _entityManager
            .GetComponentData<DroneBattery>(drone);
        Assert.That(battery.current, Is.EqualTo(battery.maximum));
        Assert.That(
            _entityManager.GetComponentData<DroneState>(drone).value,
            Is.EqualTo(DroneStateEnum.Stored));
    }

    [Test]
    public void DroneCompletesTransportAndReturnsWithDistanceBasedBatteryUse()
    {
        Entity source = CreateStorage(new int2(4, 0), 10);
        Entity destination = CreateStorage(new int2(8, 0), 10);
        Entity item = CreateStoredItem(source, ItemTypeEnum.Iron_Ore);
        Entity task = CreateTask(1);
        CreateReservation(task, source, destination, ItemTypeEnum.Iron_Ore, 1);
        _reservationSystem.Update();
        Entity drone = CreateDrone(3, 100f);

        _dispatchSystem.Update();
        AdvanceTransport(1f);
        _reservationSystem.Update();
        AdvanceTransport(1f);
        AdvanceTransport(1f);
        AdvanceTransport(1f);

        Assert.That(
            _entityManager.GetComponentData<DroneState>(drone).value,
            Is.EqualTo(DroneStateEnum.AwaitingCharge));
        Assert.That(
            _entityManager.GetComponentData<DroneBattery>(drone).current,
            Is.EqualTo(84f).Within(0.001f));
        Assert.That(
            _entityManager.GetComponentData<StoredItem>(item).owner,
            Is.EqualTo(destination));
        Assert.That(
            _entityManager.GetBuffer<StoredItemElement>(destination).Length,
            Is.EqualTo(1));
        Assert.That(
            _entityManager.GetComponentData<DroneTaskStatus>(task).state,
            Is.EqualTo(DroneTaskStateEnum.Completed));
        Assert.That(CountReservations(), Is.EqualTo(0));
    }

    [Test]
    public void InsufficientBatteryReleasesReservationRecoversCargoAndEmergencyReturns()
    {
        Entity source = CreateStorage(new int2(4, 0), 10);
        Entity destination = CreateStorage(new int2(8, 0), 10);
        Entity item = CreateStoredItem(source, ItemTypeEnum.Iron_Ore);
        Entity task = CreateTask(1);
        CreateReservation(task, source, destination, ItemTypeEnum.Iron_Ore, 1);
        _reservationSystem.Update();
        Entity drone = CreateDrone(3, 100f);

        _dispatchSystem.Update();
        AdvanceTransport(1f);
        DroneBattery battery = _entityManager
            .GetComponentData<DroneBattery>(drone);
        battery.current = 1f;
        _entityManager.SetComponentData(drone, battery);
        AdvanceTransport(1f);

        Assert.That(
            _entityManager.GetComponentData<DroneState>(drone).value,
            Is.EqualTo(DroneStateEnum.MovingToRecoveryStorage));
        Assert.That(
            _entityManager.GetComponentData<StoredItem>(item).owner,
            Is.EqualTo(drone));

        for (int i = 0; i < 9; i++)
            AdvanceTransport(1f);

        Assert.That(
            _entityManager.GetComponentData<DroneState>(drone).value,
            Is.EqualTo(DroneStateEnum.AwaitingCharge));
        Assert.That(
            _entityManager.GetComponentData<DroneBattery>(drone).current,
            Is.EqualTo(1f).Within(0.001f));
        Assert.That(
            _entityManager.GetComponentData<StoredItem>(item).owner,
            Is.EqualTo(source));
        Assert.That(
            _entityManager.GetBuffer<StoredItemElement>(drone).Length,
            Is.EqualTo(0));
        Assert.That(
            _entityManager.GetComponentData<DroneTaskStatus>(task).state,
            Is.EqualTo(DroneTaskStateEnum.Pending));
        Assert.That(CountReservations(), Is.EqualTo(0));
    }

    [Test]
    public void DestroyedDestinationCancelsTaskRecoversCargoAndReturns()
    {
        Entity source = CreateStorage(new int2(4, 0), 10);
        Entity destination = CreateStorage(new int2(8, 0), 10);
        Entity item = CreateStoredItem(source, ItemTypeEnum.Iron_Ore);
        Entity task = CreateTask(1);
        CreateReservation(task, source, destination, ItemTypeEnum.Iron_Ore, 1);
        _reservationSystem.Update();
        Entity drone = CreateDrone(3, 100f);

        _dispatchSystem.Update();
        AdvanceTransport(1f);
        _entityManager.DestroyEntity(destination);
        _reservationSystem.Update();
        AdvanceTransport(1f);
        AdvanceTransport(1f);
        AdvanceTransport(1f);

        Assert.That(
            _entityManager.GetComponentData<StoredItem>(item).owner,
            Is.EqualTo(source));
        Assert.That(
            _entityManager.GetComponentData<DroneTaskStatus>(task).state,
            Is.EqualTo(DroneTaskStateEnum.Cancelled));
        Assert.That(
            _entityManager.GetComponentData<DroneState>(drone).value,
            Is.EqualTo(DroneStateEnum.AwaitingCharge));
    }

    [Test]
    public void ReservationAboveCarryingCapacityIsNotAssigned()
    {
        Entity source = CreateStorage(new int2(4, 0), 10);
        Entity destination = CreateStorage(new int2(8, 0), 10);
        CreateStoredItem(source, ItemTypeEnum.Iron_Ore);
        CreateStoredItem(source, ItemTypeEnum.Iron_Ore);
        Entity task = CreateTask(2);
        CreateReservation(task, source, destination, ItemTypeEnum.Iron_Ore, 2);
        _reservationSystem.Update();
        Entity drone = CreateDrone(1, 100f);

        _dispatchSystem.Update();

        Assert.That(
            _entityManager.GetComponentData<DroneState>(drone).value,
            Is.EqualTo(DroneStateEnum.Stored));
        Entity reservation = GetReservation();
        Assert.That(
            _entityManager.HasComponent<DroneReservationAssignment>(
                reservation),
            Is.False);
    }

    [Test]
    public void DispatchRollsBackClaimWhenStoredDroneIndexIsInconsistent()
    {
        Entity source = CreateStorage(new int2(4, 0), 10);
        Entity destination = CreateStorage(new int2(8, 0), 10);
        CreateStoredItem(source, ItemTypeEnum.Iron_Ore);
        Entity task = CreateTask(1);
        CreateReservation(task, source, destination, ItemTypeEnum.Iron_Ore, 1);
        _reservationSystem.Update();
        Entity drone = CreateDrone(3, 100f);
        _entityManager.AddComponentData(
            drone,
            new StoredDrone { stationEntity = _station });

        _dispatchSystem.Update();

        Assert.That(CountReservations(), Is.EqualTo(0));
        Assert.That(
            _entityManager.GetComponentData<DroneState>(drone).value,
            Is.EqualTo(DroneStateEnum.Stored));
        Assert.That(
            _entityManager.GetComponentData<DroneTaskStatus>(task).state,
            Is.EqualTo(DroneTaskStateEnum.Pending));
    }

    [Test]
    public void ReturnedDroneReroutesWhenAssignedStationHasNoCapacity()
    {
        Entity availableStation = CreateStation(new int2(8, 0));
        _entityManager.SetComponentData(
            _station,
            new Storage { capacity = 0 });
        _networkSystem.Update();
        Entity drone = CreateDrone(3, 50f);
        _entityManager.SetComponentData(
            drone,
            new DroneState { value = DroneStateEnum.AwaitingStorage });
        DroneStationStorageSystem storageSystem = _world
            .GetOrCreateSystemManaged<DroneStationStorageSystem>();

        storageSystem.Update();

        DroneAssignment assignment = _entityManager
            .GetComponentData<DroneAssignment>(drone);
        Assert.That(assignment.returnStation, Is.EqualTo(availableStation));
        Assert.That(
            _entityManager.GetComponentData<DroneState>(drone).value,
            Is.EqualTo(DroneStateEnum.Returning));
        Assert.That(
            _entityManager.HasComponent<StoredDrone>(drone),
            Is.False);
    }

    [Test]
    public void CargoDropsToWorldWhenNoStorageCanAcceptIt()
    {
        Entity source = CreateStorage(new int2(4, 0), 10);
        Entity destination = CreateStorage(new int2(8, 0), 10);
        Entity item = CreateStoredItem(source, ItemTypeEnum.Iron_Ore);
        Entity task = CreateTask(1);
        CreateReservation(task, source, destination, ItemTypeEnum.Iron_Ore, 1);
        _reservationSystem.Update();
        Entity drone = CreateDrone(3, 100f);

        _dispatchSystem.Update();
        AdvanceTransport(1f);
        _entityManager.SetComponentData(
            _station,
            new Storage { capacity = 0 });
        _entityManager.DestroyEntity(source);
        _entityManager.DestroyEntity(destination);
        _reservationSystem.Update();
        AdvanceTransport(1f);
        AdvanceTransport(1f);

        Assert.That(
            _entityManager.HasComponent<StoredItem>(item),
            Is.False);
        Assert.That(
            _entityManager.HasComponent<Disabled>(item),
            Is.False);
        Assert.That(
            _entityManager.GetBuffer<StoredItemElement>(drone).Length,
            Is.EqualTo(0));
        Assert.That(
            _entityManager.GetComponentData<DroneTaskStatus>(task).state,
            Is.EqualTo(DroneTaskStateEnum.Cancelled));
        Assert.That(
            _entityManager.GetComponentData<DroneState>(drone).value,
            Is.EqualTo(DroneStateEnum.AwaitingCharge));
    }

    private void AdvanceTransport(float deltaTime)
    {
        _elapsedTime += deltaTime;
        _world.SetTime(new TimeData(_elapsedTime, deltaTime));
        _movementSystem.Update();
        _cargoSystem.Update();
        _recoverySystem.Update();
    }

    private Entity CreateStation(int2 cell)
    {
        Entity station = CreateStorage(cell, 10);
        _entityManager.AddBuffer<StoredDroneElement>(station);
        _entityManager.AddComponentData(
            station,
            new DroneStation
            {
                activityRangeInChunks = new int2(1, 1),
                isMainStation = true
            });
        return station;
    }

    private Entity CreateStorage(int2 cell, int capacity)
    {
        Entity storage = _entityManager.CreateEntity(
            typeof(Storage),
            typeof(StoredItemElement),
            typeof(GridPosition),
            typeof(BuildingOccupant));
        _entityManager.SetComponentData(
            storage,
            new Storage { capacity = capacity });
        _entityManager.SetComponentData(
            storage,
            new GridPosition { gridPosition = cell });
        return storage;
    }

    private Entity CreateStoredItem(
        Entity owner,
        ItemTypeEnum itemType)
    {
        Entity item = _entityManager.CreateEntity(
            typeof(Item),
            typeof(StoredItem),
            typeof(Disabled),
            typeof(LocalTransform),
            typeof(LocalToWorld),
            typeof(GridPosition),
            typeof(ItemCellChanged));
        _entityManager.SetComponentData(item, new Item { type = itemType });
        _entityManager.SetComponentData(item, new StoredItem { owner = owner });
        _entityManager.GetBuffer<StoredItemElement>(owner).Add(
            new StoredItemElement
            {
                itemEntity = item,
                type = itemType
            });
        return item;
    }

    private Entity CreateTask(int quantity)
    {
        Entity task = _entityManager.CreateEntity(
            typeof(DroneTask),
            typeof(DroneTaskStatus),
            typeof(DroneTaskPriority),
            typeof(DroneTaskCreationOrder),
            typeof(DroneTaskQuantity));
        _entityManager.SetComponentData(
            task,
            new DroneTask { type = DroneTaskTypeEnum.InsertBuildingItem });
        _entityManager.SetComponentData(
            task,
            new DroneTaskStatus { state = DroneTaskStateEnum.Pending });
        _entityManager.SetComponentData(
            task,
            new DroneTaskPriority
            {
                priorityClass = DroneTaskPriorityClassEnum.Normal,
                normalPriority = 5
            });
        _entityManager.SetComponentData(
            task,
            new DroneTaskCreationOrder { value = 1 });
        _entityManager.SetComponentData(
            task,
            new DroneTaskQuantity { totalQuantity = quantity });
        return task;
    }

    private void CreateReservation(
        Entity task,
        Entity source,
        Entity destination,
        ItemTypeEnum itemType,
        int quantity)
    {
        Entity request = _entityManager.CreateEntity(
            typeof(DroneTaskReservationRequest));
        _entityManager.SetComponentData(
            request,
            new DroneTaskReservationRequest
            {
                taskEntity = task,
                sourceOwner = source,
                destinationOwner = destination,
                itemType = itemType,
                quantity = quantity
            });
    }

    private Entity CreateDrone(int capacity, float batteryAmount)
    {
        int networkId = _entityManager
            .GetComponentData<DroneStationNetwork>(_station)
            .networkId;
        Entity drone = _entityManager.CreateEntity(
            typeof(ActiveDrone),
            typeof(DroneBattery),
            typeof(DroneState),
            typeof(DroneAssignment),
            typeof(DroneCargo),
            typeof(StoredItemElement),
            typeof(LocalTransform),
            typeof(GridPosition));
        _entityManager.SetComponentData(
            drone,
            new ActiveDrone
            {
                carryingCapacity = capacity,
                movementSpeed = 4f,
                emergencyMovementSpeed = 0.5f
            });
        _entityManager.SetComponentData(
            drone,
            new DroneBattery
            {
                current = batteryAmount,
                maximum = batteryAmount,
                consumptionPerDistance = 1f
            });
        _entityManager.SetComponentData(
            drone,
            new DroneState { value = DroneStateEnum.Stored });
        _entityManager.SetComponentData(
            drone,
            new DroneAssignment
            {
                returnStation = _station,
                networkId = networkId
            });
        _entityManager.SetComponentData(
            drone,
            new DroneCargo { itemType = ItemTypeEnum.None });
        _entityManager.SetComponentData(
            drone,
            LocalTransform.FromPosition(new float3(0f, 0f, -0.2f)));
        return drone;
    }

    private void CreateStorageLimit()
    {
        Entity config = _entityManager.CreateEntity(
            typeof(ItemStorageLimitElement));
        _entityManager.GetBuffer<ItemStorageLimitElement>(config).Add(
            new ItemStorageLimitElement
            {
                itemType = ItemTypeEnum.Iron_Ore,
                maxAmount = 100
            });
        _entityManager.GetBuffer<ItemStorageLimitElement>(config).Add(
            new ItemStorageLimitElement
            {
                itemType = ItemTypeEnum.Drone,
                maxAmount = 1
            });
    }

    private static DroneConfig CreateDroneConfig()
    {
        return new DroneConfig
        {
            defaultTaskPriority = 5,
            stationStorageCapacity = 10,
            stationActivityRangeInChunks = new int2(1, 1),
            carryingCapacity = 3,
            movementSpeed = 4f,
            emergencyMovementSpeed = 0.5f,
            maximumBattery = 100f,
            batteryConsumptionPerDistance = 1f
        };
    }

    private Entity GetReservation()
    {
        using EntityQuery query = _entityManager.CreateEntityQuery(
            typeof(DroneTaskReservation));
        return query.GetSingletonEntity();
    }

    private int CountReservations()
    {
        using EntityQuery query = _entityManager.CreateEntityQuery(
            typeof(DroneTaskReservation));
        return query.CalculateEntityCount();
    }
}
