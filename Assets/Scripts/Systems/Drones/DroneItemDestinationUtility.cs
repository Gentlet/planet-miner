using Unity.Collections;
using Unity.Entities;

public static class DroneItemDestinationUtility
{
    public static bool CanAcceptItem(
        EntityManager entityManager,
        Entity destinationOwner,
        ItemTypeEnum itemType,
        NativeArray<CrafterRecipeElement> recipes,
        NativeArray<CrafterRecipeIngredientElement> ingredients)
    {
        if (!entityManager.Exists(destinationOwner))
            return false;

        if (!entityManager.HasBuffer<StoredItemElement>(destinationOwner))
            return false;

        if (!itemType.IsValid())
            return false;

        if (entityManager.HasComponent<ConstructionSite>(destinationOwner))
            return HasConstructionRequirement(
                entityManager,
                destinationOwner,
                itemType);

        if (entityManager.HasComponent<Storage>(destinationOwner))
            return true;

        if (!entityManager.HasComponent<Crafter>(destinationOwner))
            return false;

        Crafter crafter = entityManager.GetComponentData<Crafter>(
            destinationOwner);

        if (!crafter.selectedItemType.IsValid())
            return false;

        if (!recipes.TryFindRecipe(
                crafter.selectedItemType,
                out CrafterRecipeElement recipe))
            return false;

        return ingredients.HasIngredient(recipe.id, itemType);
    }

    public static bool CanReserveAdditionalQuantity(
        EntityManager entityManager,
        Entity destinationOwner,
        ItemTypeEnum itemType,
        int quantity,
        NativeArray<ItemStorageLimitElement> storageLimits,
        NativeArray<CrafterRecipeElement> recipes,
        NativeArray<CrafterRecipeIngredientElement> ingredients)
    {
        if (!CanAcceptItem(
                entityManager,
                destinationOwner,
                itemType,
                recipes,
                ingredients))
            return false;

        if (quantity <= 0)
            return false;

        DynamicBuffer<StoredItemElement> storedItems = entityManager
            .GetBuffer<StoredItemElement>(destinationOwner, true);
        bool hasReservedCapacity = entityManager.HasBuffer<
            DroneReservedStorageCapacityElement>(destinationOwner);
        DynamicBuffer<DroneReservedStorageCapacityElement> reservedCapacity =
            hasReservedCapacity
                ? entityManager.GetBuffer<
                    DroneReservedStorageCapacityElement>(
                    destinationOwner,
                    true)
                : default;

        if (entityManager.HasComponent<ConstructionSite>(destinationOwner))
        {
            int requiredQuantity = GetConstructionRequiredQuantity(
                entityManager,
                destinationOwner,
                itemType);
            int constructionStoredQuantity = storedItems.CountItems(itemType);
            int constructionReservedQuantity = hasReservedCapacity
                ? GetReservedQuantity(reservedCapacity, itemType)
                : 0;
            return requiredQuantity > 0 &&
                   constructionStoredQuantity +
                   constructionReservedQuantity +
                   quantity <= requiredQuantity;
        }

        if (entityManager.HasComponent<Storage>(destinationOwner))
        {
            Storage storage = entityManager.GetComponentData<Storage>(
                destinationOwner);
            return StorageCapacityUtility.CanStoreAdditionalItems(
                storedItems,
                reservedCapacity,
                hasReservedCapacity,
                storage.capacity,
                storageLimits,
                itemType,
                quantity,
                DroneStationStorageUtility.GetStoredDroneCount(
                    entityManager,
                    destinationOwner));
        }

        int storageLimit = storageLimits.GetStorageLimit(itemType);

        if (storageLimit <= 0)
            return false;

        int storedQuantity = storedItems.CountItems(itemType);
        int reservedQuantity = hasReservedCapacity
            ? GetReservedQuantity(reservedCapacity, itemType)
            : 0;
        return storedQuantity + reservedQuantity + quantity <= storageLimit;
    }

    private static int GetReservedQuantity(
        DynamicBuffer<DroneReservedStorageCapacityElement> reservedCapacity,
        ItemTypeEnum itemType)
    {
        for (int i = 0; i < reservedCapacity.Length; i++)
        {
            if (reservedCapacity[i].itemType == itemType)
                return reservedCapacity[i].quantity;
        }

        return 0;
    }

    private static bool HasConstructionRequirement(
        EntityManager entityManager,
        Entity siteEntity,
        ItemTypeEnum itemType)
    {
        return GetConstructionRequiredQuantity(
            entityManager,
            siteEntity,
            itemType) > 0;
    }

    private static int GetConstructionRequiredQuantity(
        EntityManager entityManager,
        Entity siteEntity,
        ItemTypeEnum itemType)
    {
        if (!entityManager.HasBuffer<ConstructionMaterialRequirementElement>(
                siteEntity))
            return 0;

        DynamicBuffer<ConstructionMaterialRequirementElement> requirements =
            entityManager.GetBuffer<ConstructionMaterialRequirementElement>(
                siteEntity,
                true);

        for (int i = 0; i < requirements.Length; i++)
        {
            if (requirements[i].itemType == itemType)
                return requirements[i].quantity;
        }

        return 0;
    }
}
