using Unity.Collections;
using Unity.Entities;

public static class ItemStorageLimitBufferExtension
{
    public static int GetStorageLimit(
        this DynamicBuffer<ItemStorageLimitElement> storageLimits,
        ItemTypeEnum itemType)
    {
        for (int i = 0; i < storageLimits.Length; i++)
        {
            if (storageLimits[i].itemType == itemType)
                return storageLimits[i].maxAmount;
        }

        return 0;
    }

    public static int GetStorageLimit(
        this NativeArray<ItemStorageLimitElement> storageLimits,
        ItemTypeEnum itemType)
    {
        for (int i = 0; i < storageLimits.Length; i++)
        {
            if (storageLimits[i].itemType == itemType)
                return storageLimits[i].maxAmount;
        }

        return 0;
    }
}
