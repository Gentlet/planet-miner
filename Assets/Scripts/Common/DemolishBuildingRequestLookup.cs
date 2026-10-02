using Unity.Collections;
using Unity.Entities;

/// <summary>
/// EndCommand 이후의 검증된 철거 요청과 입출고 적용 후 소유 버퍼를 읽는다.
/// 철거 승인이나 반환 결과를 별도로 저장하지 않는다.
/// </summary>
internal static class DemolishBuildingRequestLookup
{
    public static bool ContainsTarget(
        Entity targetBuilding,
        in NativeList<DemolishBuildingRequest> requests)
    {
        if (targetBuilding == Entity.Null)
        {
            return false;
        }

        for (int i = 0; i < requests.Length; i++)
        {
            if (requests[i].TargetBuilding == targetBuilding)
            {
                return true;
            }
        }

        return false;
    }

    public static bool ContainsBufferedItem(
        Entity item,
        in NativeList<DemolishBuildingRequest> requests,
        in BufferLookup<StoredItemElement> storedBufferLookup,
        in BufferLookup<ProductItemElement> productBufferLookup)
    {
        if (item == Entity.Null)
        {
            return false;
        }

        for (int requestIndex = 0; requestIndex < requests.Length; requestIndex++)
        {
            Entity building = requests[requestIndex].TargetBuilding;
            if (building == Entity.Null)
            {
                continue;
            }

            if (storedBufferLookup.HasBuffer(building))
            {
                var storedItems = storedBufferLookup[building];
                for (int itemIndex = 0; itemIndex < storedItems.Length; itemIndex++)
                {
                    if (storedItems[itemIndex].ItemEntity == item)
                    {
                        return true;
                    }
                }
            }

            if (productBufferLookup.HasBuffer(building))
            {
                var productItems = productBufferLookup[building];
                for (int itemIndex = 0; itemIndex < productItems.Length; itemIndex++)
                {
                    if (productItems[itemIndex].ItemEntity == item)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }
}
