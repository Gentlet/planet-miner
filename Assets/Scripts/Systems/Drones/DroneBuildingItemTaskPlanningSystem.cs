using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

[UpdateAfter(typeof(DroneTaskCommandSystem))]
[UpdateAfter(typeof(DroneStationNetworkSystem))]
[UpdateBefore(typeof(DroneTaskReservationSystem))]
public partial class DroneBuildingItemTaskPlanningSystem : SystemBase
{
    private EntityQuery _taskQuery;
    private EntityQuery _storageQuery;
    private DroneStationNetworkSystem _networkSystem;

    protected override void OnCreate()
    {
        _taskQuery = GetEntityQuery(
            ComponentType.ReadOnly<DroneTask>(),
            ComponentType.ReadOnly<DroneTaskStatus>(),
            ComponentType.ReadOnly<DroneTaskQuantity>(),
            ComponentType.ReadOnly<DroneBuildingItemTaskData>());
        _storageQuery = GetEntityQuery(
            ComponentType.ReadOnly<Storage>(),
            ComponentType.ReadOnly<BuildingType>(),
            ComponentType.ReadOnly<GridPosition>(),
            ComponentType.ReadOnly<StoredItemElement>());
        _networkSystem = World.GetOrCreateSystemManaged<
            DroneStationNetworkSystem>();

        RequireForUpdate<DroneConfig>();
        RequireForUpdate<CrafterConfig>();
        RequireForUpdate<ItemStorageLimitElement>();
    }

    protected override void OnUpdate()
    {
        DroneConfig droneConfig = SystemAPI.GetSingleton<DroneConfig>();
        using NativeArray<ItemStorageLimitElement> storageLimits =
            DynamicBufferCopyUtility.CreateNativeCopy(
                SystemAPI.GetSingletonBuffer<ItemStorageLimitElement>(true),
                Allocator.Temp);
        using NativeArray<CrafterRecipeElement> recipes =
            DynamicBufferCopyUtility.CreateNativeCopy(
                SystemAPI.GetSingletonBuffer<CrafterRecipeElement>(true),
                Allocator.Temp);
        using NativeArray<CrafterRecipeIngredientElement> ingredients =
            DynamicBufferCopyUtility.CreateNativeCopy(
                SystemAPI.GetSingletonBuffer<
                    CrafterRecipeIngredientElement>(true),
                Allocator.Temp);
        using NativeArray<Entity> taskEntities =
            _taskQuery.ToEntityArray(Allocator.Temp);
        using NativeArray<Entity> storageEntities =
            _storageQuery.ToEntityArray(Allocator.Temp);

        for (int i = 0; i < taskEntities.Length; i++)
        {
            PlanNextReservation(
                taskEntities[i],
                droneConfig.carryingCapacity,
                storageEntities,
                storageLimits,
                recipes,
                ingredients);
        }
    }

    private void PlanNextReservation(
        Entity taskEntity,
        int carryingCapacity,
        NativeArray<Entity> storageEntities,
        NativeArray<ItemStorageLimitElement> storageLimits,
        NativeArray<CrafterRecipeElement> recipes,
        NativeArray<CrafterRecipeIngredientElement> ingredients)
    {
        DroneTaskStatus status = EntityManager
            .GetComponentData<DroneTaskStatus>(taskEntity);

        if (status.state != DroneTaskStateEnum.Pending &&
            status.state != DroneTaskStateEnum.InProgress)
            return;

        DroneTaskQuantity taskQuantity = EntityManager
            .GetComponentData<DroneTaskQuantity>(taskEntity);

        if (taskQuantity.UnreservedQuantity <= 0)
            return;

        DroneTask task = EntityManager.GetComponentData<DroneTask>(taskEntity);
        DroneBuildingItemTaskData taskData = EntityManager
            .GetComponentData<DroneBuildingItemTaskData>(taskEntity);

        if (!ValidateTarget(taskEntity, taskData.targetBuilding))
            return;

        if (!TryGetNetworkId(taskData.targetBuilding, out int networkId))
            return;

        if (task.type == DroneTaskTypeEnum.InsertBuildingItem ||
            task.type == DroneTaskTypeEnum.Construction)
        {
            if (!DroneItemDestinationUtility.CanAcceptItem(
                    EntityManager,
                    taskData.targetBuilding,
                    taskData.itemType,
                    recipes,
                    ingredients))
            {
                CancelTask(taskEntity);
                return;
            }

            PlanInsertion(
                taskEntity,
                taskData,
                taskQuantity.UnreservedQuantity,
                carryingCapacity,
                networkId,
                storageEntities,
                storageLimits,
                recipes,
                ingredients);
            return;
        }

        if (task.type == DroneTaskTypeEnum.RemoveBuildingItem)
        {
            PlanRemoval(
                taskEntity,
                taskData,
                taskQuantity.UnreservedQuantity,
                carryingCapacity,
                networkId,
                storageEntities,
                storageLimits,
                recipes,
                ingredients);
            return;
        }

        CancelTask(taskEntity);
    }

    private void PlanInsertion(
        Entity taskEntity,
        DroneBuildingItemTaskData taskData,
        int unreservedQuantity,
        int carryingCapacity,
        int networkId,
        NativeArray<Entity> storageEntities,
        NativeArray<ItemStorageLimitElement> storageLimits,
        NativeArray<CrafterRecipeElement> recipes,
        NativeArray<CrafterRecipeIngredientElement> ingredients)
    {
        int maximumQuantity = math.min(
            unreservedQuantity,
            carryingCapacity);
        maximumQuantity = FindReservableQuantity(
            taskData.targetBuilding,
            taskData.itemType,
            maximumQuantity,
            storageLimits,
            recipes,
            ingredients);

        if (maximumQuantity <= 0)
            return;

        if (!TryFindNearestItemSource(
                taskData.targetBuilding,
                taskData.itemType,
                maximumQuantity,
                networkId,
                storageEntities,
                out Entity sourceOwner,
                out int availableQuantity))
            return;

        CreateReservationRequest(
            taskEntity,
            sourceOwner,
            taskData.targetBuilding,
            taskData.itemType,
            math.min(maximumQuantity, availableQuantity));
    }

    private void PlanRemoval(
        Entity taskEntity,
        DroneBuildingItemTaskData taskData,
        int unreservedQuantity,
        int carryingCapacity,
        int networkId,
        NativeArray<Entity> storageEntities,
        NativeArray<ItemStorageLimitElement> storageLimits,
        NativeArray<CrafterRecipeElement> recipes,
        NativeArray<CrafterRecipeIngredientElement> ingredients)
    {
        int availableQuantity = CountAvailableItems(
            taskData.targetBuilding,
            taskData.itemType);
        int maximumQuantity = math.min(
            math.min(unreservedQuantity, carryingCapacity),
            availableQuantity);

        if (maximumQuantity <= 0)
            return;

        if (!TryFindNearestDestination(
                taskData.targetBuilding,
                taskData.itemType,
                maximumQuantity,
                networkId,
                storageEntities,
                storageLimits,
                recipes,
                ingredients,
                out Entity destinationOwner,
                out int reservableQuantity))
            return;

        CreateReservationRequest(
            taskEntity,
            taskData.targetBuilding,
            destinationOwner,
            taskData.itemType,
            reservableQuantity);
    }

    private bool TryFindNearestItemSource(
        Entity targetBuilding,
        ItemTypeEnum itemType,
        int maximumQuantity,
        int networkId,
        NativeArray<Entity> storageEntities,
        out Entity sourceOwner,
        out int availableQuantity)
    {
        sourceOwner = Entity.Null;
        availableQuantity = 0;
        float nearestDistanceSquared = float.MaxValue;

        for (int i = 0; i < storageEntities.Length; i++)
        {
            Entity candidate = storageEntities[i];

            if (candidate == targetBuilding)
                continue;

            if (!IsSearchableStorage(candidate))
                continue;

            if (!IsInNetwork(candidate, networkId))
                continue;

            int candidateQuantity = CountAvailableItems(
                candidate,
                itemType);

            if (candidateQuantity <= 0)
                continue;

            float distanceSquared = GetDistanceSquared(
                candidate,
                targetBuilding);

            if (!IsBetterCandidate(
                    candidate,
                    distanceSquared,
                    sourceOwner,
                    nearestDistanceSquared))
                continue;

            sourceOwner = candidate;
            availableQuantity = math.min(
                candidateQuantity,
                maximumQuantity);
            nearestDistanceSquared = distanceSquared;
        }

        return sourceOwner != Entity.Null;
    }

    private bool TryFindNearestDestination(
        Entity sourceOwner,
        ItemTypeEnum itemType,
        int maximumQuantity,
        int networkId,
        NativeArray<Entity> storageEntities,
        NativeArray<ItemStorageLimitElement> storageLimits,
        NativeArray<CrafterRecipeElement> recipes,
        NativeArray<CrafterRecipeIngredientElement> ingredients,
        out Entity destinationOwner,
        out int reservableQuantity)
    {
        destinationOwner = Entity.Null;
        reservableQuantity = 0;
        float nearestDistanceSquared = float.MaxValue;

        for (int i = 0; i < storageEntities.Length; i++)
        {
            Entity candidate = storageEntities[i];

            if (candidate == sourceOwner)
                continue;

            if (!IsSearchableStorage(candidate))
                continue;

            if (!IsInNetwork(candidate, networkId))
                continue;

            int candidateQuantity = FindReservableQuantity(
                candidate,
                itemType,
                maximumQuantity,
                storageLimits,
                recipes,
                ingredients);

            if (candidateQuantity <= 0)
                continue;

            float distanceSquared = GetDistanceSquared(
                sourceOwner,
                candidate);

            if (!IsBetterCandidate(
                    candidate,
                    distanceSquared,
                    destinationOwner,
                    nearestDistanceSquared))
                continue;

            destinationOwner = candidate;
            reservableQuantity = candidateQuantity;
            nearestDistanceSquared = distanceSquared;
        }

        return destinationOwner != Entity.Null;
    }

    private int FindReservableQuantity(
        Entity destinationOwner,
        ItemTypeEnum itemType,
        int maximumQuantity,
        NativeArray<ItemStorageLimitElement> storageLimits,
        NativeArray<CrafterRecipeElement> recipes,
        NativeArray<CrafterRecipeIngredientElement> ingredients)
    {
        for (int quantity = maximumQuantity; quantity > 0; quantity--)
        {
            if (DroneItemDestinationUtility.CanReserveAdditionalQuantity(
                    EntityManager,
                    destinationOwner,
                    itemType,
                    quantity,
                    storageLimits,
                    recipes,
                    ingredients))
                return quantity;
        }

        return 0;
    }

    private int CountAvailableItems(
        Entity owner,
        ItemTypeEnum itemType)
    {
        DynamicBuffer<StoredItemElement> storedItems = EntityManager
            .GetBuffer<StoredItemElement>(owner, true);
        int quantity = 0;

        for (int i = 0; i < storedItems.Length; i++)
        {
            StoredItemElement item = storedItems[i];

            if (item.type != itemType)
                continue;

            if (item.itemEntity == Entity.Null)
                continue;

            if (!EntityManager.Exists(item.itemEntity))
                continue;

            if (EntityManager.HasComponent<DroneItemReservation>(
                    item.itemEntity))
                continue;

            if (!EntityManager.HasComponent<StoredItem>(item.itemEntity))
                continue;

            StoredItem storedItem = EntityManager.GetComponentData<StoredItem>(
                item.itemEntity);

            if (storedItem.owner != owner)
                continue;

            quantity++;
        }

        return quantity;
    }

    private bool ValidateTarget(Entity taskEntity, Entity targetBuilding)
    {
        if (targetBuilding == Entity.Null)
        {
            CancelTask(taskEntity);
            return false;
        }

        if (!EntityManager.Exists(targetBuilding))
        {
            CancelTask(taskEntity);
            return false;
        }

        if (!EntityManager.HasBuffer<StoredItemElement>(targetBuilding))
        {
            CancelTask(taskEntity);
            return false;
        }

        if (!EntityManager.HasComponent<GridPosition>(targetBuilding))
        {
            CancelTask(taskEntity);
            return false;
        }

        return true;
    }

    private bool TryGetNetworkId(Entity owner, out int networkId)
    {
        GridPosition position = EntityManager.GetComponentData<GridPosition>(
            owner);
        return _networkSystem.TryGetNetworkIdAtCell(
            position.gridPosition,
            out networkId);
    }

    private bool IsInNetwork(Entity owner, int networkId)
    {
        return TryGetNetworkId(owner, out int ownerNetworkId) &&
               ownerNetworkId == networkId;
    }

    private bool IsSearchableStorage(Entity storageEntity)
    {
        BuildingTypeEnum buildingType = EntityManager
            .GetComponentData<BuildingType>(storageEntity)
            .type;
        return buildingType == BuildingTypeEnum.Storage ||
               buildingType == BuildingTypeEnum.DroneStation ||
               buildingType == BuildingTypeEnum.MainFacility;
    }

    private float GetDistanceSquared(Entity left, Entity right)
    {
        int2 leftPosition = EntityManager.GetComponentData<GridPosition>(left)
            .gridPosition;
        int2 rightPosition = EntityManager.GetComponentData<GridPosition>(right)
            .gridPosition;
        return math.distancesq(
            new float2(leftPosition),
            new float2(rightPosition));
    }

    private static bool IsBetterCandidate(
        Entity candidate,
        float distanceSquared,
        Entity selected,
        float selectedDistanceSquared)
    {
        if (distanceSquared < selectedDistanceSquared)
            return true;

        if (distanceSquared > selectedDistanceSquared)
            return false;

        if (selected == Entity.Null)
            return true;

        if (candidate.Index != selected.Index)
            return candidate.Index < selected.Index;

        return candidate.Version < selected.Version;
    }

    private void CreateReservationRequest(
        Entity taskEntity,
        Entity sourceOwner,
        Entity destinationOwner,
        ItemTypeEnum itemType,
        int quantity)
    {
        Entity requestEntity = EntityManager.CreateEntity();
        EntityManager.AddComponentData(
            requestEntity,
            new DroneTaskReservationRequest
            {
                taskEntity = taskEntity,
                sourceOwner = sourceOwner,
                destinationOwner = destinationOwner,
                itemType = itemType,
                quantity = quantity
            });
    }

    private void CancelTask(Entity taskEntity)
    {
        DroneTaskStatus status = EntityManager
            .GetComponentData<DroneTaskStatus>(taskEntity);
        status.state = DroneTaskStateEnum.Cancelled;
        EntityManager.SetComponentData(taskEntity, status);
    }
}
