using Unity.Entities;

public static class ProducedItemBufferExtension
{
    public static int CountItems(
        this DynamicBuffer<ProducedItemElement> producedItems,
        ItemTypeEnum itemType)
    {
        int count = 0;

        for (int i = 0; i < producedItems.Length; i++)
        {
            if (producedItems[i].type == itemType)
                count++;
        }

        return count;
    }
}
