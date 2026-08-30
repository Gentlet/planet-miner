using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

public partial class DroneBuildingItemTaskPlanningSystem
{
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
        int minimumQuantity = 1;
        int reservableQuantity = 0;

        while (minimumQuantity <= maximumQuantity)
        {
            int quantity = minimumQuantity +
                           (maximumQuantity - minimumQuantity) / 2;

            if (DroneItemDestinationUtility.CanReserveAdditionalQuantity(
                    EntityManager,
                    destinationOwner,
                    itemType,
                    quantity,
                    storageLimits,
                    recipes,
                    ingredients))
            {
                reservableQuantity = quantity;
                minimumQuantity = quantity + 1;
            }
            else
            {
                maximumQuantity = quantity - 1;
            }
        }

        return reservableQuantity;
    }

    private bool HasSufficientDestinationCapacity(
        Entity sourceOwner,
        ItemTypeEnum itemType,
        int requiredQuantity,
        int networkId,
        NativeArray<Entity> storageEntities,
        NativeArray<ItemStorageLimitElement> storageLimits,
        NativeArray<CrafterRecipeElement> recipes,
        NativeArray<CrafterRecipeIngredientElement> ingredients)
    {
        int remainingQuantity = requiredQuantity;

        for (int i = 0; i < storageEntities.Length; i++)
        {
            Entity candidate = storageEntities[i];

            if (candidate == sourceOwner)
                continue;

            if (!IsSearchableStorage(candidate))
                continue;

            if (!IsInNetwork(candidate, networkId))
                continue;

            remainingQuantity -= FindReservableQuantity(
                candidate,
                itemType,
                remainingQuantity,
                storageLimits,
                recipes,
                ingredients);

            if (remainingQuantity <= 0)
                return true;
        }

        return false;
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
}
