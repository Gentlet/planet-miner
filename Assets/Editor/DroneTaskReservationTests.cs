using NUnit.Framework;
using Unity.Entities;

public class DroneTaskReservationTests
{
    private World _world;
    private EntityManager _entityManager;
    private DroneTaskReservationSystem _reservationSystem;
    private ItemStorageSystem _itemStorageSystem;

    [SetUp]
    public void SetUp()
    {
        _world = new World(nameof(DroneTaskReservationTests));
        _entityManager = _world.EntityManager;
        _reservationSystem = _world
            .GetOrCreateSystemManaged<DroneTaskReservationSystem>();
        _itemStorageSystem = _world
            .GetOrCreateSystemManaged<ItemStorageSystem>();

        Entity storageLimitEntity = _entityManager.CreateEntity(
            typeof(ItemStorageLimitElement));
        _entityManager.GetBuffer<ItemStorageLimitElement>(storageLimitEntity)
            .Add(new ItemStorageLimitElement
            {
                itemType = ItemTypeEnum.Iron_Ore,
                maxAmount = 10
            });
    }

    [TearDown]
    public void TearDown()
    {
        _world.Dispose();
    }

    [Test]
    public void CompetingRequestsCannotReserveTheSameItems()
    {
        Entity source = CreateStorage(2);
        Entity destination = CreateStorage(2);
        CreateStoredItem(source);
        CreateStoredItem(source);
        Entity firstTask = CreateTask(2);
        Entity secondTask = CreateTask(1);
        CreateReservationRequest(firstTask, source, destination, 2);
        CreateReservationRequest(secondTask, source, destination, 1);

        _reservationSystem.Update();

        Assert.That(CountReservations(), Is.EqualTo(1));
        Assert.That(CountReservedItems(), Is.EqualTo(2));
        Assert.That(GetReservedQuantity(firstTask), Is.EqualTo(2));
        Assert.That(GetReservedQuantity(secondTask), Is.EqualTo(0));
    }

    [Test]
    public void CompetingRequestsCannotOverbookDestinationCapacity()
    {
        SetIronStackLimit(1);
        Entity source = CreateStorage(2);
        Entity destination = CreateStorage(1);
        CreateStoredItem(source);
        CreateStoredItem(source);
        Entity firstTask = CreateTask(1);
        Entity secondTask = CreateTask(1);
        CreateReservationRequest(firstTask, source, destination, 1);
        CreateReservationRequest(secondTask, source, destination, 1);

        _reservationSystem.Update();

        Assert.That(CountReservations(), Is.EqualTo(1));
        Assert.That(CountReservedItems(), Is.EqualTo(1));
        Assert.That(GetReservedQuantity(firstTask), Is.EqualTo(1));
        Assert.That(GetReservedQuantity(secondTask), Is.EqualTo(0));
    }

    [Test]
    public void CancelledTaskReleasesItemsCapacityAndTaskQuantity()
    {
        Entity source = CreateStorage(2);
        Entity destination = CreateStorage(2);
        Entity firstItem = CreateStoredItem(source);
        Entity secondItem = CreateStoredItem(source);
        Entity task = CreateTask(2);
        CreateReservationRequest(task, source, destination, 2);
        _reservationSystem.Update();

        _entityManager.SetComponentData(
            task,
            new DroneTaskStatus
            {
                state = DroneTaskStateEnum.Cancelled,
                stateBeforeSuspension = DroneTaskStateEnum.Pending
            });
        _reservationSystem.Update();

        Assert.That(CountReservations(), Is.EqualTo(0));
        Assert.That(
            _entityManager.HasComponent<DroneItemReservation>(firstItem),
            Is.False);
        Assert.That(
            _entityManager.HasComponent<DroneItemReservation>(secondItem),
            Is.False);
        Assert.That(
            _entityManager
                .GetBuffer<DroneReservedStorageCapacityElement>(destination)
                .Length,
            Is.EqualTo(0));
        Assert.That(GetReservedQuantity(task), Is.EqualTo(0));
    }

    [Test]
    public void ProducedItemCanBeReservedWithoutChangingItsOwner()
    {
        Entity source = _entityManager.CreateEntity(
            typeof(ProducedItemElement));
        Entity destination = CreateStorage(1);
        Entity item = CreateProducedItem(source);
        Entity task = CreateTask(1);
        CreateReservationRequest(task, source, destination, 1);

        _reservationSystem.Update();

        Assert.That(CountReservations(), Is.EqualTo(1));
        Assert.That(
            _entityManager.HasComponent<DroneItemReservation>(item),
            Is.True);
        Assert.That(
            _entityManager.GetComponentData<StoredItem>(item).owner,
            Is.EqualTo(source));

        EntityQuery reservationQuery = _entityManager.CreateEntityQuery(
            typeof(DroneTaskReservation));
        Entity reservation = reservationQuery.GetSingletonEntity();
        DroneTaskReservedItemElement reservedItem = _entityManager
            .GetBuffer<DroneTaskReservedItemElement>(reservation, true)[0];
        Assert.That(
            reservedItem.sourceKind,
            Is.EqualTo(DroneTaskItemSourceKind.Produced));
        reservationQuery.Dispose();
    }

    [Test]
    public void ProducedItemTransfersBetweenOwnersThroughStorageApi()
    {
        Entity source = _entityManager.CreateEntity(
            typeof(ProducedItemElement));
        Entity destination = CreateStorage(1);
        Entity item = CreateProducedItem(source);

        bool transferred = _itemStorageSystem
            .TryTransferOwnedItemImmediate<ProducedItemElement>(
                source,
                0,
                destination);

        Assert.That(transferred, Is.True);
        Assert.That(
            _entityManager.GetBuffer<ProducedItemElement>(source).Length,
            Is.EqualTo(0));
        Assert.That(
            _entityManager.GetBuffer<StoredItemElement>(destination).Length,
            Is.EqualTo(1));
        Assert.That(
            _entityManager.GetComponentData<StoredItem>(item).owner,
            Is.EqualTo(destination));
    }

    [Test]
    public void ReservedItemCannotTransferWithoutReservationHandoff()
    {
        Entity source = _entityManager.CreateEntity(
            typeof(ProducedItemElement));
        Entity destination = CreateStorage(1);
        Entity item = CreateProducedItem(source);
        _entityManager.AddComponentData(
            item,
            new DroneItemReservation
            {
                reservationEntity = Entity.Null,
                taskEntity = Entity.Null
            });

        bool transferred = _itemStorageSystem
            .TryTransferOwnedItemImmediate<ProducedItemElement>(
                source,
                0,
                destination);

        Assert.That(transferred, Is.False);
        Assert.That(
            _entityManager.GetBuffer<ProducedItemElement>(source).Length,
            Is.EqualTo(1));
        Assert.That(
            _entityManager.GetBuffer<StoredItemElement>(destination).Length,
            Is.EqualTo(0));
        Assert.That(
            _entityManager.GetComponentData<StoredItem>(item).owner,
            Is.EqualTo(source));
    }

    private Entity CreateStorage(int capacity)
    {
        Entity storage = _entityManager.CreateEntity(
            typeof(Storage),
            typeof(StoredItemElement));
        _entityManager.SetComponentData(
            storage,
            new Storage { capacity = capacity });
        return storage;
    }

    private Entity CreateStoredItem(Entity owner)
    {
        Entity item = _entityManager.CreateEntity(
            typeof(Item),
            typeof(StoredItem),
            typeof(Disabled));
        _entityManager.SetComponentData(
            item,
            new Item { type = ItemTypeEnum.Iron_Ore });
        _entityManager.SetComponentData(item, new StoredItem { owner = owner });
        _entityManager.GetBuffer<StoredItemElement>(owner).Add(
            new StoredItemElement
            {
                itemEntity = item,
                type = ItemTypeEnum.Iron_Ore
            });
        return item;
    }

    private Entity CreateProducedItem(Entity owner)
    {
        Entity item = _entityManager.CreateEntity(
            typeof(Item),
            typeof(StoredItem),
            typeof(Disabled));
        _entityManager.SetComponentData(
            item,
            new Item { type = ItemTypeEnum.Iron_Ore });
        _entityManager.SetComponentData(item, new StoredItem { owner = owner });
        _entityManager.GetBuffer<ProducedItemElement>(owner).Add(
            new ProducedItemElement
            {
                itemEntity = item,
                type = ItemTypeEnum.Iron_Ore
            });
        return item;
    }

    private Entity CreateTask(int totalQuantity)
    {
        Entity task = _entityManager.CreateEntity(
            typeof(DroneTask),
            typeof(DroneTaskStatus),
            typeof(DroneTaskQuantity));
        _entityManager.SetComponentData(
            task,
            new DroneTask
            {
                type = DroneTaskTypeEnum.InsertBuildingItem
            });
        _entityManager.SetComponentData(
            task,
            new DroneTaskStatus
            {
                state = DroneTaskStateEnum.Pending,
                stateBeforeSuspension = DroneTaskStateEnum.Pending
            });
        _entityManager.SetComponentData(
            task,
            new DroneTaskQuantity { totalQuantity = totalQuantity });
        return task;
    }

    private void CreateReservationRequest(
        Entity task,
        Entity source,
        Entity destination,
        int quantity)
    {
        Entity request = _entityManager.CreateEntity(
            typeof(DroneTaskReservationRequest));
        _entityManager.SetComponentData(
            request,
            new DroneTaskReservationRequest
            {
                taskEntity = task,
                sourceOwner = source,
                destinationOwner = destination,
                itemType = ItemTypeEnum.Iron_Ore,
                quantity = quantity
            });
    }

    private void SetIronStackLimit(int maxAmount)
    {
        EntityQuery query = _entityManager.CreateEntityQuery(
            typeof(ItemStorageLimitElement));
        Entity configEntity = query.GetSingletonEntity();
        DynamicBuffer<ItemStorageLimitElement> storageLimits =
            _entityManager.GetBuffer<ItemStorageLimitElement>(configEntity);
        storageLimits[0] = new ItemStorageLimitElement
        {
            itemType = ItemTypeEnum.Iron_Ore,
            maxAmount = maxAmount
        };
        query.Dispose();
    }

    private int CountReservations()
    {
        EntityQuery query = _entityManager.CreateEntityQuery(
            typeof(DroneTaskReservation));
        int count = query.CalculateEntityCount();
        query.Dispose();
        return count;
    }

    private int CountReservedItems()
    {
        EntityQuery query = _entityManager.CreateEntityQuery(
            new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<DroneItemReservation>()
                },
                Options = EntityQueryOptions.IncludeDisabledEntities
            });
        int count = query.CalculateEntityCount();
        query.Dispose();
        return count;
    }

    private int GetReservedQuantity(Entity task)
    {
        return _entityManager
            .GetComponentData<DroneTaskQuantity>(task)
            .reservedQuantity;
    }
}
