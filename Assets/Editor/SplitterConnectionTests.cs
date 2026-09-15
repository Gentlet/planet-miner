using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

public class SplitterConnectionTests
{
    private World _world;
    private EntityManager _entityManager;
    private ChunkMapSystem _chunkMap;
    private EndSimulationEntityCommandBufferSystem _endSimulation;
    private SplitterSystem _splitterSystem;

    [SetUp]
    public void SetUp()
    {
        _world = new World(nameof(SplitterConnectionTests));
        _entityManager = _world.EntityManager;
        _endSimulation = _world.GetOrCreateSystemManaged<
            EndSimulationEntityCommandBufferSystem>();
        _chunkMap = _world.GetOrCreateSystemManaged<ChunkMapSystem>();
        _world.GetOrCreateSystemManaged<ItemTrackingSystem>();
        _splitterSystem = _world.GetOrCreateSystemManaged<SplitterSystem>();
    }

    [TearDown]
    public void TearDown()
    {
        _world.Dispose();
    }

    [Test]
    public void OldestInwardBeltBecomesInputAndRotatesSplitter()
    {
        Entity oldestInput = CreateRegisteredBelt(
            new int2(-1, 0),
            DirectionEnum.Right);
        CreateRegisteredBelt(new int2(0, -1), DirectionEnum.Up);
        Entity splitter = CreateSplitter(DirectionEnum.Left);

        _splitterSystem.Update();

        Assert.That(
            _entityManager.GetComponentData<Splitter>(splitter).inputBelt,
            Is.EqualTo(oldestInput));
        Assert.That(
            _entityManager.GetComponentData<Direction>(splitter).dir,
            Is.EqualTo(DirectionEnum.Right));
        quaternion expectedRotation = quaternion.RotateZ(
            math.radians((float)DirectionEnum.Right.ToDegrees()));
        quaternion actualRotation = _entityManager
            .GetComponentData<LocalTransform>(splitter)
            .Rotation;
        Assert.That(
            math.distance(actualRotation.value, expectedRotation.value),
            Is.LessThan(0.0001f));
    }

    [Test]
    public void LaterInputCandidateDoesNotReplaceSelectedInput()
    {
        Entity selectedInput = CreateRegisteredBelt(
            new int2(-1, 0),
            DirectionEnum.Right);
        Entity splitter = CreateSplitter(DirectionEnum.Up);
        _splitterSystem.Update();

        CreateRegisteredBelt(new int2(0, -1), DirectionEnum.Up);
        _splitterSystem.Update();

        Assert.That(
            _entityManager.GetComponentData<Splitter>(splitter).inputBelt,
            Is.EqualTo(selectedInput));
        Assert.That(
            _entityManager.GetComponentData<Direction>(splitter).dir,
            Is.EqualTo(DirectionEnum.Right));
    }

    [Test]
    public void RemovingInputSelectsOldestRemainingCandidate()
    {
        Entity firstInput = CreateRegisteredBelt(
            new int2(-1, 0),
            DirectionEnum.Right);
        Entity remainingInput = CreateRegisteredBelt(
            new int2(0, -1),
            DirectionEnum.Up);
        Entity splitter = CreateSplitter(DirectionEnum.Left);
        _splitterSystem.Update();

        RemoveBelt(firstInput);
        _splitterSystem.Update();

        Assert.That(
            _entityManager.GetComponentData<Splitter>(splitter).inputBelt,
            Is.EqualTo(remainingInput));
        Assert.That(
            _entityManager.GetComponentData<Direction>(splitter).dir,
            Is.EqualTo(DirectionEnum.Up));
    }

    [Test]
    public void OutputsUseForwardRightLeftRoundRobinOrder()
    {
        CreateRegisteredBelt(new int2(-1, 0), DirectionEnum.Right);
        CreateRegisteredBelt(new int2(1, 0), DirectionEnum.Right);
        CreateRegisteredBelt(new int2(0, -1), DirectionEnum.Down);
        CreateRegisteredBelt(new int2(0, 1), DirectionEnum.Up);
        Entity splitter = CreateSplitter(DirectionEnum.Up);
        Entity firstItem = CreateWorldItem(new float3(-0.15f, 0f, 0f));
        Entity secondItem = CreateWorldItem(new float3(-0.25f, 0f, 0f));
        Entity thirdItem = CreateWorldItem(new float3(-0.35f, 0f, 0f));

        _splitterSystem.Update();

        Assert.That(GetItemCell(firstItem), Is.EqualTo(new int2(1, 0)));
        Assert.That(GetItemCell(secondItem), Is.EqualTo(new int2(0, -1)));
        Assert.That(GetItemCell(thirdItem), Is.EqualTo(new int2(0, 1)));
        Assert.That(
            _entityManager.GetComponentData<Splitter>(splitter)
                .nextOutputDirection,
            Is.EqualTo(DirectionEnum.Right));
    }

    [Test]
    public void OutputBeltMustPointAwayFromSplitter()
    {
        CreateRegisteredBelt(new int2(-1, 0), DirectionEnum.Right);
        CreateRegisteredBelt(new int2(1, 0), DirectionEnum.Left);
        CreateSplitter(DirectionEnum.Up);
        Entity item = CreateWorldItem(new float3(-0.25f, 0f, 0f));

        _splitterSystem.Update();

        Assert.That(GetItemCell(item), Is.EqualTo(int2.zero));
    }

    [Test]
    public void ItemAlreadyInsideContinuesAfterSelectedInputIsRemoved()
    {
        Entity inputBelt = CreateRegisteredBelt(
            new int2(-1, 0),
            DirectionEnum.Right);
        CreateRegisteredBelt(new int2(1, 0), DirectionEnum.Right);
        Entity splitter = CreateSplitter(DirectionEnum.Up);
        Entity item = CreateWorldItem(new float3(-0.25f, 0f, 0f));
        Entity blocker = CreateWorldItem(new float3(1f, 0f, 0f));

        _splitterSystem.Update();
        Assert.That(GetItemCell(item), Is.EqualTo(int2.zero));

        RemoveBelt(inputBelt);
        Assert.That(
            _chunkMap.TryUnregisterItem(new int2(1, 0), blocker),
            Is.True);
        _entityManager.DestroyEntity(blocker);
        _splitterSystem.Update();

        Assert.That(GetItemCell(item), Is.EqualTo(new int2(1, 0)));
        Assert.That(
            _entityManager.GetBuffer<SplitterRetainedItemElement>(splitter)
                .Length,
            Is.EqualTo(0));
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
            typeof(BuildingFootprint),
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
        _entityManager.SetComponentData(
            belt,
            new BuildingFootprint { size = new int2(1, 1) });

        Assert.That(_chunkMap.TryReserveBuilding(cell), Is.True);
        _chunkMap.Update();
        _endSimulation.Update();
        Assert.That(
            _entityManager.GetComponentData<Belt>(belt).installationOrder,
            Is.GreaterThan(0));
        return belt;
    }

    private Entity CreateSplitter(DirectionEnum direction)
    {
        Entity splitter = _entityManager.CreateEntity(
            typeof(Splitter),
            typeof(SplitterRetainedItemElement),
            typeof(GridPosition),
            typeof(Direction),
            typeof(LocalTransform));
        _entityManager.SetComponentData(
            splitter,
            new GridPosition { gridPosition = int2.zero });
        _entityManager.SetComponentData(
            splitter,
            new Direction { dir = direction });
        _entityManager.SetComponentData(
            splitter,
            LocalTransform.FromPositionRotation(
                float3.zero,
                quaternion.RotateZ(
                    math.radians((float)direction.ToDegrees()))));
        return splitter;
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
            new GridPosition { gridPosition = position.ToGridCell() });
        Assert.That(
            _chunkMap.TryRegisterItem(position.ToGridCell(), item),
            Is.True);
        return item;
    }

    private int2 GetItemCell(Entity item)
    {
        return _entityManager.GetComponentData<GridPosition>(item).gridPosition;
    }

    private void RemoveBelt(Entity belt)
    {
        Assert.That(_chunkMap.TryUnregisterBuilding(belt), Is.True);
        _entityManager.DestroyEntity(belt);
    }
}
