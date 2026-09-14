using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

public class DroneBuildingItemTaskTests : EcsWorldTestFixture
{
    private DroneStationNetworkSystem _networkSystem;
    private DroneBuildingItemTaskPlanningSystem _planningSystem;
    private DroneTaskReservationSystem _reservationSystem;
    private Entity _station;

    [SetUp]
    public void SetUp()
    {
        _world.GetOrCreateSystemManaged<ChunkMapSystem>();
        _networkSystem = _world.GetOrCreateSystemManaged<
            DroneStationNetworkSystem>();
        _planningSystem = _world.GetOrCreateSystemManaged<
            DroneBuildingItemTaskPlanningSystem>();
        _reservationSystem = _world.GetOrCreateSystemManaged<
            DroneTaskReservationSystem>();
        CreateConfig();
        _station = CreateStation(int2.zero);
        _networkSystem.Update();
    }

    [Test]
    public void LargeInsertionCreatesDistinctCarrySizedReservations()
    {
        Entity source = CreateStorage(
            new int2(4, 0),
            10,
            BuildingTypeEnum.Storage);
        Entity destination = CreateStorage(
            new int2(8, 0),
            10,
            BuildingTypeEnum.Storage);

        for (int i = 0; i < 5; i++)
            CreateStoredItem(source, ItemTypeEnum.Iron_Ore);

        Entity task = CreateBuildingItemTask(
            DroneTaskTypeEnum.InsertBuildingItem,
            destination,
            ItemTypeEnum.Iron_Ore,
            5);

        PlanAndReserve();
        PlanAndReserve();

        Assert.That(CountReservations(), Is.EqualTo(2));
        Assert.That(CountReservedItems(), Is.EqualTo(5));
        DroneTaskQuantity quantity = _entityManager
            .GetComponentData<DroneTaskQuantity>(task);
        Assert.That(quantity.reservedQuantity, Is.EqualTo(5));
        Assert.That(GetReservationQuantity(3), Is.EqualTo(3));
        Assert.That(GetReservationQuantity(2), Is.EqualTo(2));
    }

    [Test]
    public void RemovalChoosesNearestOrdinaryStorage()
    {
        Entity source = CreateCrafter(new int2(8, 0), ItemTypeEnum.Iron);
        CreateStoredItem(source, ItemTypeEnum.Iron_Ore);
        Entity nearestStorage = CreateStorage(
            new int2(9, 0),
            10,
            BuildingTypeEnum.Storage);
        CreateStorage(
            new int2(-8, 0),
            10,
            BuildingTypeEnum.DroneStation);
        CreateBuildingItemTask(
            DroneTaskTypeEnum.RemoveBuildingItem,
            source,
            ItemTypeEnum.Iron_Ore,
            1);

        PlanAndReserve();

        Entity reservationEntity = GetSingleReservation();
        DroneTaskReservation reservation = _entityManager
            .GetComponentData<DroneTaskReservation>(reservationEntity);
        Assert.That(reservation.destinationOwner, Is.EqualTo(nearestStorage));
    }

    [Test]
    public void RemovalWaitsUntilEntireRemainingQuantityFitsInStorage()
    {
        _entityManager.SetComponentData(_station, new Storage { capacity = 0 });
        Entity source = CreateCrafter(new int2(8, 0), ItemTypeEnum.Iron);

        for (int i = 0; i < 4; i++)
            CreateStoredItem(source, ItemTypeEnum.Iron_Ore);

        CreateStorage(new int2(9, 0), 1, BuildingTypeEnum.Storage);
        Entity task = CreateBuildingItemTask(
            DroneTaskTypeEnum.RemoveBuildingItem,
            source,
            ItemTypeEnum.Iron_Ore,
            4);

        PlanAndReserve();

        Assert.That(CountReservations(), Is.EqualTo(0));
        Assert.That(
            _entityManager.GetComponentData<DroneTaskStatus>(task).state,
            Is.EqualTo(DroneTaskStateEnum.Suspended));
        Assert.That(
            _entityManager.GetComponentData<DroneTaskAutomaticSuspension>(task)
                .reason,
            Is.EqualTo(DroneTaskAutomaticSuspensionReasonEnum
                .DestinationCapacityUnavailable));

        CreateStorage(new int2(10, 0), 1, BuildingTypeEnum.Storage);
        PlanAndReserve();

        Assert.That(CountReservations(), Is.EqualTo(1));
        Assert.That(
            _entityManager.GetComponentData<DroneTaskStatus>(task).state,
            Is.EqualTo(DroneTaskStateEnum.Pending));
        Assert.That(
            _entityManager.HasComponent<DroneTaskAutomaticSuspension>(task),
            Is.False);
    }

    [Test]
    public void CrafterCapacityShortageAutomaticallySuspendsInsertion()
    {
        Entity source = CreateStorage(
            new int2(4, 0),
            10,
            BuildingTypeEnum.Storage);
        CreateStoredItem(source, ItemTypeEnum.Iron_Ore);
        Entity crafter = CreateCrafter(new int2(8, 0), ItemTypeEnum.Iron);
        CreateStoredItem(crafter, ItemTypeEnum.Iron_Ore);
        CreateStoredItem(crafter, ItemTypeEnum.Iron_Ore);
        CreateStoredItem(crafter, ItemTypeEnum.Iron_Ore);
        Entity task = CreateBuildingItemTask(
            DroneTaskTypeEnum.InsertBuildingItem,
            crafter,
            ItemTypeEnum.Iron_Ore,
            1);

        PlanAndReserve();

        Assert.That(CountReservations(), Is.EqualTo(0));
        Assert.That(
            _entityManager.GetComponentData<DroneTaskStatus>(task).state,
            Is.EqualTo(DroneTaskStateEnum.Suspended));
        Assert.That(
            _entityManager.GetComponentData<DroneTaskAutomaticSuspension>(task)
                .reason,
            Is.EqualTo(DroneTaskAutomaticSuspensionReasonEnum
                .DestinationCapacityUnavailable));
    }

    [Test]
    public void MissingInsertionItemsAutomaticallySuspendAndResume()
    {
        Entity source = CreateStorage(
            new int2(4, 0),
            10,
            BuildingTypeEnum.Storage);
        Entity destination = CreateStorage(
            new int2(8, 0),
            10,
            BuildingTypeEnum.Storage);
        Entity task = CreateBuildingItemTask(
            DroneTaskTypeEnum.InsertBuildingItem,
            destination,
            ItemTypeEnum.Iron_Ore,
            1);

        PlanAndReserve();

        Assert.That(CountReservations(), Is.EqualTo(0));
        Assert.That(
            _entityManager.GetComponentData<DroneTaskStatus>(task).state,
            Is.EqualTo(DroneTaskStateEnum.Suspended));
        Assert.That(
            _entityManager.GetComponentData<DroneTaskAutomaticSuspension>(task)
                .reason,
            Is.EqualTo(DroneTaskAutomaticSuspensionReasonEnum
                .MissingSourceItems));

        CreateStoredItem(source, ItemTypeEnum.Iron_Ore);
        PlanAndReserve();

        Assert.That(CountReservations(), Is.EqualTo(1));
        Assert.That(
            _entityManager.GetComponentData<DroneTaskStatus>(task).state,
            Is.EqualTo(DroneTaskStateEnum.Pending));
        Assert.That(
            _entityManager.HasComponent<DroneTaskAutomaticSuspension>(task),
            Is.False);
    }

    [Test]
    public void ManualSuspensionDoesNotResumeWhenItemsAreAvailable()
    {
        Entity source = CreateStorage(
            new int2(4, 0),
            10,
            BuildingTypeEnum.Storage);
        CreateStoredItem(source, ItemTypeEnum.Iron_Ore);
        Entity destination = CreateStorage(
            new int2(8, 0),
            10,
            BuildingTypeEnum.Storage);
        Entity task = CreateBuildingItemTask(
            DroneTaskTypeEnum.InsertBuildingItem,
            destination,
            ItemTypeEnum.Iron_Ore,
            1);
        _entityManager.SetComponentData(
            task,
            new DroneTaskStatus
            {
                state = DroneTaskStateEnum.Suspended,
                stateBeforeSuspension = DroneTaskStateEnum.Pending
            });

        PlanAndReserve();

        Assert.That(CountReservations(), Is.EqualTo(0));
        Assert.That(
            _entityManager.GetComponentData<DroneTaskStatus>(task).state,
            Is.EqualTo(DroneTaskStateEnum.Suspended));
    }

    [Test]
    public void RecipeChangeRequestsOnlyIncompatibleStoredItemRemoval()
    {
        EndSimulationEntityCommandBufferSystem endSimulationSystem = _world
            .GetOrCreateSystemManaged<
                EndSimulationEntityCommandBufferSystem>();
        CrafterRecipeChangeSystem recipeChangeSystem = _world
            .GetOrCreateSystemManaged<CrafterRecipeChangeSystem>();
        Entity crafter = CreateCrafter(new int2(8, 0), ItemTypeEnum.Iron);
        CreateStoredItem(crafter, ItemTypeEnum.Iron_Ore);
        CreateStoredItem(crafter, ItemTypeEnum.Copper_Ore);
        DynamicBuffer<ProducedItemElement> producedItems = _entityManager
            .AddBuffer<ProducedItemElement>(crafter);
        producedItems.Add(new ProducedItemElement
        {
            itemEntity = _entityManager.CreateEntity(),
            type = ItemTypeEnum.Iron
        });
        Entity request = _entityManager.CreateEntity(
            typeof(CrafterRecipeChangeRequest));
        _entityManager.SetComponentData(
            request,
            new CrafterRecipeChangeRequest
            {
                crafterEntity = crafter,
                selectedItemType = ItemTypeEnum.Copper
            });

        recipeChangeSystem.Update();
        endSimulationSystem.Update();

        using EntityQuery removalRequestQuery = _entityManager
            .CreateEntityQuery(
                typeof(DroneTaskCreateRequest),
                typeof(DroneBuildingItemTaskData));
        Assert.That(
            removalRequestQuery.CalculateEntityCount(),
            Is.EqualTo(1));
        Entity removalRequest = removalRequestQuery.GetSingletonEntity();
        DroneTaskCreateRequest taskRequest = _entityManager
            .GetComponentData<DroneTaskCreateRequest>(removalRequest);
        DroneBuildingItemTaskData taskData = _entityManager
            .GetComponentData<DroneBuildingItemTaskData>(removalRequest);
        Assert.That(
            taskRequest.type,
            Is.EqualTo(DroneTaskTypeEnum.RemoveBuildingItem));
        Assert.That(taskRequest.totalQuantity, Is.EqualTo(1));
        Assert.That(taskData.itemType, Is.EqualTo(ItemTypeEnum.Iron_Ore));
        Assert.That(
            _entityManager.GetBuffer<ProducedItemElement>(crafter).Length,
            Is.EqualTo(1));
    }

    private void PlanAndReserve()
    {
        _planningSystem.Update();
        _reservationSystem.Update();
    }

    private void CreateConfig()
    {
        Entity config = _entityManager.CreateEntity(
            typeof(DroneConfig),
            typeof(CrafterConfig),
            typeof(ItemStorageLimitElement),
            typeof(CrafterRecipeElement),
            typeof(CrafterRecipeIngredientElement));
        _entityManager.SetComponentData(
            config,
            new DroneConfig
            {
                defaultTaskPriority = 5,
                stationStorageCapacity = 10,
                stationActivityRangeInChunks = new int2(1, 1),
                carryingCapacity = 3,
                movementSpeed = 4f,
                emergencyMovementSpeed = 0.5f,
                maximumBattery = 100f,
                batteryConsumptionPerDistance = 1f
            });
        DynamicBuffer<ItemStorageLimitElement> storageLimits =
            _entityManager.GetBuffer<ItemStorageLimitElement>(config);
        storageLimits.Add(new ItemStorageLimitElement
        {
            itemType = ItemTypeEnum.Iron_Ore,
            maxAmount = 3
        });
        storageLimits.Add(new ItemStorageLimitElement
        {
            itemType = ItemTypeEnum.Copper_Ore,
            maxAmount = 3
        });
        DynamicBuffer<CrafterRecipeElement> recipes = _entityManager
            .GetBuffer<CrafterRecipeElement>(config);
        recipes.Add(new CrafterRecipeElement
        {
            id = 1,
            outputItemType = ItemTypeEnum.Iron,
            craftTime = 1f
        });
        recipes.Add(new CrafterRecipeElement
        {
            id = 2,
            outputItemType = ItemTypeEnum.Copper,
            craftTime = 1f
        });
        DynamicBuffer<CrafterRecipeIngredientElement> ingredients =
            _entityManager.GetBuffer<CrafterRecipeIngredientElement>(config);
        ingredients.Add(new CrafterRecipeIngredientElement
        {
            recipeId = 1,
            itemType = ItemTypeEnum.Iron_Ore,
            amount = 1
        });
        ingredients.Add(new CrafterRecipeIngredientElement
        {
            recipeId = 2,
            itemType = ItemTypeEnum.Copper_Ore,
            amount = 1
        });

        Entity researchConfig = _entityManager.CreateEntity(
            typeof(ResearchConfig),
            typeof(RecipeUnlockElement));
        DynamicBuffer<RecipeUnlockElement> recipeUnlocks = _entityManager
            .GetBuffer<RecipeUnlockElement>(researchConfig);
        recipeUnlocks.Add(new RecipeUnlockElement { recipeId = 1 });
        recipeUnlocks.Add(new RecipeUnlockElement { recipeId = 2 });
    }

    private Entity CreateStation(int2 position)
    {
        Entity station = CreateStorage(
            position,
            10,
            BuildingTypeEnum.MainFacility);
        _entityManager.AddComponentData(
            station,
            new DroneStation
            {
                activityRangeInChunks = new int2(1, 1),
                isMainStation = true
            });
        _entityManager.AddComponent<BuildingOccupant>(station);
        return station;
    }

    private Entity CreateStorage(
        int2 position,
        int capacity,
        BuildingTypeEnum buildingType)
    {
        Entity storage = _entityManager.CreateEntity(
            typeof(Storage),
            typeof(StoredItemElement),
            typeof(GridPosition),
            typeof(BuildingType));
        _entityManager.SetComponentData(
            storage,
            new Storage { capacity = capacity });
        _entityManager.SetComponentData(
            storage,
            new GridPosition { gridPosition = position });
        _entityManager.SetComponentData(
            storage,
            new BuildingType { type = buildingType });
        return storage;
    }

    private Entity CreateCrafter(
        int2 position,
        ItemTypeEnum selectedRecipe)
    {
        Entity crafter = _entityManager.CreateEntity(
            typeof(Crafter),
            typeof(StoredItemElement),
            typeof(GridPosition),
            typeof(BuildingType));
        _entityManager.SetComponentData(
            crafter,
            new Crafter
            {
                selectedItemType = selectedRecipe,
                state = CrafterStateEnum.Idle,
                speed = 1f
            });
        _entityManager.SetComponentData(
            crafter,
            new GridPosition { gridPosition = position });
        _entityManager.SetComponentData(
            crafter,
            new BuildingType { type = BuildingTypeEnum.Crafter });
        return crafter;
    }

    private Entity CreateStoredItem(Entity owner, ItemTypeEnum itemType)
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
        return item;
    }

    private Entity CreateBuildingItemTask(
        DroneTaskTypeEnum taskType,
        Entity targetBuilding,
        ItemTypeEnum itemType,
        int quantity)
    {
        Entity task = _entityManager.CreateEntity(
            typeof(DroneTask),
            typeof(DroneTaskStatus),
            typeof(DroneTaskQuantity),
            typeof(DroneBuildingItemTaskData));
        _entityManager.SetComponentData(task, new DroneTask { type = taskType });
        _entityManager.SetComponentData(
            task,
            new DroneTaskStatus { state = DroneTaskStateEnum.Pending });
        _entityManager.SetComponentData(
            task,
            new DroneTaskQuantity { totalQuantity = quantity });
        _entityManager.SetComponentData(
            task,
            new DroneBuildingItemTaskData
            {
                targetBuilding = targetBuilding,
                itemType = itemType
            });
        return task;
    }

    private int CountReservations()
    {
        using EntityQuery query = _entityManager.CreateEntityQuery(
            typeof(DroneTaskReservation));
        return query.CalculateEntityCount();
    }

    private int CountReservedItems()
    {
        using EntityQuery query = _entityManager.CreateEntityQuery(
            new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<DroneItemReservation>()
                },
                Options = EntityQueryOptions.IncludeDisabledEntities
            });
        return query.CalculateEntityCount();
    }

    private Entity GetSingleReservation()
    {
        using EntityQuery query = _entityManager.CreateEntityQuery(
            typeof(DroneTaskReservation));
        return query.GetSingletonEntity();
    }

    private int GetReservationQuantity(int expectedQuantity)
    {
        using EntityQuery query = _entityManager.CreateEntityQuery(
            typeof(DroneTaskReservation));
        using NativeArray<Entity> reservations =
            query.ToEntityArray(Allocator.Temp);

        for (int i = 0; i < reservations.Length; i++)
        {
            int quantity = _entityManager
                .GetComponentData<DroneTaskReservation>(reservations[i])
                .quantity;

            if (quantity == expectedQuantity)
                return quantity;
        }

        return 0;
    }
}
