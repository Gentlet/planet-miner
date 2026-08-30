using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

public class DroneDemolitionRecoveryTests
{
    private World _world;
    private EntityManager _entityManager;
    private ChunkMapSystem _chunkMap;
    private DroneStationNetworkSystem _networkSystem;

    [SetUp]
    public void SetUp()
    {
        _world = new World(nameof(DroneDemolitionRecoveryTests));
        _entityManager = _world.EntityManager;
        _chunkMap = _world.GetOrCreateSystemManaged<ChunkMapSystem>();
        _world.GetOrCreateSystemManaged<ItemTrackingSystem>();
        _world.GetOrCreateSystemManaged<ItemStorageSystem>();
        _networkSystem = _world.GetOrCreateSystemManaged<
            DroneStationNetworkSystem>();
        CreateDroneAndStorageConfig();
    }

    [TearDown]
    public void TearDown()
    {
        _world.Dispose();
    }

    [Test]
    public void UserInputCreatesDemolitionTaskWithRequestedPriority()
    {
        Entity building = CreateRegisteredBuilding(
            new int2(3, 4),
            BuildingTypeEnum.Storage);
        Entity request = _entityManager.CreateEntity(
            typeof(DroneDemolitionRequest));
        _entityManager.SetComponentData(request,
            new DroneDemolitionRequest
            {
                gridPosition = new int2(3, 4),
                normalPriority = 3
            });
        DroneDemolitionRequestSystem requestSystem = _world
            .GetOrCreateSystemManaged<DroneDemolitionRequestSystem>();

        requestSystem.Update();

        Assert.That(_entityManager.Exists(building), Is.True);
        Assert.That(Count<BuildingDestroyRequest>(), Is.EqualTo(0));
        Assert.That(Count<DroneTaskCreateRequest>(), Is.EqualTo(1));
        Entity taskRequest = GetSingleton<DroneTaskCreateRequest>();
        Assert.That(
            _entityManager.GetComponentData<DroneTaskCreateRequest>(taskRequest).type,
            Is.EqualTo(DroneTaskTypeEnum.Demolition));
        Assert.That(
            _entityManager.GetComponentData<DroneTaskCreateRequest>(taskRequest)
                .normalPriority,
            Is.EqualTo(3));
    }

    [Test]
    public void AreaSelectionCreatesDemolitionAndWorldItemRecoveryRequests()
    {
        CreateRegisteredBuilding(new int2(3, 4), BuildingTypeEnum.Storage);
        CreateRegisteredBuilding(new int2(5, 4), BuildingTypeEnum.Storage);
        CreateWorldItem(new int2(3, 5), ItemTypeEnum.Iron);
        CreateWorldItem(new int2(5, 5), ItemTypeEnum.Copper);

        DemolitionAreaRequestUtility.CreateRequests(
            _entityManager,
            _chunkMap,
            new GridBounds(new int2(3, 4), new int2(5, 5)),
            3);

        Assert.That(Count<DroneDemolitionRequest>(), Is.EqualTo(2));
        Assert.That(Count<DroneWorldItemRecoveryCreateRequest>(), Is.EqualTo(2));

        using EntityQuery demolitionQuery = _entityManager.CreateEntityQuery(
            typeof(DroneDemolitionRequest));
        using var demolitionRequests = demolitionQuery.ToComponentDataArray<
            DroneDemolitionRequest>(Unity.Collections.Allocator.Temp);

        for (int i = 0; i < demolitionRequests.Length; i++)
        {
            Assert.That(demolitionRequests[i].normalPriority, Is.EqualTo(3));
        }
    }

    [Test]
    public void DroneArrivalCreatesUserDemolitionDestroyRequest()
    {
        Entity target = _entityManager.CreateEntity(
            typeof(BuildingOccupant),
            typeof(GridPosition));
        _entityManager.SetComponentData(target,
            new GridPosition { gridPosition = new int2(6, 7) });
        Entity drone = _entityManager.CreateEntity(
            typeof(DroneState),
            typeof(DroneAssignment),
            typeof(GridPosition));
        Entity task = CreateTask(DroneTaskTypeEnum.Demolition);
        _entityManager.AddComponentData(task,
            new DroneDemolitionTaskData { targetBuilding = target });
        _entityManager.AddComponentData(task, new DroneDirectTaskAssignment
        {
            droneEntity = drone
        });
        _entityManager.SetComponentData(drone,
            new DroneState { value = DroneStateEnum.Demolishing });
        _entityManager.SetComponentData(drone, new DroneAssignment
        {
            taskEntity = task
        });
        DroneDirectTaskSystem directTaskSystem = _world
            .GetOrCreateSystemManaged<DroneDirectTaskSystem>();

        directTaskSystem.Update();

        Entity destroyRequestEntity = GetSingleton<BuildingDestroyRequest>();
        BuildingDestroyRequest destroyRequest = _entityManager
            .GetComponentData<BuildingDestroyRequest>(destroyRequestEntity);
        Assert.That(
            destroyRequest.cause,
            Is.EqualTo(BuildingDestroyCauseEnum.UserDemolition));
        Assert.That(destroyRequest.gridPosition, Is.EqualTo(new int2(6, 7)));
        Assert.That(_entityManager.Exists(target), Is.True);
        Assert.That(
            _entityManager.GetComponentData<DroneTaskStatus>(task).state,
            Is.EqualTo(DroneTaskStateEnum.Completed));
    }

    [Test]
    public void RecoveryOutsideEveryNetworkSuspendsAndResumesWhenCovered()
    {
        Entity item = CreateWorldItem(new int2(2, 2), ItemTypeEnum.Iron);
        Entity task = CreateRecoveryTask(item, 5);
        DroneDirectTaskPlanningSystem planningSystem = _world
            .GetOrCreateSystemManaged<DroneDirectTaskPlanningSystem>();

        planningSystem.Update();

        Assert.That(_entityManager.Exists(item), Is.True);
        Assert.That(_entityManager.Exists(task), Is.True);
        Assert.That(
            _entityManager.GetComponentData<DroneTaskStatus>(task).state,
            Is.EqualTo(DroneTaskStateEnum.Suspended));
        Assert.That(
            _entityManager.GetComponentData<DroneTaskAutomaticSuspension>(task)
                .reason,
            Is.EqualTo(DroneTaskAutomaticSuspensionReasonEnum
                .OutsideDroneNetwork));

        CreateStation(int2.zero, 10);
        _networkSystem.Update();
        Assert.That(
            _networkSystem.TryGetNetworkIdAtCell(new int2(2, 2), out _),
            Is.True);
        planningSystem.Update();

        Assert.That(
            _entityManager.GetComponentData<DroneTaskStatus>(task).state,
            Is.EqualTo(DroneTaskStateEnum.Pending));
        Assert.That(
            _entityManager.HasComponent<DroneTaskAutomaticSuspension>(task),
            Is.False);
    }

    [Test]
    public void RecoveryWithoutStorageCapacityAutomaticallySuspendsAndResumes()
    {
        Entity station = CreateStation(int2.zero, 0);
        _networkSystem.Update();
        Entity item = CreateWorldItem(new int2(2, 2), ItemTypeEnum.Iron);
        Entity task = CreateRecoveryTask(item, 5);
        DroneDirectTaskPlanningSystem planningSystem = _world
            .GetOrCreateSystemManaged<DroneDirectTaskPlanningSystem>();

        planningSystem.Update();

        Assert.That(
            _entityManager.GetComponentData<DroneTaskStatus>(task).state,
            Is.EqualTo(DroneTaskStateEnum.Suspended));
        Assert.That(
            _entityManager.GetComponentData<DroneTaskAutomaticSuspension>(task)
                .reason,
            Is.EqualTo(DroneTaskAutomaticSuspensionReasonEnum
                .DestinationCapacityUnavailable));

        _entityManager.SetComponentData(station, new Storage { capacity = 1 });
        planningSystem.Update();

        Assert.That(
            _entityManager.GetComponentData<DroneTaskStatus>(task).state,
            Is.EqualTo(DroneTaskStateEnum.Pending));
        Assert.That(
            _entityManager.HasComponent<DroneTaskAutomaticSuspension>(task),
            Is.False);
        Assert.That(
            _entityManager.HasComponent<DroneDirectTaskPlan>(task),
            Is.True);

        DynamicBuffer<DroneReservedStorageCapacityElement> reservedCapacity =
            _entityManager.AddBuffer<DroneReservedStorageCapacityElement>(
                station);
        reservedCapacity.Add(new DroneReservedStorageCapacityElement
        {
            itemType = ItemTypeEnum.Iron,
            quantity = 3
        });
        planningSystem.Update();

        Assert.That(
            _entityManager.GetComponentData<DroneTaskStatus>(task).state,
            Is.EqualTo(DroneTaskStateEnum.Suspended));
        Assert.That(
            _entityManager.HasComponent<DroneDirectTaskPlan>(task),
            Is.False);
    }

    [Test]
    public void UserDemolitionReturnsEveryConfiguredConstructionMaterial()
    {
        CreateConstructionConfig(2);
        _world.GetOrCreateSystemManaged<PowerGridSystem>();
        BuildingDestroySystem destroySystem = _world
            .GetOrCreateSystemManaged<BuildingDestroySystem>();
        Entity building = CreateRegisteredBuilding(
            new int2(8, 9),
            BuildingTypeEnum.Storage);
        Entity request = _entityManager.CreateEntity(
            typeof(BuildingDestroyRequest));
        _entityManager.SetComponentData(request, new BuildingDestroyRequest
        {
            gridPosition = new int2(8, 9),
            cause = BuildingDestroyCauseEnum.UserDemolition
        });

        destroySystem.Update();

        Assert.That(_chunkMap.TryGetBuilding(new int2(8, 9), out _), Is.False);
        Assert.That(Count<WorldItemSpawnRequest>(), Is.EqualTo(2));
        using EntityQuery query = _entityManager.CreateEntityQuery(
            typeof(WorldItemSpawnRequest));
        using var requests = query.ToComponentDataArray<WorldItemSpawnRequest>(
            Unity.Collections.Allocator.Temp);

        for (int i = 0; i < requests.Length; i++)
        {
            Assert.That(requests[i].itemType, Is.EqualTo(ItemTypeEnum.Iron));
            Assert.That(requests[i].createRecoveryTask, Is.True);
        }

        Assert.That(_entityManager.Exists(building), Is.True,
            "Building destruction remains deferred to the existing ECB path.");
    }

    [Test]
    public void ExternalDestructionReturnsMaterialsWithoutAutomaticRecovery()
    {
        CreateConstructionConfig(1);
        _world.GetOrCreateSystemManaged<PowerGridSystem>();
        BuildingDestroySystem destroySystem = _world
            .GetOrCreateSystemManaged<BuildingDestroySystem>();
        CreateRegisteredBuilding(
            new int2(10, 11),
            BuildingTypeEnum.Storage);
        Entity request = _entityManager.CreateEntity(
            typeof(BuildingDestroyRequest));
        _entityManager.SetComponentData(request, new BuildingDestroyRequest
        {
            gridPosition = new int2(10, 11),
            cause = BuildingDestroyCauseEnum.External
        });

        destroySystem.Update();

        Entity spawnRequest = GetSingleton<WorldItemSpawnRequest>();
        Assert.That(
            _entityManager.GetComponentData<WorldItemSpawnRequest>(spawnRequest)
                .createRecoveryTask,
            Is.False);
        Assert.That(
            Count<DroneWorldItemRecoveryCreateRequest>(),
            Is.EqualTo(0));
    }

    private void CreateConstructionConfig(int quantity)
    {
        Entity constructionConfig = _entityManager.CreateEntity(
            typeof(ConstructionConfig),
            typeof(ConstructionMaterialConfigElement));
        _entityManager.GetBuffer<ConstructionMaterialConfigElement>(
                constructionConfig)
            .Add(new ConstructionMaterialConfigElement
            {
                buildingType = BuildingTypeEnum.Storage,
                itemType = ItemTypeEnum.Iron,
                quantity = quantity
            });
    }

    private void CreateDroneAndStorageConfig()
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
        _entityManager.GetBuffer<ItemStorageLimitElement>(config).Add(
            new ItemStorageLimitElement
            {
                itemType = ItemTypeEnum.Iron,
                maxAmount = 3
            });
    }

    private Entity CreateRegisteredBuilding(int2 cell, BuildingTypeEnum type)
    {
        Entity building = _entityManager.CreateEntity(
            typeof(BuildingType),
            typeof(GridPosition),
            typeof(Direction),
            typeof(BuildingOccupantRequest),
            typeof(StoredItemElement));
        _entityManager.SetComponentData(building, new BuildingType { type = type });
        _entityManager.SetComponentData(building,
            new GridPosition { gridPosition = cell });
        _entityManager.SetComponentData(building,
            new Direction { dir = DirectionEnum.Up });
        Assert.That(_chunkMap.TryReserveBuilding(cell), Is.True);
        EndSimulationEntityCommandBufferSystem endSimulation = _world
            .GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>();
        _chunkMap.Update();
        endSimulation.Update();
        Assert.That(_entityManager.HasComponent<BuildingOccupant>(building), Is.True);
        return building;
    }

    private Entity CreateStation(int2 cell, int capacity)
    {
        Entity station = _entityManager.CreateEntity(
            typeof(Storage),
            typeof(StoredItemElement),
            typeof(GridPosition),
            typeof(BuildingType),
            typeof(DroneStation),
            typeof(BuildingOccupant));
        _entityManager.SetComponentData(station, new Storage { capacity = capacity });
        _entityManager.SetComponentData(station,
            new GridPosition { gridPosition = cell });
        _entityManager.SetComponentData(station,
            new BuildingType { type = BuildingTypeEnum.DroneStation });
        _entityManager.SetComponentData(station, new DroneStation
        {
            activityRangeInChunks = new int2(1, 1)
        });
        return station;
    }

    private Entity CreateWorldItem(int2 cell, ItemTypeEnum type)
    {
        Entity item = _entityManager.CreateEntity(
            typeof(Item),
            typeof(LocalTransform),
            typeof(LocalToWorld),
            typeof(GridPosition),
            typeof(ItemCellChanged));
        _entityManager.SetComponentData(item, new Item { type = type });
        _entityManager.SetComponentData(item,
            LocalTransform.FromPosition(new float3(cell.x, cell.y, 0f)));
        _entityManager.SetComponentData(item,
            new GridPosition { gridPosition = cell });
        Assert.That(_chunkMap.TryRegisterItem(cell, item), Is.True);
        return item;
    }

    private Entity CreateRecoveryTask(Entity itemEntity, int priority)
    {
        Entity task = CreateTask(DroneTaskTypeEnum.RecoverWorldItem);
        _entityManager.AddComponentData(task,
            new DroneWorldItemRecoveryTaskData { itemEntity = itemEntity });
        DroneTaskPriority taskPriority = _entityManager
            .GetComponentData<DroneTaskPriority>(task);
        taskPriority.normalPriority = priority;
        _entityManager.SetComponentData(task, taskPriority);
        return task;
    }

    private Entity CreateTask(DroneTaskTypeEnum type)
    {
        Entity task = _entityManager.CreateEntity(
            typeof(DroneTask),
            typeof(DroneTaskStatus),
            typeof(DroneTaskPriority),
            typeof(DroneTaskCreationOrder),
            typeof(DroneTaskQuantity));
        _entityManager.SetComponentData(task, new DroneTask { type = type });
        _entityManager.SetComponentData(task,
            new DroneTaskStatus { state = DroneTaskStateEnum.Pending });
        _entityManager.SetComponentData(task, new DroneTaskPriority
        {
            priorityClass = DroneTaskPriorityClassEnum.Normal,
            normalPriority = 5
        });
        _entityManager.SetComponentData(task,
            new DroneTaskCreationOrder { value = 1 });
        _entityManager.SetComponentData(task,
            new DroneTaskQuantity { totalQuantity = 1 });
        return task;
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
