using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

public class DroneConstructionTests
{
    private World _world;
    private EntityManager _entityManager;
    private ChunkMapSystem _chunkMap;
    private ConstructionCancelSystem _cancelSystem;
    private ConstructionCompletionSystem _completionSystem;
    private DroneWorldItemRecoveryRequestSystem _recoveryRequestSystem;

    [SetUp]
    public void SetUp()
    {
        _world = new World(nameof(DroneConstructionTests));
        _entityManager = _world.EntityManager;
        _chunkMap = _world.GetOrCreateSystemManaged<ChunkMapSystem>();
        _world.GetOrCreateSystemManaged<ItemTrackingSystem>();
        _world.GetOrCreateSystemManaged<ItemStorageSystem>();
        _cancelSystem = _world.GetOrCreateSystemManaged<ConstructionCancelSystem>();
        _completionSystem = _world.GetOrCreateSystemManaged<ConstructionCompletionSystem>();
        _recoveryRequestSystem = _world.GetOrCreateSystemManaged<
            DroneWorldItemRecoveryRequestSystem>();
    }

    [TearDown]
    public void TearDown()
    {
        _world.Dispose();
    }

    [Test]
    public void CompletionWaitsForEveryMaterialAndPreservesSpawnData()
    {
        Entity site = CreateSite(
            new int2(2, 3),
            BuildingTypeEnum.Crafter,
            DirectionEnum.Right,
            ItemTypeEnum.Copper,
            new int2(2, 1),
            new int2(2, 3),
            new int2(2, 2));
        DynamicBuffer<ConstructionMaterialRequirementElement> requirements =
            _entityManager.GetBuffer<ConstructionMaterialRequirementElement>(site);
        requirements.Add(new ConstructionMaterialRequirementElement
        {
            itemType = ItemTypeEnum.Iron,
            quantity = 2
        });
        CreateStoredItem(site, ItemTypeEnum.Iron);

        _completionSystem.Update();

        Assert.That(_entityManager.Exists(site), Is.True);
        Assert.That(Count<BuildingSpawnRequest>(), Is.EqualTo(0));

        CreateStoredItem(site, ItemTypeEnum.Iron);
        _completionSystem.Update();

        Assert.That(_entityManager.Exists(site), Is.False);
        Assert.That(Count<BuildingSpawnRequest>(), Is.EqualTo(1));
        Entity spawnEntity = GetSingleton<BuildingSpawnRequest>();
        BuildingSpawnRequest spawn = _entityManager
            .GetComponentData<BuildingSpawnRequest>(spawnEntity);
        Assert.That(spawn.type, Is.EqualTo(BuildingTypeEnum.Crafter));
        Assert.That(spawn.gridPosition, Is.EqualTo(new int2(2, 3)));
        Assert.That(spawn.dir, Is.EqualTo(DirectionEnum.Right));
        Assert.That(spawn.selectedItemType, Is.EqualTo(ItemTypeEnum.Copper));
        Assert.That(_chunkMap.IsBuildingReserved(new int2(2, 3)), Is.True);
        Assert.That(_chunkMap.IsBuildingReserved(new int2(2, 2)), Is.True);
    }

    [Test]
    public void SameFrameCancelWinsAndCreatesOneRecoveryRequestPerWorldItem()
    {
        int2 siteCell = new(5, 6);
        Entity site = CreateSite(
            siteCell,
            BuildingTypeEnum.Storage,
            DirectionEnum.Up,
            ItemTypeEnum.None,
            new int2(1, 2),
            siteCell,
            siteCell + new int2(0, 1));
        _entityManager.GetBuffer<ConstructionMaterialRequirementElement>(site)
            .Add(new ConstructionMaterialRequirementElement
            {
                itemType = ItemTypeEnum.Iron,
                quantity = 1
            });
        Entity material = CreateStoredItem(site, ItemTypeEnum.Iron);
        Entity cancelRequest = _entityManager.CreateEntity(
            typeof(ConstructionCancelRequest));
        _entityManager.SetComponentData(cancelRequest,
            new ConstructionCancelRequest { gridPosition = siteCell });

        _cancelSystem.Update();
        _recoveryRequestSystem.Update();
        _completionSystem.Update();

        Assert.That(_entityManager.Exists(site), Is.False);
        Assert.That(Count<BuildingSpawnRequest>(), Is.EqualTo(0));
        Assert.That(_chunkMap.IsBuildingReserved(siteCell), Is.False);
        Assert.That(_chunkMap.IsBuildingReserved(siteCell + new int2(0, 1)), Is.False);
        Assert.That(_chunkMap.TryGetConstructionSite(siteCell + new int2(0, 1), out _), Is.False);
        Assert.That(_entityManager.HasComponent<StoredItem>(material), Is.False);
        Assert.That(_entityManager.HasComponent<Disabled>(material), Is.False);
        Assert.That(Count<DroneWorldItemRecoveryTaskData>(), Is.EqualTo(1));
        Assert.That(
            DroneWorldItemRecoveryTaskUtility.TryCreateRequest(
                _entityManager,
                material,
                5),
            Is.False);
        Assert.That(Count<DroneWorldItemRecoveryTaskData>(), Is.EqualTo(1));
    }

    [Test]
    public void ConstructionRequestCreatesMaterialTasksWithRequestedPriority()
    {
        CreateConstructionAndDroneConfig();
        int2 siteCell = new(7, 8);
        Entity request = _entityManager.CreateEntity(
            typeof(ConstructionSiteCreateRequest));
        _entityManager.SetComponentData(request,
            new ConstructionSiteCreateRequest
            {
                type = BuildingTypeEnum.Storage,
                gridPosition = siteCell,
                dir = DirectionEnum.Up,
                selectedItemType = ItemTypeEnum.None,
                normalPriority = 2
            });
        DynamicBuffer<ConstructionSiteReservedCellElement> reservedCells =
            _entityManager.AddBuffer<ConstructionSiteReservedCellElement>(
                request);
        reservedCells.Add(new ConstructionSiteReservedCellElement
        {
            cell = siteCell
        });
        reservedCells.Add(new ConstructionSiteReservedCellElement
        {
            cell = siteCell + new int2(0, 1)
        });
        reservedCells.Add(new ConstructionSiteReservedCellElement
        {
            cell = siteCell + new int2(0, 2)
        });
        Assert.That(_chunkMap.TryReserveBuilding(siteCell), Is.True);
        Assert.That(_chunkMap.TryReserveBuilding(siteCell + new int2(0, 1)), Is.True);
        Assert.That(_chunkMap.TryReserveBuilding(siteCell + new int2(0, 2)), Is.True);
        ConstructionSiteCreationSystem creationSystem = _world
            .GetOrCreateSystemManaged<ConstructionSiteCreationSystem>();

        creationSystem.Update();

        Assert.That(Count<DroneTaskCreateRequest>(), Is.EqualTo(1));
        Entity taskRequest = GetSingleton<DroneTaskCreateRequest>();
        DroneTaskCreateRequest task = _entityManager
            .GetComponentData<DroneTaskCreateRequest>(taskRequest);
        Assert.That(task.type, Is.EqualTo(DroneTaskTypeEnum.Construction));
        Assert.That(task.normalPriority, Is.EqualTo(2));
        Assert.That(
            _entityManager.GetComponentData<BuildingFootprint>(request).size,
            Is.EqualTo(new int2(1, 3)));

        CreateStoredItem(request, ItemTypeEnum.Iron);
        _completionSystem.Update();
        BeginSimulationEntityCommandBufferSystem beginSimulation = _world
            .GetOrCreateSystemManaged<BeginSimulationEntityCommandBufferSystem>();
        EndSimulationEntityCommandBufferSystem endSimulation = _world
            .GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>();
        _world.GetOrCreateSystemManaged<BuildingSpawnSystem>().Update();
        beginSimulation.Update();
        _chunkMap.Update();
        endSimulation.Update();

        Entity building = GetSingleton<BuildingOccupant>();
        Assert.That(_entityManager.GetComponentData<BuildingFootprint>(building).size,
            Is.EqualTo(new int2(1, 3)));
        Assert.That(_entityManager.GetComponentData<GridPosition>(building).gridPosition,
            Is.EqualTo(siteCell));
        for (int y = 0; y < 3; y++)
        {
            int2 cell = siteCell + new int2(0, y);
            Assert.That(_chunkMap.TryGetBuilding(cell, out Entity occupant), Is.True);
            Assert.That(occupant, Is.EqualTo(building));
            Assert.That(_chunkMap.IsBuildingReserved(cell), Is.False);
            Assert.That(_chunkMap.TryGetConstructionSite(cell, out _), Is.False);
        }
    }

    private void CreateConstructionAndDroneConfig()
    {
        Entity constructionConfig = _entityManager.CreateEntity(
            typeof(ConstructionConfig));
        DynamicBuffer<ConstructionMaterialConfigElement> materials =
            _entityManager.AddBuffer<ConstructionMaterialConfigElement>(
                constructionConfig);
        materials.Add(new ConstructionMaterialConfigElement
        {
            buildingType = BuildingTypeEnum.Storage,
            itemType = ItemTypeEnum.Iron,
            quantity = 1
        });

        Entity prefab = _entityManager.CreateEntity(typeof(Prefab), typeof(LocalTransform));
        _entityManager.SetComponentData(prefab, LocalTransform.Identity);
        Entity prefabDatabase = _entityManager.CreateEntity(
            typeof(BuildingPrefabElement));
        _entityManager.GetBuffer<BuildingPrefabElement>(prefabDatabase).Add(
            new BuildingPrefabElement
            {
                type = BuildingTypeEnum.Storage,
                prefab = prefab,
                size = new int2(0, 3)
            });

        _entityManager.CreateEntity(typeof(PowerConfig),
            typeof(PowerConsumerConfigElement), typeof(PowerGeneratorConfigElement));
        Entity runtimeConfig = _entityManager.CreateEntity(
            typeof(BuildingRuntimeConfig), typeof(BuildingRuntimeConfigElement));
        _entityManager.GetBuffer<BuildingRuntimeConfigElement>(runtimeConfig).Add(
            new BuildingRuntimeConfigElement
            {
                buildingType = BuildingTypeEnum.Storage,
                storageCapacity = 10
            });

        Entity droneConfig = _entityManager.CreateEntity(typeof(DroneConfig));
        _entityManager.SetComponentData(droneConfig, new DroneConfig
        {
            defaultTaskPriority = 5
        });

        Entity researchConfig = _entityManager.CreateEntity(
            typeof(ResearchConfig),
            typeof(BuildingUnlockElement));
        _entityManager.GetBuffer<BuildingUnlockElement>(researchConfig).Add(
            new BuildingUnlockElement
            {
                buildingType = BuildingTypeEnum.Storage
            });
    }

    private Entity CreateSite(
        int2 anchor,
        BuildingTypeEnum type,
        DirectionEnum direction,
        ItemTypeEnum selectedItemType,
        int2 size,
        params int2[] cells)
    {
        Entity site = _entityManager.CreateEntity(
            typeof(ConstructionSite),
            typeof(GridPosition),
            typeof(BuildingFootprint));
        _entityManager.SetComponentData(site, new ConstructionSite
        {
            type = type,
            direction = direction,
            selectedItemType = selectedItemType
        });
        _entityManager.SetComponentData(site,
            new GridPosition { gridPosition = anchor });
        _entityManager.SetComponentData(site,
            new BuildingFootprint
            {
                size = BuildingFootprintUtility.NormalizeSize(size)
            });
        _entityManager.AddBuffer<StoredItemElement>(site);
        _entityManager.AddBuffer<DroneReservedStorageCapacityElement>(site);
        _entityManager.AddBuffer<ConstructionMaterialRequirementElement>(site);
        DynamicBuffer<ConstructionSiteReservedCellElement> reservedCells =
            _entityManager.AddBuffer<ConstructionSiteReservedCellElement>(site);

        for (int i = 0; i < cells.Length; i++)
        {
            Assert.That(_chunkMap.TryReserveBuilding(cells[i]), Is.True);
            reservedCells.Add(new ConstructionSiteReservedCellElement
            {
                cell = cells[i]
            });
        }

        Assert.That(
            _chunkMap.TryRegisterConstructionSite(site, reservedCells),
            Is.True);
        return site;
    }

    private Entity CreateStoredItem(Entity owner, ItemTypeEnum itemType)
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
        _entityManager.SetComponentData(item, LocalTransform.Identity);
        _entityManager.SetComponentData(item, new GridPosition());
        _entityManager.GetBuffer<StoredItemElement>(owner).Add(
            new StoredItemElement
            {
                itemEntity = item,
                type = itemType
            });
        return item;
    }

    private int Count<T>() where T : unmanaged, IComponentData
    {
        using EntityQuery query = _entityManager.CreateEntityQuery(typeof(T));
        return query.CalculateEntityCount();
    }

    private Entity GetSingleton<T>() where T : unmanaged, IComponentData
    {
        using EntityQuery query = _entityManager.CreateEntityQuery(typeof(T));
        return query.GetSingletonEntity();
    }
}
