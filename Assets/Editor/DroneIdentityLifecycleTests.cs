using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

public class DroneIdentityLifecycleTests
{
    private World _world;
    private EntityManager _entityManager;
    private DroneIdentityConversionSystem _conversionSystem;
    private Entity _configEntity;

    [SetUp]
    public void SetUp()
    {
        _world = new World(nameof(DroneIdentityLifecycleTests));
        _entityManager = _world.EntityManager;
        _world.GetOrCreateSystemManaged<ChunkMapSystem>();
        _world.GetOrCreateSystemManaged<ItemTrackingSystem>();
        _world.GetOrCreateSystemManaged<ItemStorageSystem>();
        _conversionSystem = _world.GetOrCreateSystemManaged<
            DroneIdentityConversionSystem>();
        _configEntity = CreateConfiguration();
    }

    [TearDown]
    public void TearDown()
    {
        _world.Dispose();
    }

    [Test]
    public void StoredDroneItemConvertsInPlaceToSingleActiveIdentity()
    {
        Entity station = CreateStation(2);
        Entity itemEntity = CreateStoredDroneItem(station);

        _conversionSystem.Update();

        Assert.That(_entityManager.Exists(itemEntity), Is.True);
        Assert.That(_entityManager.HasComponent<ActiveDrone>(itemEntity), Is.True);
        Assert.That(_entityManager.HasComponent<StoredDrone>(itemEntity), Is.True);
        Assert.That(_entityManager.HasComponent<Item>(itemEntity), Is.False);
        Assert.That(_entityManager.HasComponent<StoredItem>(itemEntity), Is.False);
        Assert.That(_entityManager.HasComponent<Disabled>(itemEntity), Is.False);
        Assert.That(
            _entityManager.GetBuffer<StoredItemElement>(station).Length,
            Is.EqualTo(0));
        Assert.That(
            _entityManager.GetBuffer<StoredDroneElement>(station).Length,
            Is.EqualTo(1));
    }

    [Test]
    public void DedicatedDroneSlotsDoNotConsumeSharedStationCapacity()
    {
        Entity station = CreateStation(1);
        CreateStoredDroneItem(station);
        _conversionSystem.Update();
        DynamicBuffer<StoredItemElement> storedItems = _entityManager
            .GetBuffer<StoredItemElement>(station, true);
        DynamicBuffer<DroneReservedStorageCapacityElement> reserved = default;
        using NativeArray<ItemStorageLimitElement> limits =
            DynamicBufferCopyUtility.CreateNativeCopy(
                _entityManager.GetBuffer<ItemStorageLimitElement>(
                    _configEntity,
                    true),
                Allocator.Temp);

        bool canStoreIron = StorageCapacityUtility.CanStoreAdditionalItems(
            storedItems,
            reserved,
            false,
            1,
            limits,
            ItemTypeEnum.Iron,
            1,
            DroneStationStorageUtility.GetStoredDroneCount(
                _entityManager,
                station),
            DroneStationStorageUtility.GetDedicatedDroneSlotCapacity(
                _entityManager,
                station));

        Assert.That(canStoreIron, Is.True);
    }

    [Test]
    public void DronesUseSharedCapacityAfterDedicatedSlotsAreFull()
    {
        Entity station = CreateStation(1);

        for (int i = 0;
             i < DroneStationStorageUtility.DedicatedDroneSlotCapacity + 1;
             i++)
        {
            CreateStoredDroneItem(station);
        }

        _conversionSystem.Update();
        DynamicBuffer<StoredItemElement> storedItems = _entityManager
            .GetBuffer<StoredItemElement>(station, true);
        DynamicBuffer<DroneReservedStorageCapacityElement> reserved = default;
        using NativeArray<ItemStorageLimitElement> limits =
            DynamicBufferCopyUtility.CreateNativeCopy(
                _entityManager.GetBuffer<ItemStorageLimitElement>(
                    _configEntity,
                    true),
                Allocator.Temp);

        bool canStoreIron = StorageCapacityUtility.CanStoreAdditionalItems(
            storedItems,
            reserved,
            false,
            1,
            limits,
            ItemTypeEnum.Iron,
            1,
            DroneStationStorageUtility.GetStoredDroneCount(
                _entityManager,
                station),
            DroneStationStorageUtility.GetDedicatedDroneSlotCapacity(
                _entityManager,
                station));

        Assert.That(canStoreIron, Is.False);
    }

    [Test]
    public void DedicatedDroneSlotsRejectOrdinaryItemsWhenSharedStorageIsFull()
    {
        Entity station = CreateStation(1);
        DynamicBuffer<StoredItemElement> storedItems = _entityManager
            .GetBuffer<StoredItemElement>(station);

        for (int i = 0; i < 10; i++)
        {
            storedItems.Add(new StoredItemElement
            {
                itemEntity = _entityManager.CreateEntity(),
                type = ItemTypeEnum.Iron
            });
        }

        DynamicBuffer<DroneReservedStorageCapacityElement> reserved = default;
        using NativeArray<ItemStorageLimitElement> limits =
            DynamicBufferCopyUtility.CreateNativeCopy(
                _entityManager.GetBuffer<ItemStorageLimitElement>(
                    _configEntity,
                    true),
                Allocator.Temp);
        int dedicatedDroneSlotCapacity = DroneStationStorageUtility
            .GetDedicatedDroneSlotCapacity(_entityManager, station);
        bool canStoreIron = StorageCapacityUtility.CanStoreAdditionalItems(
            storedItems,
            reserved,
            false,
            1,
            limits,
            ItemTypeEnum.Iron,
            1,
            0,
            dedicatedDroneSlotCapacity);
        bool canStoreDrone = StorageCapacityUtility.CanStoreAdditionalItems(
            storedItems,
            reserved,
            false,
            1,
            limits,
            ItemTypeEnum.Drone,
            1,
            0,
            dedicatedDroneSlotCapacity);

        Assert.That(canStoreIron, Is.False);
        Assert.That(canStoreDrone, Is.True);
    }

    [Test]
    public void StationDestructionRelocatesStoredDroneWithoutWorldItem()
    {
        Entity station = CreateStation(2, int2.zero, 7);
        Entity targetStation = CreateStation(2, new int2(4, 0), 7);
        Entity droneEntity = CreateStoredDroneItem(station);
        _conversionSystem.Update();

        bool relocated = _conversionSystem
            .TryRelocateStoredDronesForStationDestruction(station);

        Assert.That(relocated, Is.True);
        Assert.That(_entityManager.HasComponent<Item>(droneEntity), Is.False);
        Assert.That(
            _entityManager.HasComponent<ActiveDrone>(droneEntity),
            Is.True);
        Assert.That(_entityManager.HasComponent<StoredDrone>(droneEntity), Is.False);
        Assert.That(
            _entityManager.GetBuffer<StoredDroneElement>(station).Length,
            Is.EqualTo(0));
        Assert.That(
            _entityManager.GetComponentData<DroneState>(droneEntity).value,
            Is.EqualTo(DroneStateEnum.Returning));
        Assert.That(
            _entityManager.GetComponentData<DroneAssignment>(droneEntity)
                .returnStation,
            Is.EqualTo(targetStation));
        Assert.That(
            Count<DroneWorldItemRecoveryCreateRequest>(),
            Is.EqualTo(0));
    }

    [Test]
    public void StationDestructionPrefersAvailableStationInCurrentNetwork()
    {
        Entity station = CreateStation(2, int2.zero, 7);
        Entity sameNetworkStation = CreateStation(2, new int2(8, 0), 7);
        CreateStation(2, new int2(1, 0), 8);
        Entity droneEntity = CreateStoredDroneItem(station);
        _conversionSystem.Update();

        bool relocated = _conversionSystem
            .TryRelocateStoredDronesForStationDestruction(station);

        Assert.That(
            relocated,
            Is.True);
        Assert.That(
            _entityManager.GetComponentData<DroneAssignment>(droneEntity)
                .returnStation,
            Is.EqualTo(sameNetworkStation));
    }

    [Test]
    public void StationDestructionPlansCapacityAcrossAllStoredDrones()
    {
        Entity station = CreateStation(2, int2.zero, 7);
        Entity nearStation = CreateStation(0, new int2(2, 0), 7);
        Entity farStation = CreateStation(0, new int2(6, 0), 7);

        for (int i = 0;
             i < DroneStationStorageUtility.DedicatedDroneSlotCapacity - 1;
             i++)
        {
            CreateStoredDroneItem(nearStation);
        }

        Entity firstDrone = CreateStoredDroneItem(station);
        Entity secondDrone = CreateStoredDroneItem(station);
        _conversionSystem.Update();

        bool relocated = _conversionSystem
            .TryRelocateStoredDronesForStationDestruction(station);

        Assert.That(relocated, Is.True);
        Assert.That(
            _entityManager.GetComponentData<DroneAssignment>(firstDrone)
                .returnStation,
            Is.EqualTo(nearStation));
        Assert.That(
            _entityManager.GetComponentData<DroneAssignment>(secondDrone)
                .returnStation,
            Is.EqualTo(farStation));
        Assert.That(_entityManager.HasComponent<Item>(firstDrone), Is.False);
        Assert.That(_entityManager.HasComponent<Item>(secondDrone), Is.False);
    }

    [Test]
    public void StationDestructionIsDeferredWhenNoStationCanAcceptEveryDrone()
    {
        Entity station = CreateStation(2);
        Entity droneEntity = CreateStoredDroneItem(station);
        _conversionSystem.Update();

        bool relocated = _conversionSystem
            .TryRelocateStoredDronesForStationDestruction(station);

        Assert.That(relocated, Is.False);
        Assert.That(_entityManager.HasComponent<StoredDrone>(droneEntity), Is.True);
        Assert.That(
            _entityManager.GetBuffer<StoredDroneElement>(station).Length,
            Is.EqualTo(1));
        Assert.That(
            _entityManager.GetComponentData<DroneState>(droneEntity).value,
            Is.EqualTo(DroneStateEnum.Stored));
    }

    private Entity CreateConfiguration()
    {
        Entity config = _entityManager.CreateEntity(
            typeof(DroneConfig),
            typeof(ItemStorageLimitElement));
        _entityManager.SetComponentData(config, new DroneConfig
        {
            defaultTaskPriority = 5,
            carryingCapacity = 3,
            movementSpeed = 4f,
            emergencyMovementSpeed = 0.5f,
            maximumBattery = 100f,
            batteryConsumptionPerDistance = 1f
        });
        DynamicBuffer<ItemStorageLimitElement> limits = _entityManager
            .GetBuffer<ItemStorageLimitElement>(config);
        limits.Add(new ItemStorageLimitElement
        {
            itemType = ItemTypeEnum.Iron,
            maxAmount = 10
        });
        limits.Add(new ItemStorageLimitElement
        {
            itemType = ItemTypeEnum.Drone,
            maxAmount = 1
        });
        return config;
    }

    private Entity CreateStation(int capacity)
    {
        return CreateStation(capacity, int2.zero, 7);
    }

    private Entity CreateStation(
        int capacity,
        int2 gridPosition,
        int networkId)
    {
        Entity station = _entityManager.CreateEntity(
            typeof(DroneStation),
            typeof(DroneStationNetwork),
            typeof(Storage),
            typeof(GridPosition),
            typeof(BuildingFootprint),
            typeof(Direction),
            typeof(StoredItemElement),
            typeof(StoredDroneElement),
            typeof(BuildingOccupant));
        _entityManager.SetComponentData(
            station,
            new DroneStation { activityRangeInChunks = new int2(1) });
        _entityManager.SetComponentData(
            station,
            new DroneStationNetwork { networkId = networkId });
        _entityManager.SetComponentData(
            station,
            new Storage { capacity = capacity });
        _entityManager.SetComponentData(
            station,
            new GridPosition { gridPosition = gridPosition });
        _entityManager.SetComponentData(
            station,
            new BuildingFootprint { size = new int2(1, 1) });
        _entityManager.SetComponentData(
            station,
            new Direction { dir = DirectionEnum.Up });
        return station;
    }

    private Entity CreateStoredDroneItem(Entity station)
    {
        int2 stationCell = _entityManager
            .GetComponentData<GridPosition>(station)
            .gridPosition;
        Entity itemEntity = _entityManager.CreateEntity(
            typeof(LocalTransform),
            typeof(LocalToWorld),
            typeof(GridPosition),
            typeof(Item),
            typeof(ItemCellChanged),
            typeof(StoredItem),
            typeof(Disabled));
        _entityManager.SetComponentData(
            itemEntity,
            LocalTransform.FromPosition(
                new float3(stationCell.x, stationCell.y, -0.2f)));
        _entityManager.SetComponentData(
            itemEntity,
            new GridPosition { gridPosition = stationCell });
        _entityManager.SetComponentData(
            itemEntity,
            new Item { type = ItemTypeEnum.Drone });
        _entityManager.SetComponentData(
            itemEntity,
            new StoredItem { owner = station });
        _entityManager.GetBuffer<StoredItemElement>(station).Add(
            new StoredItemElement
            {
                itemEntity = itemEntity,
                type = ItemTypeEnum.Drone
            });
        return itemEntity;
    }

    private int Count<T>() where T : unmanaged, IComponentData
    {
        using EntityQuery query = _entityManager.CreateEntityQuery(typeof(T));
        return query.CalculateEntityCount();
    }
}
