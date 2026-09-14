using NUnit.Framework;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

public class PowerProductionProgressTests
{
    private World _world;
    private EntityManager _entityManager;

    [SetUp]
    public void SetUp()
    {
        _world = new World(nameof(PowerProductionProgressTests));
        _entityManager = _world.EntityManager;
    }

    [TearDown]
    public void TearDown()
    {
        _world.Dispose();
    }

    [Test]
    public void FullSupplyAdvancesMinerAtNormalRate()
    {
        Entity minerEntity = CreatePowerConsumer(1f);
        Miner miner = new Miner { speed = 10f, timer = 2f };

        miner.timer += GetProgressDeltaTime(minerEntity, 4f);

        Assert.That(miner.speed, Is.EqualTo(10f));
        Assert.That(miner.timer, Is.EqualTo(6f));
    }

    [Test]
    public void HalfSupplyAdvancesCrafterAtHalfRate()
    {
        Entity crafterEntity = CreatePowerConsumer(0.5f);
        Crafter crafter = new Crafter { speed = 2f, progress = 3f };

        crafter.progress += GetProgressDeltaTime(crafterEntity, 4f);

        Assert.That(crafter.speed, Is.EqualTo(2f));
        Assert.That(crafter.progress, Is.EqualTo(5f));
    }

    [Test]
    public void ZeroSupplyPreservesMinerTimerAndCrafterProgress()
    {
        Entity consumerEntity = CreatePowerConsumer(0f);
        Miner miner = new Miner { timer = 2.5f };
        Crafter crafter = new Crafter { progress = 7.5f };

        float progressDeltaTime = GetProgressDeltaTime(consumerEntity, 10f);
        miner.timer += progressDeltaTime;
        crafter.progress += progressDeltaTime;

        Assert.That(miner.timer, Is.EqualTo(2.5f));
        Assert.That(crafter.progress, Is.EqualTo(7.5f));
    }

    [Test]
    public void ProgressContinuesFromSamePointAfterSupplyRecovers()
    {
        Entity consumerEntity = CreatePowerConsumer(0f);
        Crafter crafter = new Crafter { progress = 3f };

        crafter.progress += GetProgressDeltaTime(consumerEntity, 4f);
        SetSupplyRatio(consumerEntity, 0.5f);
        crafter.progress += GetProgressDeltaTime(consumerEntity, 4f);
        SetSupplyRatio(consumerEntity, 1f);
        crafter.progress += GetProgressDeltaTime(consumerEntity, 2f);

        Assert.That(crafter.progress, Is.EqualTo(7f));
    }

    [Test]
    public void BuildingWithoutPowerConsumerKeepsLegacyProgressRate()
    {
        Entity buildingEntity = _entityManager.CreateEntity();

        Assert.That(
            GetProgressDeltaTime(buildingEntity, 3f),
            Is.EqualTo(3f));
    }

    [Test]
    public void MiningSystemUsesPowerSupplyRatio()
    {
        CreateSimulationDependencies();
        Entity minerEntity = _entityManager.CreateEntity(
            typeof(Miner),
            typeof(GridPosition),
            typeof(Direction),
            typeof(BuildingOutputCursor),
            typeof(ProducedItemElement),
            typeof(PowerConsumer));
        _entityManager.SetComponentData(
            minerEntity,
            new Miner
            {
                speed = 10f,
                timer = 2f,
                randomState = 1u
            });
        SetSupplyRatio(minerEntity, 0.5f);
        _world.SetTime(new TimeData(1d, 4f));

        MiningSystem miningSystem =
            _world.GetOrCreateSystemManaged<MiningSystem>();
        miningSystem.Update();

        Miner miner = _entityManager.GetComponentData<Miner>(minerEntity);
        Assert.That(miner.timer, Is.EqualTo(4f));
    }

    [Test]
    public void CrafterSystemUsesPowerSupplyRatio()
    {
        Entity crafterConfigEntity = CreateSimulationDependencies();
        _entityManager.GetBuffer<CrafterRecipeElement>(crafterConfigEntity).Add(
            new CrafterRecipeElement
            {
                id = 1,
                outputItemType = ItemTypeEnum.Iron,
                craftTime = 10f,
                conditionFlags = CrafterRecipeConditionFlags.None
            });
        Entity crafterEntity = _entityManager.CreateEntity(
            typeof(Crafter),
            typeof(GridPosition),
            typeof(Direction),
            typeof(BuildingOutputCursor),
            typeof(StoredItemElement),
            typeof(ProducedItemElement),
            typeof(PowerConsumer));
        _entityManager.SetComponentData(
            crafterEntity,
            new Crafter
            {
                speed = 1f,
                selectedItemType = ItemTypeEnum.Iron,
                progress = 3f,
                state = CrafterStateEnum.Crafting
            });
        SetSupplyRatio(crafterEntity, 0.5f);
        _world.SetTime(new TimeData(1d, 4f));

        CrafterSystem crafterSystem =
            _world.GetOrCreateSystemManaged<CrafterSystem>();
        crafterSystem.Update();

        Crafter crafter = _entityManager
            .GetComponentData<Crafter>(crafterEntity);
        Assert.That(crafter.progress, Is.EqualTo(5f));
        Assert.That(crafter.state, Is.EqualTo(CrafterStateEnum.Crafting));
    }

    [Test]
    public void TwoResearchBuildingsCompleteOneGlobalResearchInParallel()
    {
        _world.GetOrCreateSystemManaged<ChunkMapSystem>();
        _world.GetOrCreateSystemManaged<ItemTrackingSystem>();
        _world.GetOrCreateSystemManaged<ItemStorageSystem>();
        EndSimulationEntityCommandBufferSystem endSimulationEcb = _world
            .GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>();

        Entity buildingConfig = _entityManager.CreateEntity(
            typeof(BuildingPrefabElement));
        _entityManager.GetBuffer<BuildingPrefabElement>(buildingConfig).Add(
            new BuildingPrefabElement
            {
                type = BuildingTypeEnum.ResearchBuilding,
                size = new int2(3, 3)
            });

        Entity researchConfig = _entityManager.CreateEntity(
            typeof(ResearchConfig),
            typeof(ResearchState),
            typeof(ResearchDefinitionElement),
            typeof(ResearchIngredientElement),
            typeof(ResearchRewardElement),
            typeof(ResearchProgressElement),
            typeof(BuildingUnlockElement),
            typeof(RecipeUnlockElement),
            typeof(ResearchStatModifierElement));
        Unity.Collections.FixedString64Bytes researchId =
            new("parallel_test");
        _entityManager.SetComponentData(researchConfig, new ResearchState
        {
            activeResearchId = researchId
        });
        _entityManager.GetBuffer<ResearchDefinitionElement>(researchConfig).Add(
            new ResearchDefinitionElement
            {
                stableId = researchId,
                cycleDuration = 5f,
                progressPerCycle = 10f,
                requiredProgress = 20f
            });
        _entityManager.GetBuffer<ResearchIngredientElement>(researchConfig).Add(
            new ResearchIngredientElement
            {
                researchId = researchId,
                itemType = ItemTypeEnum.Iron,
                amount = 1
            });
        _entityManager.GetBuffer<ResearchRewardElement>(researchConfig).Add(
            new ResearchRewardElement
            {
                researchId = researchId,
                type = ResearchRewardTypeEnum.BuildingUnlock,
                buildingType = BuildingTypeEnum.Splitter
            });
        _entityManager.GetBuffer<ResearchProgressElement>(researchConfig).Add(
            new ResearchProgressElement { researchId = researchId });

        Entity firstBuilding = CreateResearchBuilding(new int2(0, 0));
        Entity secondBuilding = CreateResearchBuilding(new int2(5, 0));
        AddStoredResearchItem(firstBuilding, ItemTypeEnum.Iron);
        AddStoredResearchItem(secondBuilding, ItemTypeEnum.Iron);
        ResearchSystem researchSystem = _world
            .GetOrCreateSystemManaged<ResearchSystem>();

        _world.SetTime(new TimeData(0d, 0f));
        researchSystem.Update();

        Assert.That(
            _entityManager.GetComponentData<ResearchBuilding>(firstBuilding)
                .cycleActive,
            Is.True);
        Assert.That(
            _entityManager.GetComponentData<ResearchBuilding>(secondBuilding)
                .cycleActive,
            Is.True);
        Assert.That(
            _entityManager.GetBuffer<StoredItemElement>(firstBuilding).Length,
            Is.Zero);
        Assert.That(
            _entityManager.GetBuffer<StoredItemElement>(secondBuilding).Length,
            Is.Zero);

        _world.SetTime(new TimeData(5d, 5f));
        researchSystem.Update();
        endSimulationEcb.Update();

        ResearchProgressElement completedProgress = _entityManager
            .GetBuffer<ResearchProgressElement>(researchConfig)[0];
        ResearchState state = _entityManager
            .GetComponentData<ResearchState>(researchConfig);
        DynamicBuffer<BuildingUnlockElement> unlocks = _entityManager
            .GetBuffer<BuildingUnlockElement>(researchConfig);
        Assert.That(completedProgress.progress, Is.EqualTo(20f));
        Assert.That(completedProgress.completed, Is.True);
        Assert.That(state.activeResearchId.Length, Is.Zero);
        Assert.That(
            unlocks.IsBuildingUnlocked(BuildingTypeEnum.Splitter),
            Is.True);
        Assert.That(
            _entityManager.GetComponentData<ResearchBuilding>(firstBuilding)
                .cycleActive,
            Is.False);
        Assert.That(
            _entityManager.GetComponentData<ResearchBuilding>(secondBuilding)
                .cycleActive,
            Is.False);
    }

    [Test]
    public void ResearchSystemStoresFirstWorldInputWithoutInvalidatingConfigBuffers()
    {
        ChunkMapSystem chunkMap = _world
            .GetOrCreateSystemManaged<ChunkMapSystem>();
        _world.GetOrCreateSystemManaged<ItemTrackingSystem>();
        _world.GetOrCreateSystemManaged<ItemStorageSystem>();
        _world.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>();

        Entity buildingConfig = _entityManager.CreateEntity(
            typeof(BuildingPrefabElement));
        _entityManager.GetBuffer<BuildingPrefabElement>(buildingConfig).Add(
            new BuildingPrefabElement
            {
                type = BuildingTypeEnum.ResearchBuilding,
                size = new int2(3, 3)
            });

        Unity.Collections.FixedString64Bytes researchId =
            new("world_input_test");
        Entity researchConfig = _entityManager.CreateEntity(
            typeof(ResearchConfig),
            typeof(ResearchState),
            typeof(ResearchDefinitionElement),
            typeof(ResearchIngredientElement),
            typeof(ResearchRewardElement),
            typeof(ResearchProgressElement),
            typeof(BuildingUnlockElement),
            typeof(RecipeUnlockElement),
            typeof(ResearchStatModifierElement));
        _entityManager.SetComponentData(researchConfig, new ResearchState
        {
            activeResearchId = researchId
        });
        _entityManager.GetBuffer<ResearchDefinitionElement>(researchConfig).Add(
            new ResearchDefinitionElement
            {
                stableId = researchId,
                cycleDuration = 5f,
                progressPerCycle = 10f,
                requiredProgress = 100f
            });
        _entityManager.GetBuffer<ResearchIngredientElement>(researchConfig).Add(
            new ResearchIngredientElement
            {
                researchId = researchId,
                itemType = ItemTypeEnum.Iron,
                amount = 1
            });
        _entityManager.GetBuffer<ResearchProgressElement>(researchConfig).Add(
            new ResearchProgressElement { researchId = researchId });

        Entity researchBuilding = CreateResearchBuilding(int2.zero);
        Entity worldItem = _entityManager.CreateEntity(
            typeof(Item),
            typeof(LocalTransform),
            typeof(LocalToWorld),
            typeof(GridPosition),
            typeof(ItemCellChanged));
        _entityManager.SetComponentData(
            worldItem,
            new Item { type = ItemTypeEnum.Iron });
        _entityManager.SetComponentData(
            worldItem,
            LocalTransform.FromPosition(float3.zero));
        _entityManager.SetComponentData(
            worldItem,
            new GridPosition { gridPosition = int2.zero });
        _entityManager.SetComponentEnabled<ItemCellChanged>(worldItem, false);
        Assert.That(chunkMap.TryRegisterItem(int2.zero, worldItem), Is.True);

        ResearchSystem researchSystem = _world
            .GetOrCreateSystemManaged<ResearchSystem>();
        _world.SetTime(new TimeData(0d, 0f));

        Assert.DoesNotThrow(() => researchSystem.Update());

        ResearchBuilding building = _entityManager
            .GetComponentData<ResearchBuilding>(researchBuilding);
        Assert.That(building.cycleActive, Is.True);
        Assert.That(
            _entityManager.GetBuffer<StoredItemElement>(researchBuilding).Length,
            Is.Zero);
    }

    private Entity CreatePowerConsumer(float supplyRatio)
    {
        Entity entity = _entityManager.CreateEntity(typeof(PowerConsumer));
        SetSupplyRatio(entity, supplyRatio);
        return entity;
    }

    private Entity CreateResearchBuilding(int2 position)
    {
        Entity entity = _entityManager.CreateEntity(
            typeof(ResearchBuilding),
            typeof(GridPosition),
            typeof(Direction),
            typeof(StoredItemElement),
            typeof(PowerConsumer));
        _entityManager.SetComponentData(entity, new ResearchBuilding
        {
            speed = 1f,
            state = ResearchBuildingStateEnum.WaitingForMaterials
        });
        _entityManager.SetComponentData(entity, new GridPosition
        {
            gridPosition = position
        });
        _entityManager.SetComponentData(entity, new Direction
        {
            dir = DirectionEnum.Up
        });
        _entityManager.SetComponentData(entity, new PowerConsumer
        {
            supplyRatio = 1f
        });
        return entity;
    }

    private void AddStoredResearchItem(Entity owner, ItemTypeEnum itemType)
    {
        Entity item = _entityManager.CreateEntity(
            typeof(Item),
            typeof(StoredItem),
            typeof(Disabled));
        _entityManager.SetComponentData(item, new Item { type = itemType });
        _entityManager.SetComponentData(item, new StoredItem { owner = owner });
        _entityManager.GetBuffer<StoredItemElement>(owner).Add(
            new StoredItemElement
            {
                itemEntity = item,
                type = itemType
            });
    }

    private void SetSupplyRatio(Entity entity, float supplyRatio)
    {
        _entityManager.SetComponentData(
            entity,
            new PowerConsumer { supplyRatio = supplyRatio });
    }

    private float GetProgressDeltaTime(Entity entity, float deltaTime)
    {
        return PowerProductionUtility.GetProgressDeltaTime(
            _entityManager,
            entity,
            deltaTime);
    }

    private Entity CreateSimulationDependencies()
    {
        _world.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>();
        _world.GetOrCreateSystemManaged<ChunkMapSystem>();
        _world.GetOrCreateSystemManaged<ItemTrackingSystem>();
        _world.GetOrCreateSystemManaged<ItemStorageSystem>();

        Entity buildingConfigEntity = _entityManager.CreateEntity(
            typeof(BuildingPrefabElement));
        DynamicBuffer<BuildingPrefabElement> buildingDefinitions =
            _entityManager.GetBuffer<BuildingPrefabElement>(
                buildingConfigEntity);
        buildingDefinitions.Add(new BuildingPrefabElement
        {
            type = BuildingTypeEnum.Miner,
            size = new int2(1, 1)
        });
        buildingDefinitions.Add(new BuildingPrefabElement
        {
            type = BuildingTypeEnum.Crafter,
            size = new int2(1, 1)
        });

        _entityManager.CreateEntity(
            typeof(ItemPrefabElement),
            typeof(ItemStorageLimitElement));
        Entity researchConfigEntity = _entityManager.CreateEntity(
            typeof(ResearchConfig),
            typeof(ResearchState),
            typeof(ResearchStatModifierElement),
            typeof(RecipeUnlockElement));
        _entityManager.GetBuffer<RecipeUnlockElement>(researchConfigEntity).Add(
            new RecipeUnlockElement { recipeId = 1 });
        return _entityManager.CreateEntity(
            typeof(CrafterConfig),
            typeof(CrafterRecipeElement),
            typeof(CrafterRecipeIngredientElement));
    }
}
