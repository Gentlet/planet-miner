using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

public class WorldTaskMarkerPresentationTests
{
    private World _world;
    private EntityManager _entityManager;
    private WorldTaskMarkerPresentationSystem _markerSystem;

    [SetUp]
    public void SetUp()
    {
        _world = new World(nameof(WorldTaskMarkerPresentationTests));
        _entityManager = _world.EntityManager;
        CreateBuildingPrefabDefinition();
        _markerSystem = _world.GetOrCreateSystemManaged<
            WorldTaskMarkerPresentationSystem>();
    }

    [TearDown]
    public void TearDown()
    {
        _world.Dispose();
    }

    [Test]
    public void ActiveDemolitionTaskCreatesScaledMarkerEntity()
    {
        Entity building = CreateBuilding(
            new int2(4, 5),
            DirectionEnum.Right);
        Entity task = CreateDemolitionTask(building);

        _markerSystem.Update();

        Entity markerEntity = GetSingletonMarker();
        WorldTaskMarker marker = _entityManager
            .GetComponentData<WorldTaskMarker>(markerEntity);
        LocalTransform localTransform = _entityManager
            .GetComponentData<LocalTransform>(markerEntity);
        PostTransformMatrix postTransform = _entityManager
            .GetComponentData<PostTransformMatrix>(markerEntity);

        Assert.That(marker.sourceTask, Is.EqualTo(task));
        Assert.That(marker.targetEntity, Is.EqualTo(building));
        Assert.That(marker.type, Is.EqualTo(WorldTaskMarkerTypeEnum.Demolition));
        Assert.That(localTransform.Position, Is.EqualTo(new float3(4f, 4.5f, 0f)));
        Assert.That(postTransform.Value.c0.x, Is.EqualTo(2f));
        Assert.That(postTransform.Value.c1.y, Is.EqualTo(1f));
        Assert.That(_entityManager.HasComponent<MaterialMeshInfo>(markerEntity),
            Is.True);
        Assert.That(_entityManager.HasComponent<LocalToWorld>(markerEntity),
            Is.True);
    }

    [TestCase(DroneTaskStateEnum.Completed)]
    [TestCase(DroneTaskStateEnum.Cancelled)]
    public void TerminalDemolitionTaskRemovesMarker(
        DroneTaskStateEnum terminalState)
    {
        Entity building = CreateBuilding(int2.zero, DirectionEnum.Up);
        Entity task = CreateDemolitionTask(building);
        _markerSystem.Update();
        Assert.That(CountMarkers(), Is.EqualTo(1));

        _entityManager.SetComponentData(task, new DroneTaskStatus
        {
            state = terminalState
        });
        _markerSystem.Update();

        Assert.That(CountMarkers(), Is.EqualTo(0));
    }

    [Test]
    public void ActiveWorldItemRecoveryTaskCreatesMarkerAtItemPosition()
    {
        Entity item = CreateWorldItem(new float3(2.25f, -3.5f, 0f));
        Entity task = CreateWorldItemRecoveryTask(item);

        _markerSystem.Update();

        Entity markerEntity = GetSingletonMarker();
        WorldTaskMarker marker = _entityManager
            .GetComponentData<WorldTaskMarker>(markerEntity);
        LocalTransform localTransform = _entityManager
            .GetComponentData<LocalTransform>(markerEntity);
        PostTransformMatrix postTransform = _entityManager
            .GetComponentData<PostTransformMatrix>(markerEntity);

        Assert.That(marker.sourceTask, Is.EqualTo(task));
        Assert.That(marker.targetEntity, Is.EqualTo(item));
        Assert.That(marker.type, Is.EqualTo(WorldTaskMarkerTypeEnum.ItemRecovery));
        Assert.That(localTransform.Position,
            Is.EqualTo(new float3(2.25f, -3.5f, 0f)));
        Assert.That(postTransform.Value.c0.x, Is.EqualTo(0.6f));
        Assert.That(postTransform.Value.c1.y, Is.EqualTo(0.6f));
    }

    [Test]
    public void StoredRecoveryItemRemovesMarkerBeforeTaskCompletion()
    {
        Entity item = CreateWorldItem(float3.zero);
        CreateWorldItemRecoveryTask(item);
        _markerSystem.Update();
        Assert.That(CountMarkers(), Is.EqualTo(1));

        _entityManager.AddComponentData(item, new StoredItem
        {
            owner = Entity.Null
        });
        _markerSystem.Update();

        Assert.That(CountMarkers(), Is.EqualTo(0));
    }

    private void CreateBuildingPrefabDefinition()
    {
        Entity database = _entityManager.CreateEntity(
            typeof(BuildingPrefabElement));
        _entityManager.GetBuffer<BuildingPrefabElement>(database).Add(
            new BuildingPrefabElement
            {
                type = BuildingTypeEnum.Storage,
                size = new int2(2, 1)
            });
    }

    private Entity CreateBuilding(int2 gridPosition, DirectionEnum direction)
    {
        Entity building = _entityManager.CreateEntity(
            typeof(BuildingOccupant),
            typeof(BuildingType),
            typeof(GridPosition),
            typeof(Direction));
        _entityManager.SetComponentData(building,
            new BuildingType { type = BuildingTypeEnum.Storage });
        _entityManager.SetComponentData(building,
            new GridPosition { gridPosition = gridPosition });
        _entityManager.SetComponentData(building,
            new Direction { dir = direction });
        return building;
    }

    private Entity CreateDemolitionTask(Entity targetBuilding)
    {
        Entity task = _entityManager.CreateEntity(
            typeof(DroneTask),
            typeof(DroneTaskStatus),
            typeof(DroneDemolitionTaskData));
        _entityManager.SetComponentData(task,
            new DroneTask { type = DroneTaskTypeEnum.Demolition });
        _entityManager.SetComponentData(task, new DroneTaskStatus
        {
            state = DroneTaskStateEnum.Pending
        });
        _entityManager.SetComponentData(task, new DroneDemolitionTaskData
        {
            targetBuilding = targetBuilding
        });
        return task;
    }

    private Entity CreateWorldItem(float3 position)
    {
        Entity item = _entityManager.CreateEntity(
            typeof(Item),
            typeof(GridPosition),
            typeof(LocalTransform));
        _entityManager.SetComponentData(item,
            new Item { type = ItemTypeEnum.Iron });
        _entityManager.SetComponentData(item, new GridPosition
        {
            gridPosition = position.ToGridCell()
        });
        _entityManager.SetComponentData(item,
            LocalTransform.FromPosition(position));
        return item;
    }

    private Entity CreateWorldItemRecoveryTask(Entity itemEntity)
    {
        Entity task = _entityManager.CreateEntity(
            typeof(DroneTask),
            typeof(DroneTaskStatus),
            typeof(DroneWorldItemRecoveryTaskData));
        _entityManager.SetComponentData(task,
            new DroneTask { type = DroneTaskTypeEnum.RecoverWorldItem });
        _entityManager.SetComponentData(task, new DroneTaskStatus
        {
            state = DroneTaskStateEnum.Pending
        });
        _entityManager.SetComponentData(task,
            new DroneWorldItemRecoveryTaskData
            {
                itemEntity = itemEntity
            });
        return task;
    }

    private Entity GetSingletonMarker()
    {
        using EntityQuery query = _entityManager.CreateEntityQuery(
            typeof(WorldTaskMarker));
        return query.GetSingletonEntity();
    }

    private int CountMarkers()
    {
        using EntityQuery query = _entityManager.CreateEntityQuery(
            typeof(WorldTaskMarker));
        return query.CalculateEntityCount();
    }
}
