using Unity.Entities;

/// <summary>보관 실물 조회와 사전 검증된 재료의 역순 선소비를 공통 기준으로 수행한다.</summary>
internal static class StoredItemBufferUtility
{
    /// <summary>호출자가 전체 요구량을 검증한 뒤 사용한다. 보관 참조를 먼저 제거하며 실제 삭제는 Item Lifecycle/EndBuilding이 수행한다.</summary>
    internal static void ConsumeRequiredItems(DynamicBuffer<StoredItemElement> storedItems, ItemTypeEnum itemType,
        int quantity, ref ComponentLookup<DestroyItemRequest> destroyRequests)
    {
        for (int index = storedItems.Length - 1; index >= 0 && quantity > 0; index--)
        {
            var item = storedItems[index];
            if (item.ItemType != itemType)
            {
                continue;
            }
            if (!RecipeConfigLookupUtility.CanConsumeStoredItem(item.ItemEntity, destroyRequests))
            {
                continue;
            }

            storedItems.RemoveAt(index);
            destroyRequests.SetComponentEnabled(item.ItemEntity, true);
            quantity--;
        }
    }

    internal static bool ContainsEntity(DynamicBuffer<StoredItemElement> items, Entity item)
    {
        for (int i = 0; i < items.Length; i++)
        {
            if (items[i].ItemEntity == item)
            {
                return true;
            }
        }

        return false;
    }

    internal static int CountConsumable(DynamicBuffer<StoredItemElement> items, ItemTypeEnum itemType,
        in ComponentLookup<DestroyItemRequest> destroyRequests)
    {
        int count = 0;
        for (int i = 0; i < items.Length; i++)
        {
            var item = items[i];
            if (item.ItemType == itemType &&
                RecipeConfigLookupUtility.CanConsumeStoredItem(item.ItemEntity, destroyRequests))
            {
                count++;
            }
        }

        return count;
    }
}
