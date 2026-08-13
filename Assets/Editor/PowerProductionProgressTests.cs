using NUnit.Framework;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;

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

    private Entity CreatePowerConsumer(float supplyRatio)
    {
        Entity entity = _entityManager.CreateEntity(typeof(PowerConsumer));
        SetSupplyRatio(entity, supplyRatio);
        return entity;
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
        return _entityManager.CreateEntity(
            typeof(CrafterConfig),
            typeof(CrafterRecipeElement),
            typeof(CrafterRecipeIngredientElement));
    }
}
