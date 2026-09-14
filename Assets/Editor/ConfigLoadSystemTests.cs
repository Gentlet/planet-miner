using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.TestTools;

public class ConfigLoadSystemTests : EcsWorldTestFixture
{
    [Test]
    public void MissingConfigReportsNameAndResourcesPath()
    {
        const string configName = "Missing test";
        const string resourcePath = "Config/DoesNotExist";
        LogAssert.Expect(
            LogType.Error,
            $"{configName} config file not found. Path : Resources/{resourcePath}");

        bool loaded = ConfigResourceLoader.TryLoadJson(
            configName,
            resourcePath,
            out string json);

        Assert.That(loaded, Is.False);
        Assert.That(json, Is.Null);
    }

    [Test]
    public void ConfigLoadersPublishAllRuntimeConfiguration()
    {
        _world.GetOrCreateSystemManaged<BuildingRuntimeConfigLoadSystem>();
        _world.GetOrCreateSystemManaged<ConstructionConfigLoadSystem>();
        _world.GetOrCreateSystemManaged<CrafterConfigLoadSystem>();
        _world.GetOrCreateSystemManaged<DroneConfigLoadSystem>();
        _world.GetOrCreateSystemManaged<StartingItemConfigLoadSystem>();
        _world.GetOrCreateSystemManaged<PowerConfigLoadSystem>();
        ResearchConfigLoadSystem researchConfigLoadSystem = _world
            .GetOrCreateSystemManaged<ResearchConfigLoadSystem>();
        researchConfigLoadSystem.Update();

        AssertSingletonExists<ConstructionConfig>();
        AssertSingletonExists<CrafterConfig>();
        AssertSingletonExists<DroneConfig>();
        AssertSingletonExists<PowerConfig>();
        AssertSingletonExists<ResearchConfig>();
        AssertBufferHasElements<BuildingRuntimeConfigElement>();
        AssertBufferHasElements<ConstructionMaterialConfigElement>();
        AssertBufferHasElements<CrafterRecipeElement>();
        AssertBufferHasElements<CrafterRecipeIngredientElement>();
        AssertBufferHasElements<ItemStorageLimitElement>();
        AssertBufferHasElements<StartingItemConfigElement>();
        AssertBufferHasElements<PowerPoleConfigElement>();
        AssertBufferHasElements<PowerGeneratorConfigElement>();
        AssertBufferHasElements<PowerConsumerConfigElement>();
        AssertBufferHasElements<ResearchDefinitionElement>();
        AssertBufferHasElements<ResearchProgressElement>();
        AssertBufferHasElements<BuildingUnlockElement>();
        AssertBufferHasElements<RecipeUnlockElement>();
        AssertBuildingRuntimeConfig(
            BuildingTypeEnum.Belt,
            expectedSpeed: 10f);
        AssertBuildingRuntimeConfig(
            BuildingTypeEnum.Miner,
            expectedSpeed: 0.1f);
        AssertBuildingRuntimeConfig(
            BuildingTypeEnum.Crafter,
            expectedSpeed: 1f);
        AssertBuildingRuntimeConfig(
            BuildingTypeEnum.Storage,
            expectedStorageCapacity: 10);
    }

    [Test]
    public void ResearchSelectionPreservesGlobalProgressAndResetsLocalCycle()
    {
        _world.GetOrCreateSystemManaged<CrafterConfigLoadSystem>();
        ResearchConfigLoadSystem loadSystem = _world
            .GetOrCreateSystemManaged<ResearchConfigLoadSystem>();
        loadSystem.Update();
        EndSimulationEntityCommandBufferSystem endSimulationEcb = _world
            .GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>();
        ResearchSelectionSystem selectionSystem = _world
            .GetOrCreateSystemManaged<ResearchSelectionSystem>();

        using EntityQuery configQuery = _entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<ResearchConfig>());
        Entity configEntity = configQuery.GetSingletonEntity();
        DynamicBuffer<ResearchProgressElement> progress = _entityManager
            .GetBuffer<ResearchProgressElement>(configEntity);
        int logisticsProgressIndex = progress.FindProgressIndex(
            new Unity.Collections.FixedString64Bytes("logistics_distribution"));
        ResearchProgressElement logisticsProgress = progress[logisticsProgressIndex];
        logisticsProgress.progress = 30f;
        progress[logisticsProgressIndex] = logisticsProgress;

        Entity buildingEntity = _entityManager.CreateEntity(
            typeof(ResearchBuilding));
        _entityManager.SetComponentData(buildingEntity, new ResearchBuilding
        {
            cycleActive = true,
            cycleResearchId = new Unity.Collections.FixedString64Bytes(
                "logistics_distribution"),
            progress = 2f,
            state = ResearchBuildingStateEnum.Researching
        });
        CreateResearchSelectionRequest("material_processing");

        selectionSystem.Update();
        endSimulationEcb.Update();

        ResearchState state = _entityManager
            .GetComponentData<ResearchState>(configEntity);
        ResearchBuilding building = _entityManager
            .GetComponentData<ResearchBuilding>(buildingEntity);
        progress = _entityManager.GetBuffer<ResearchProgressElement>(
            configEntity);
        Assert.That(
            state.activeResearchId.ToString(),
            Is.EqualTo("material_processing"));
        Assert.That(progress[logisticsProgressIndex].progress, Is.EqualTo(30f));
        Assert.That(building.cycleActive, Is.False);
        Assert.That(building.progress, Is.Zero);
        Assert.That(
            building.resetReason,
            Is.EqualTo(ResearchCycleResetReasonEnum.ResearchChanged));

        CreateResearchSelectionRequest("drone_logistics");
        selectionSystem.Update();
        endSimulationEcb.Update();

        state = _entityManager.GetComponentData<ResearchState>(configEntity);
        Assert.That(
            state.activeResearchId.ToString(),
            Is.EqualTo("material_processing"),
            "Locked research must not replace the active research.");
    }

    [Test]
    public void BuildingSpawnUsesRuntimeConfigForAdjustableValues()
    {
        _world.GetOrCreateSystemManaged<ChunkMapSystem>();
        BeginSimulationEntityCommandBufferSystem beginSimulationEcb = _world
            .GetOrCreateSystemManaged<BeginSimulationEntityCommandBufferSystem>();
        CreatePowerConfig();
        CreateBuildingRuntimeConfig(
            beltSpeed: 12f,
            minerSpeed: 0.25f,
            crafterSpeed: 2f,
            storageCapacity: 17);
        CreateBuildingPrefabDefinitions();
        CreateSpawnRequest(BuildingTypeEnum.Belt, new int2(0, 0));
        CreateSpawnRequest(BuildingTypeEnum.Miner, new int2(2, 0));
        CreateSpawnRequest(
            BuildingTypeEnum.Crafter,
            new int2(4, 0),
            ItemTypeEnum.Iron);
        CreateSpawnRequest(BuildingTypeEnum.Storage, new int2(6, 0));
        BuildingSpawnSystem spawnSystem = _world
            .GetOrCreateSystemManaged<BuildingSpawnSystem>();

        spawnSystem.Update();
        beginSimulationEcb.Update();

        Belt belt = GetSingletonComponent<Belt>();
        Miner miner = GetSingletonComponent<Miner>();
        Crafter crafter = GetSingletonComponent<Crafter>();
        Storage storage = GetSingletonComponent<Storage>();
        Assert.That(belt.speed, Is.EqualTo(12f));
        Assert.That(miner.speed, Is.EqualTo(0.25f));
        Assert.That(miner.timer, Is.Zero);
        Assert.That(crafter.speed, Is.EqualTo(2f));
        Assert.That(crafter.progress, Is.Zero);
        Assert.That(crafter.selectedItemType, Is.EqualTo(ItemTypeEnum.Iron));
        Assert.That(storage.capacity, Is.EqualTo(17));
    }

    private void AssertSingletonExists<T>()
        where T : unmanaged, IComponentData
    {
        using EntityQuery query = _entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<T>());
        Assert.That(query.CalculateEntityCount(), Is.EqualTo(1));
    }

    private void AssertBufferHasElements<T>()
        where T : unmanaged, IBufferElementData
    {
        using EntityQuery query = _entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<T>());
        Assert.That(query.CalculateEntityCount(), Is.EqualTo(1));

        Entity configEntity = query.GetSingletonEntity();
        DynamicBuffer<T> buffer = _entityManager.GetBuffer<T>(
            configEntity,
            true);
        Assert.That(buffer.Length, Is.GreaterThan(0));
    }

    private void AssertBuildingRuntimeConfig(
        BuildingTypeEnum buildingType,
        float expectedSpeed = 0f,
        int expectedStorageCapacity = 0)
    {
        using EntityQuery query = _entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<BuildingRuntimeConfigElement>());
        DynamicBuffer<BuildingRuntimeConfigElement> configs =
            _entityManager.GetBuffer<BuildingRuntimeConfigElement>(
                query.GetSingletonEntity(),
                true);

        for (int i = 0; i < configs.Length; i++)
        {
            if (configs[i].buildingType != buildingType)
                continue;

            Assert.That(configs[i].speed, Is.EqualTo(expectedSpeed));
            Assert.That(
                configs[i].storageCapacity,
                Is.EqualTo(expectedStorageCapacity));
            return;
        }

        Assert.Fail($"Missing building runtime config for {buildingType}");
    }

    private void CreatePowerConfig()
    {
        _entityManager.CreateEntity(
            typeof(PowerConfig),
            typeof(PowerConsumerConfigElement),
            typeof(PowerGeneratorConfigElement));
    }

    private void CreateBuildingRuntimeConfig(
        float beltSpeed,
        float minerSpeed,
        float crafterSpeed,
        int storageCapacity)
    {
        Entity configEntity = _entityManager.CreateEntity(
            typeof(BuildingRuntimeConfig),
            typeof(BuildingRuntimeConfigElement));
        DynamicBuffer<BuildingRuntimeConfigElement> configs =
            _entityManager.GetBuffer<BuildingRuntimeConfigElement>(
                configEntity);
        configs.Add(new BuildingRuntimeConfigElement
        {
            buildingType = BuildingTypeEnum.Belt,
            speed = beltSpeed
        });
        configs.Add(new BuildingRuntimeConfigElement
        {
            buildingType = BuildingTypeEnum.Miner,
            speed = minerSpeed
        });
        configs.Add(new BuildingRuntimeConfigElement
        {
            buildingType = BuildingTypeEnum.Crafter,
            speed = crafterSpeed
        });
        configs.Add(new BuildingRuntimeConfigElement
        {
            buildingType = BuildingTypeEnum.Storage,
            storageCapacity = storageCapacity
        });
    }

    private void CreateBuildingPrefabDefinitions()
    {
        Entity prefab = _entityManager.CreateEntity(
            typeof(Prefab),
            typeof(LocalTransform));
        Entity databaseEntity = _entityManager.CreateEntity(
            typeof(BuildingPrefabElement));
        DynamicBuffer<BuildingPrefabElement> definitions =
            _entityManager.GetBuffer<BuildingPrefabElement>(databaseEntity);
        BuildingTypeEnum[] buildingTypes =
        {
            BuildingTypeEnum.Belt,
            BuildingTypeEnum.Miner,
            BuildingTypeEnum.Crafter,
            BuildingTypeEnum.Storage
        };

        for (int i = 0; i < buildingTypes.Length; i++)
        {
            definitions.Add(new BuildingPrefabElement
            {
                type = buildingTypes[i],
                prefab = prefab,
                size = new int2(1)
            });
        }
    }

    private void CreateSpawnRequest(
        BuildingTypeEnum buildingType,
        int2 gridPosition,
        ItemTypeEnum selectedItemType = ItemTypeEnum.None)
    {
        Entity request = _entityManager.CreateEntity(
            typeof(BuildingSpawnRequest));
        _entityManager.SetComponentData(
            request,
            new BuildingSpawnRequest
            {
                type = buildingType,
                gridPosition = gridPosition,
                dir = DirectionEnum.Up,
                selectedItemType = selectedItemType
            });
    }

    private void CreateResearchSelectionRequest(string researchId)
    {
        Entity request = _entityManager.CreateEntity(
            typeof(ResearchSelectionRequest));
        _entityManager.SetComponentData(request, new ResearchSelectionRequest
        {
            researchId = new Unity.Collections.FixedString64Bytes(researchId)
        });
    }

    private T GetSingletonComponent<T>()
        where T : unmanaged, IComponentData
    {
        using EntityQuery query = _entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<T>());
        Assert.That(query.CalculateEntityCount(), Is.EqualTo(1));
        return _entityManager.GetComponentData<T>(query.GetSingletonEntity());
    }
}
