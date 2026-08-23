using Unity.Collections;
using Unity.Entities;

public static class StorageCapacityUtility
{
    public static bool CanStoreAdditionalItems(
        DynamicBuffer<StoredItemElement> storedItems,
        DynamicBuffer<DroneReservedStorageCapacityElement> reservedCapacity,
        bool hasReservedCapacity,
        int capacity,
        NativeArray<ItemStorageLimitElement> storageLimits,
        ItemTypeEnum additionalItemType,
        int additionalQuantity,
        int storedDroneCount = 0)
    {
        if (capacity <= 0)
            return false;

        if (!additionalItemType.IsValid())
            return false;

        if (additionalQuantity <= 0)
            return false;

        if (storageLimits.GetStorageLimit(additionalItemType) <= 0)
            return false;

        int usedSlotCount = 0;

        for (ItemTypeEnum itemType = ItemTypeEnum.Iron_Ore;
             itemType < ItemTypeEnum.Count;
             itemType++)
        {
            int itemCount = storedItems.CountItems(itemType);

            if (itemType == ItemTypeEnum.Drone)
                itemCount += storedDroneCount;

            if (hasReservedCapacity)
            {
                itemCount += GetReservedQuantity(
                    reservedCapacity,
                    itemType);
            }

            if (itemType == additionalItemType)
                itemCount += additionalQuantity;

            if (itemCount == 0)
                continue;

            int stackLimit = storageLimits.GetStorageLimit(itemType);

            if (stackLimit <= 0)
                return false;

            usedSlotCount += (itemCount + stackLimit - 1) / stackLimit;

            if (usedSlotCount > capacity)
                return false;
        }

        return true;
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
}
