using NUnit.Framework;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

public class CoalGeneratorFuelAndRestoreTests
{
    private World _world;
    private EntityManager _entityManager;
    private ItemStorageSystem _itemStorage;
    private CoalGeneratorFuelSystem _fuelSystem;
    private EndSimulationEntityCommandBufferSystem _endSimulationEcb;

    [SetUp]
    public void SetUp()
    {
        _world = new World(nameof(CoalGeneratorFuelAndRestoreTests));
        _entityManager = _world.EntityManager;
        _itemStorage = _world.GetOrCreateSystemManaged<ItemStorageSystem>();
        _endSimulationEcb = _world
            .GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>();
        _fuelSystem = _world
            .GetOrCreateSystemManaged<CoalGeneratorFuelSystem>();
        Entity configEntity = _entityManager.CreateEntity(
            typeof(CoalGeneratorConfig));
        _entityManager.SetComponentData(
            configEntity,
            new CoalGeneratorConfig
            {
                coalEnergyPerItem = 600f,
                fuelStorageCapacity = 10
            });
        _world.SetTime(new TimeData(1d, 1f));
    }

    [TearDown]
    public void TearDown()
    {
        _world.Dispose();
    }

    [Test]
    public void ZeroGenerationDoesNotConsumeInternalFuel()
    {
        Entity generator = CreateGenerator(100f, 0f);

        _fuelSystem.Update();

        CoalGenerator state = _entityManager
            .GetComponentData<CoalGenerator>(generator);
        Assert.That(state.remainingFuelEnergy, Is.EqualTo(100f));
    }

    [Test]
    public void PartialGenerationConsumesProportionalInternalFuel()
    {
        Entity generator = CreateGenerator(100f, 25f);

        _fuelSystem.Update();

        CoalGenerator state = _entityManager
            .GetComponentData<CoalGenerator>(generator);
        Assert.That(state.remainingFuelEnergy, Is.EqualTo(75f));
    }

    [Test]
    public void StoredCoalIsConsumedThroughStorageOwnershipApi()
    {
        Entity generator = CreateGenerator(0f, 100f);
        Entity coalItem = _entityManager.CreateEntity();
        _entityManager.GetBuffer<StoredItemElement>(generator).Add(
            new StoredItemElement
            {
                itemEntity = coalItem,
                type = ItemTypeEnum.Coal
            });

        _fuelSystem.Update();
        _endSimulationEcb.Update();

        CoalGenerator state = _entityManager
            .GetComponentData<CoalGenerator>(generator);
        Assert.That(
            _entityManager.GetBuffer<StoredItemElement>(generator).Length,
            Is.EqualTo(0));
        Assert.That(_entityManager.Exists(coalItem), Is.False);
        Assert.That(state.remainingFuelEnergy, Is.EqualTo(500f));
    }

    [Test]
    public void RestoreItemsUsedByDestroyReturnsUnconsumedCoalToWorld()
    {
        Entity generator = _entityManager.CreateEntity(
            typeof(StoredItemElement));
        Entity coalItem = _entityManager.CreateEntity(
            typeof(Item),
            typeof(StoredItem),
            typeof(Disabled),
            typeof(LocalTransform),
            typeof(LocalToWorld),
            typeof(GridPosition),
            typeof(ItemCellChanged));
        _entityManager.SetComponentData(
            coalItem,
            new Item { type = ItemTypeEnum.Coal });
        _entityManager.SetComponentData(
            coalItem,
            new StoredItem { owner = generator });
        _entityManager.SetComponentEnabled<ItemCellChanged>(coalItem, false);
        _entityManager.GetBuffer<StoredItemElement>(generator).Add(
            new StoredItemElement
            {
                itemEntity = coalItem,
                type = ItemTypeEnum.Coal
            });

        EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.Temp);
        int2 restoreCell = new int2(3, 4);
        DynamicBuffer<StoredItemElement> storedItems =
            _entityManager.GetBuffer<StoredItemElement>(generator);
        _itemStorage.RestoreItems(ref ecb, storedItems, restoreCell);
        ecb.Playback(_entityManager);
        ecb.Dispose();

        Assert.That(
            _entityManager.GetBuffer<StoredItemElement>(generator).Length,
            Is.EqualTo(0));
        Assert.That(
            _entityManager.HasComponent<StoredItem>(coalItem),
            Is.False);
        Assert.That(
            _entityManager.HasComponent<Disabled>(coalItem),
            Is.False);
        Assert.That(
            _entityManager.IsComponentEnabled<ItemCellChanged>(coalItem),
            Is.True);
        Assert.That(
            _entityManager.GetComponentData<GridPosition>(coalItem)
                .gridPosition,
            Is.EqualTo(restoreCell));
    }

    private Entity CreateGenerator(
        float remainingFuelEnergy,
        float currentGeneration)
    {
        Entity generator = _entityManager.CreateEntity(
            typeof(CoalGenerator),
            typeof(PowerGenerator),
            typeof(StoredItemElement));
        _entityManager.SetComponentData(
            generator,
            new CoalGenerator
            {
                remainingFuelEnergy = remainingFuelEnergy
            });
        _entityManager.SetComponentData(
            generator,
            new PowerGenerator
            {
                type = PowerGeneratorTypeEnum.CoalGenerator,
                maximumGeneration = 100f,
                currentGeneration = currentGeneration
            });
        return generator;
    }
}
