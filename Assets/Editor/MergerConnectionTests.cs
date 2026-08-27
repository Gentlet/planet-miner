using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

public class MergerConnectionTests
{
    private World _world;
    private EntityManager _entityManager;
    private ChunkMapSystem _chunkMap;
    private EndSimulationEntityCommandBufferSystem _endSimulation;
    private MergerSystem _mergerSystem;

    [SetUp]
    public void SetUp()
    {
        _world = new World(nameof(MergerConnectionTests));
        _entityManager = _world.EntityManager;
        _endSimulation = _world.GetOrCreateSystemManaged<
            EndSimulationEntityCommandBufferSystem>();
        _chunkMap = _world.GetOrCreateSystemManaged<ChunkMapSystem>();
        _world.GetOrCreateSystemManaged<ItemTrackingSystem>();
        _mergerSystem = _world.GetOrCreateSystemManaged<MergerSystem>();
    }

    [TearDown]
    public void TearDown()
    {
        _world.Dispose();
    }

    [Test]
    public void OldestOutwardBeltBecomesOutputAndRotatesMerger()
    {
        Entity oldestOutput = CreateRegisteredBelt(
            new int2(0, 1),
            DirectionEnum.Up);
        CreateRegisteredBelt(new int2(1, 0), DirectionEnum.Right);
        Entity merger = CreateMerger(DirectionEnum.Down);

        _mergerSystem.Update();

        Assert.That(
            _entityManager.GetComponentData<Merger>(merger).outputBelt,
            Is.EqualTo(oldestOutput));
        Assert.That(
            _entityManager.GetComponentData<Direction>(merger).dir,
            Is.EqualTo(DirectionEnum.Up));
        quaternion expectedRotation = quaternion.RotateZ(
            math.radians((float)DirectionEnum.Up.ToDegrees()));
        quaternion actualRotation = _entityManager
            .GetComponentData<LocalTransform>(merger)
            .Rotation;
        Assert.That(
            math.distance(actualRotation.value, expectedRotation.value),
            Is.LessThan(0.0001f));
    }

    [Test]
    public void LaterOutputCandidateDoesNotReplaceSelectedOutput()
    {
        Entity selectedOutput = CreateRegisteredBelt(
            new int2(1, 0),
            DirectionEnum.Right);
        Entity merger = CreateMerger(DirectionEnum.Up);
        _mergerSystem.Update();

        CreateRegisteredBelt(new int2(0, 1), DirectionEnum.Up);
        _mergerSystem.Update();

        Assert.That(
            _entityManager.GetComponentData<Merger>(merger).outputBelt,
            Is.EqualTo(selectedOutput));
        Assert.That(
            _entityManager.GetComponentData<Direction>(merger).dir,
            Is.EqualTo(DirectionEnum.Right));
    }

    [Test]
    public void RemovingOutputSelectsOldestRemainingCandidate()
    {
        Entity firstOutput = CreateRegisteredBelt(
            new int2(0, 1),
            DirectionEnum.Up);
        Entity remainingOutput = CreateRegisteredBelt(
            new int2(1, 0),
            DirectionEnum.Right);
        Entity merger = CreateMerger(DirectionEnum.Down);
        _mergerSystem.Update();

        Assert.That(_chunkMap.TryUnregisterBuilding(firstOutput), Is.True);
        _entityManager.DestroyEntity(firstOutput);
        _mergerSystem.Update();

        Assert.That(
            _entityManager.GetComponentData<Merger>(merger).outputBelt,
            Is.EqualTo(remainingOutput));
        Assert.That(
            _entityManager.GetComponentData<Direction>(merger).dir,
            Is.EqualTo(DirectionEnum.Right));
    }

    [TestCase(DirectionEnum.Right, true)]
    [TestCase(DirectionEnum.Left, false)]
    public void InputRequiresBeltPointingTowardMerger(
        DirectionEnum inputBeltDirection,
        bool shouldOutputItem)
    {
        CreateRegisteredBelt(new int2(1, 0), DirectionEnum.Right);
        CreateRegisteredBelt(new int2(-1, 0), inputBeltDirection);
        CreateMerger(DirectionEnum.Up);
        Entity item = CreateWorldItem(new float3(-0.25f, 0f, 0f));

        _mergerSystem.Update();

        int2 expectedCell = shouldOutputItem
            ? new int2(1, 0)
            : int2.zero;
        Assert.That(
            _entityManager.GetComponentData<GridPosition>(item).gridPosition,
            Is.EqualTo(expectedCell));
    }

    private Entity CreateRegisteredBelt(
        int2 cell,
        DirectionEnum direction)
    {
        Entity belt = _entityManager.CreateEntity(
            typeof(Belt),
            typeof(BuildingType),
            typeof(GridPosition),
            typeof(Direction),
            typeof(BuildingOccupantRequest));
        _entityManager.SetComponentData(
            belt,
            new Belt { speed = 10f });
        _entityManager.SetComponentData(
            belt,
            new BuildingType { type = BuildingTypeEnum.Belt });
        _entityManager.SetComponentData(
            belt,
            new GridPosition { gridPosition = cell });
        _entityManager.SetComponentData(
            belt,
            new Direction { dir = direction });

        Assert.That(_chunkMap.TryReserveBuilding(cell), Is.True);
        _chunkMap.Update();
        _endSimulation.Update();
        Assert.That(
            _entityManager.GetComponentData<Belt>(belt).installationOrder,
            Is.GreaterThan(0));
        return belt;
    }

    private Entity CreateMerger(DirectionEnum direction)
    {
        Entity merger = _entityManager.CreateEntity(
            typeof(Merger),
            typeof(GridPosition),
            typeof(Direction),
            typeof(LocalTransform));
        _entityManager.SetComponentData(
            merger,
            new GridPosition { gridPosition = int2.zero });
        _entityManager.SetComponentData(
            merger,
            new Direction { dir = direction });
        _entityManager.SetComponentData(
            merger,
            LocalTransform.FromPositionRotation(
                float3.zero,
                quaternion.RotateZ(
                    math.radians((float)direction.ToDegrees()))));
        return merger;
    }

    private Entity CreateWorldItem(float3 position)
    {
        Entity item = _entityManager.CreateEntity(
            typeof(Item),
            typeof(LocalTransform),
            typeof(GridPosition),
            typeof(ItemCellChanged));
        _entityManager.SetComponentData(
            item,
            LocalTransform.FromPosition(position));
        _entityManager.SetComponentData(
            item,
            new GridPosition { gridPosition = int2.zero });
        Assert.That(_chunkMap.TryRegisterItem(int2.zero, item), Is.True);
        return item;
    }
}
