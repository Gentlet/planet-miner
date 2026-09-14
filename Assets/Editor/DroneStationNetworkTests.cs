using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

public class DroneStationNetworkTests : EcsWorldTestFixture
{
    private ChunkMapSystem _chunkMap;
    private DroneStationNetworkSystem _networkSystem;

    [SetUp]
    public void SetUp()
    {
        _chunkMap = _world.GetOrCreateSystemManaged<ChunkMapSystem>();
        _networkSystem = _world
            .GetOrCreateSystemManaged<DroneStationNetworkSystem>();
    }

    [Test]
    public void BridgeStationMergesAndRemovalSplitsNetworks()
    {
        Entity leftStation = CreateStation(0, 1);
        Entity rightStation = CreateStation(4, 1);
        _networkSystem.Update();

        int leftNetwork = GetNetworkId(leftStation);
        int rightNetwork = GetNetworkId(rightStation);
        Assert.That(leftNetwork, Is.Not.EqualTo(rightNetwork));

        Entity bridgeStation = CreateStation(2, 1);
        _networkSystem.Update();

        int mergedNetwork = GetNetworkId(leftStation);
        Assert.That(GetNetworkId(bridgeStation), Is.EqualTo(mergedNetwork));
        Assert.That(GetNetworkId(rightStation), Is.EqualTo(mergedNetwork));
        Assert.That(
            _networkSystem.TryGetNetworkIdAtCell(
                new int2(2 * GameConstants.chunkSize, 0),
                out int networkAtBridge),
            Is.True);
        Assert.That(networkAtBridge, Is.EqualTo(mergedNetwork));
        Assert.That(
            _networkSystem.TryFindNearestStation(
                mergedNetwork,
                new int2(2 * GameConstants.chunkSize, 0),
                out Entity nearestStation),
            Is.True);
        Assert.That(nearestStation, Is.EqualTo(bridgeStation));

        _entityManager.DestroyEntity(bridgeStation);
        _networkSystem.Update();

        Assert.That(
            GetNetworkId(leftStation),
            Is.Not.EqualTo(GetNetworkId(rightStation)));
        Assert.That(
            _networkSystem.IsCellCovered(new int2(2 * GameConstants.chunkSize, 0)),
            Is.False);
    }

    [Test]
    public void ActivityRangeIsSymmetricAroundSingleCellStation()
    {
        Entity station = CreateStation(0, 1);
        _networkSystem.Update();
        List<Entity> coveringStations = new();

        _chunkMap.GetDroneStationsCoveringCell(
            new int2(-GameConstants.chunkSize, -GameConstants.chunkSize),
            coveringStations);
        Assert.That(coveringStations.Contains(station), Is.True);

        _chunkMap.GetDroneStationsCoveringCell(
            new int2(GameConstants.chunkSize, GameConstants.chunkSize),
            coveringStations);
        Assert.That(coveringStations.Contains(station), Is.True);

        _chunkMap.GetDroneStationsCoveringCell(
            new int2(GameConstants.chunkSize + 1, 0),
            coveringStations);
        Assert.That(coveringStations.Contains(station), Is.False);
    }

    [Test]
    public void MainFacilityRangeExpandsSymmetricallyFromThreeByThreeFootprint()
    {
        Entity station = CreateStation(
            int2.zero,
            1,
            new int2(3),
            DirectionEnum.Up);
        _networkSystem.Update();
        List<Entity> coveringStations = new();

        int minimum = -GameConstants.chunkSize;
        int maximum = GameConstants.chunkSize + 2;
        _chunkMap.GetDroneStationsCoveringCell(
            new int2(minimum, 1),
            coveringStations);
        Assert.That(coveringStations.Contains(station), Is.True);

        _chunkMap.GetDroneStationsCoveringCell(
            new int2(maximum, 1),
            coveringStations);
        Assert.That(coveringStations.Contains(station), Is.True);

        _chunkMap.GetDroneStationsCoveringCell(
            new int2(minimum - 1, 1),
            coveringStations);
        Assert.That(coveringStations.Contains(station), Is.False);

        _chunkMap.GetDroneStationsCoveringCell(
            new int2(maximum + 1, 1),
            coveringStations);
        Assert.That(coveringStations.Contains(station), Is.False);
    }

    [Test]
    public void RotatedFootprintRangeUsesRotatedOccupiedBounds()
    {
        GridBounds bounds = DroneStationRangeUtility.GetActivityBounds(
            new int2(10, -4),
            new int2(2, 3),
            DirectionEnum.Right,
            new int2(1, 2));

        Assert.That(bounds.Min, Is.EqualTo(new int2(-6, -37)));
        Assert.That(bounds.Max, Is.EqualTo(new int2(28, 28)));
    }

    [Test]
    public void MainFacilityBootstrapComposesStationOnExistingEntity()
    {
        CreateBootstrapConfiguration();
        MainFacilityBootstrapSystem bootstrapSystem = _world
            .GetOrCreateSystemManaged<MainFacilityBootstrapSystem>();

        bootstrapSystem.Update();

        using EntityQuery mainFacilityQuery =
            _entityManager.CreateEntityQuery(typeof(MainFacility));
        using EntityQuery stationQuery =
            _entityManager.CreateEntityQuery(typeof(DroneStation));
        Assert.That(mainFacilityQuery.CalculateEntityCount(), Is.EqualTo(1));
        Assert.That(stationQuery.CalculateEntityCount(), Is.EqualTo(1));

        Entity mainFacility = mainFacilityQuery.GetSingletonEntity();
        Assert.That(
            _entityManager.GetComponentData<BuildingType>(mainFacility).type,
            Is.EqualTo(BuildingTypeEnum.MainFacility));
        Assert.That(
            _entityManager.HasComponent<Storage>(mainFacility),
            Is.True);
        Assert.That(
            _entityManager.HasBuffer<StoredItemElement>(mainFacility),
            Is.True);
        Assert.That(
            _entityManager.GetComponentData<Storage>(mainFacility).capacity,
            Is.EqualTo(13));
        Assert.That(
            _entityManager.GetComponentData<DroneStation>(mainFacility)
                .footprintSize,
            Is.EqualTo(new int2(3)));
        Assert.That(
            _entityManager.HasComponent<BuildingOutputCursor>(mainFacility),
            Is.False);

        using EntityQuery startingItemQuery = _entityManager
            .CreateEntityQuery(typeof(StartingItemSpawnRequest));
        using NativeArray<StartingItemSpawnRequest> startingItems =
            startingItemQuery.ToComponentDataArray<StartingItemSpawnRequest>(
                Allocator.Temp);
        int ironCount = 0;
        int copperCount = 0;
        int ironStickCount = 0;
        int copperStickCount = 0;
        int droneCount = 0;

        for (int i = 0; i < startingItems.Length; i++)
        {
            if (startingItems[i].itemType == ItemTypeEnum.Iron)
                ironCount++;

            if (startingItems[i].itemType == ItemTypeEnum.Copper)
                copperCount++;

            if (startingItems[i].itemType == ItemTypeEnum.Iron_Stick)
                ironStickCount++;

            if (startingItems[i].itemType == ItemTypeEnum.Copper_Stick)
                copperStickCount++;

            if (startingItems[i].itemType == ItemTypeEnum.Drone)
                droneCount++;
        }

        Assert.That(ironCount, Is.EqualTo(30));
        Assert.That(copperCount, Is.EqualTo(30));
        Assert.That(ironStickCount, Is.EqualTo(30));
        Assert.That(copperStickCount, Is.EqualTo(30));
        Assert.That(droneCount, Is.EqualTo(10));
    }

    [Test]
    public void StartingItemSpawnCreatesStoredItemOwnedByMainFacility()
    {
        Entity itemPrefab = _entityManager.CreateEntity(
            typeof(Prefab),
            typeof(LocalTransform));
        Entity itemPrefabDatabase = _entityManager.CreateEntity(
            typeof(ItemPrefabElement));
        _entityManager.GetBuffer<ItemPrefabElement>(itemPrefabDatabase).Add(
            new ItemPrefabElement
            {
                type = ItemTypeEnum.Iron,
                prefab = itemPrefab
            });
        Entity mainFacility = _entityManager.CreateEntity(
            typeof(MainFacility),
            typeof(Storage),
            typeof(GridPosition),
            typeof(StoredItemElement));
        Entity request = _entityManager.CreateEntity(
            typeof(StartingItemSpawnRequest));
        _entityManager.SetComponentData(
            request,
            new StartingItemSpawnRequest
            {
                owner = mainFacility,
                itemType = ItemTypeEnum.Iron
            });
        ItemSpawnSystem itemSpawnSystem = _world
            .GetOrCreateSystemManaged<ItemSpawnSystem>();

        itemSpawnSystem.Update();

        DynamicBuffer<StoredItemElement> storedItems = _entityManager
            .GetBuffer<StoredItemElement>(mainFacility);
        Assert.That(storedItems.Length, Is.EqualTo(1));
        Entity itemEntity = storedItems[0].itemEntity;
        Assert.That(
            _entityManager.GetComponentData<StoredItem>(itemEntity).owner,
            Is.EqualTo(mainFacility));
        Assert.That(
            _entityManager.GetComponentData<Item>(itemEntity).type,
            Is.EqualTo(ItemTypeEnum.Iron));
        Assert.That(
            _entityManager.HasComponent<Disabled>(itemEntity),
            Is.True);
        Assert.That(_entityManager.Exists(request), Is.False);
    }

    [Test]
    public void DroneStationAcceptsInputAndDoesNotAutoOutput()
    {
        CreateStorageConfiguration();
        _world.GetOrCreateSystemManaged<ItemTrackingSystem>();
        _world.GetOrCreateSystemManaged<ItemStorageSystem>();
        StorageSystem storageSystem =
            _world.GetOrCreateSystemManaged<StorageSystem>();
        Entity station = CreateInputOnlyStation();
        Entity item = CreateWorldItem(int2.zero);

        storageSystem.Update();
        storageSystem.Update();

        Assert.That(
            _entityManager.GetBuffer<StoredItemElement>(station).Length,
            Is.EqualTo(1));
        Assert.That(
            _entityManager.GetComponentData<StoredItem>(item).owner,
            Is.EqualTo(station));
        Assert.That(
            _entityManager.HasComponent<Disabled>(item),
            Is.True);
        Assert.That(
            _entityManager.HasComponent<BuildingOutputCursor>(station),
            Is.False);
    }

    [Test]
    public void GeneralStationSpawnAddsInputOnlyStorageBehavior()
    {
        Entity stationPrefab = _entityManager.CreateEntity(
            typeof(Prefab),
            typeof(LocalTransform));
        CreateBuildingSpawnConfiguration(stationPrefab);
        BeginSimulationEntityCommandBufferSystem beginSimulationEcb = _world
            .GetOrCreateSystemManaged<BeginSimulationEntityCommandBufferSystem>();
        BuildingSpawnSystem spawnSystem = _world
            .GetOrCreateSystemManaged<BuildingSpawnSystem>();
        Entity request = _entityManager.CreateEntity(
            typeof(BuildingSpawnRequest));
        _entityManager.SetComponentData(
            request,
            new BuildingSpawnRequest
            {
                type = BuildingTypeEnum.DroneStation,
                gridPosition = new int2(32, 0),
                dir = DirectionEnum.Up
            });

        spawnSystem.Update();
        beginSimulationEcb.Update();

        using EntityQuery stationQuery = _entityManager.CreateEntityQuery(
            typeof(DroneStation),
            typeof(BuildingType));
        Assert.That(stationQuery.CalculateEntityCount(), Is.EqualTo(1));
        Entity station = stationQuery.GetSingletonEntity();
        Assert.That(
            _entityManager.GetComponentData<BuildingType>(station).type,
            Is.EqualTo(BuildingTypeEnum.DroneStation));
        Assert.That(
            _entityManager.GetComponentData<Storage>(station).capacity,
            Is.EqualTo(10));
        Assert.That(
            _entityManager.GetComponentData<DroneStation>(station)
                .footprintSize,
            Is.EqualTo(new int2(2)));
        Assert.That(
            _entityManager.HasBuffer<StoredItemElement>(station),
            Is.True);
        Assert.That(
            _entityManager.HasComponent<BuildingOutputCursor>(station),
            Is.False);
    }

    private Entity CreateStation(int chunkX, int rangeInChunks)
    {
        return CreateStation(
            new int2(chunkX * GameConstants.chunkSize, 0),
            rangeInChunks,
            new int2(1),
            DirectionEnum.Up);
    }

    private Entity CreateStation(
        int2 gridPosition,
        int rangeInChunks,
        int2 footprintSize,
        DirectionEnum direction)
    {
        Entity station = _entityManager.CreateEntity(
            typeof(DroneStation),
            typeof(GridPosition),
            typeof(Direction),
            typeof(BuildingOccupant));
        _entityManager.SetComponentData(
            station,
            new DroneStation
            {
                activityRangeInChunks = new int2(rangeInChunks),
                footprintSize = footprintSize,
                isMainStation = false
            });
        _entityManager.SetComponentData(
            station,
            new GridPosition { gridPosition = gridPosition });
        _entityManager.SetComponentData(
            station,
            new Direction { dir = direction });
        return station;
    }

    private int GetNetworkId(Entity station)
    {
        return _entityManager
            .GetComponentData<DroneStationNetwork>(station)
            .networkId;
    }

    private void CreateBootstrapConfiguration()
    {
        Entity droneConfigEntity = _entityManager.CreateEntity(
            typeof(DroneConfig));
        _entityManager.SetComponentData(
            droneConfigEntity,
            new DroneConfig
            {
                defaultTaskPriority = 5,
                stationStorageCapacity = 13,
                stationActivityRangeInChunks = new int2(1)
            });

        Entity startingItemConfigEntity = _entityManager.CreateEntity(
            typeof(StartingItemConfigElement));
        DynamicBuffer<StartingItemConfigElement> startingItems =
            _entityManager.GetBuffer<StartingItemConfigElement>(
                startingItemConfigEntity);
        startingItems.Add(new StartingItemConfigElement
        {
            itemType = ItemTypeEnum.Iron,
            quantity = 30
        });
        startingItems.Add(new StartingItemConfigElement
        {
            itemType = ItemTypeEnum.Copper,
            quantity = 30
        });
        startingItems.Add(new StartingItemConfigElement
        {
            itemType = ItemTypeEnum.Iron_Stick,
            quantity = 30
        });
        startingItems.Add(new StartingItemConfigElement
        {
            itemType = ItemTypeEnum.Copper_Stick,
            quantity = 30
        });
        startingItems.Add(new StartingItemConfigElement
        {
            itemType = ItemTypeEnum.Drone,
            quantity = 10
        });

        Entity powerConfigEntity = _entityManager.CreateEntity(
            typeof(PowerConfig),
            typeof(PowerGeneratorConfigElement));
        _entityManager.GetBuffer<PowerGeneratorConfigElement>(
                powerConfigEntity)
            .Add(new PowerGeneratorConfigElement
            {
                generatorType = PowerGeneratorTypeEnum.MainFacility,
                maximumGeneration = 100f
            });

        Entity prefab = _entityManager.CreateEntity(
            typeof(Prefab),
            typeof(LocalTransform));
        Entity prefabDatabase = _entityManager.CreateEntity(
            typeof(BuildingPrefabElement));
        _entityManager.GetBuffer<BuildingPrefabElement>(prefabDatabase).Add(
            new BuildingPrefabElement
            {
                type = BuildingTypeEnum.MainFacility,
                prefab = prefab,
                size = new int2(3)
            });
    }

    private void CreateStorageConfiguration()
    {
        Entity storageLimitEntity = _entityManager.CreateEntity(
            typeof(ItemStorageLimitElement));
        _entityManager.GetBuffer<ItemStorageLimitElement>(storageLimitEntity)
            .Add(new ItemStorageLimitElement
            {
                itemType = ItemTypeEnum.Iron_Ore,
                maxAmount = 10
            });

        Entity prefabDatabase = _entityManager.CreateEntity(
            typeof(BuildingPrefabElement));
        _entityManager.GetBuffer<BuildingPrefabElement>(prefabDatabase).Add(
            new BuildingPrefabElement
            {
                type = BuildingTypeEnum.DroneStation,
                prefab = Entity.Null,
                size = new int2(1)
            });
    }

    private void CreateBuildingSpawnConfiguration(Entity stationPrefab)
    {
        Entity droneConfigEntity = _entityManager.CreateEntity(
            typeof(DroneConfig));
        _entityManager.SetComponentData(
            droneConfigEntity,
            new DroneConfig
            {
                defaultTaskPriority = 5,
                stationStorageCapacity = 10,
                stationActivityRangeInChunks = new int2(1)
            });

        Entity powerConfigEntity = _entityManager.CreateEntity(
            typeof(PowerConfig),
            typeof(PowerConsumerConfigElement),
            typeof(PowerGeneratorConfigElement));
        Entity prefabDatabase = _entityManager.CreateEntity(
            typeof(BuildingPrefabElement));
        _entityManager.GetBuffer<BuildingPrefabElement>(prefabDatabase).Add(
            new BuildingPrefabElement
            {
                type = BuildingTypeEnum.DroneStation,
                prefab = stationPrefab,
                size = new int2(2)
            });
    }

    private Entity CreateInputOnlyStation()
    {
        Entity station = _entityManager.CreateEntity(
            typeof(Storage),
            typeof(BuildingType),
            typeof(GridPosition),
            typeof(Direction),
            typeof(StoredItemElement));
        _entityManager.SetComponentData(
            station,
            new Storage { capacity = 10 });
        _entityManager.SetComponentData(
            station,
            new BuildingType { type = BuildingTypeEnum.DroneStation });
        _entityManager.SetComponentData(
            station,
            new GridPosition { gridPosition = int2.zero });
        _entityManager.SetComponentData(
            station,
            new Direction { dir = DirectionEnum.Up });
        return station;
    }

    private Entity CreateWorldItem(int2 cell)
    {
        Entity item = _entityManager.CreateEntity(
            typeof(Item),
            typeof(LocalTransform),
            typeof(GridPosition),
            typeof(ItemCellChanged));
        _entityManager.SetComponentData(
            item,
            new Item { type = ItemTypeEnum.Iron_Ore });
        _entityManager.SetComponentData(
            item,
            LocalTransform.FromPosition(new float3(cell.x, cell.y, 0f)));
        _entityManager.SetComponentData(
            item,
            new GridPosition { gridPosition = cell });
        return item;
    }
}
