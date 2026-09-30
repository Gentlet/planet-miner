using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 건물 출고와 Routing이 공유하는 벨트 입구의 현재 점유 판정.
/// 벨트 존재/연결 검사는 호출자가 담당하며, 신규 후보 사이의 경합은 Reservation이 처리한다.
/// 공간 인덱스와 Lookup을 읽기만 한다. 호출 Job의 의존성과 Fence 등록도 호출자가 유지한다.
/// </summary>
public static class BeltEntryUtility
{
    public static bool HasEntrySpace(
        int2 targetPosition,
        in NativeParallelMultiHashMap<int2, Entity> itemMap,
        in ComponentLookup<ItemOwnership> ownershipLookup,
        in ComponentLookup<BeltMovementState> movementLookup)
    {
        if (!itemMap.TryGetFirstValue(targetPosition, out Entity item, out var iterator))
        {
            return true;
        }

        int occupyingItemCount = 0;
        float minimumGap = GameConstants.ItemSpacing - GameConstants.AlignmentEpsilon;

        do
        {
            if (!ownershipLookup.TryGetComponent(item, out var ownership))
            {
                continue;
            }

            if (!ownership.IsWorldItem)
            {
                continue;
            }

            if (!movementLookup.HasComponent(item))
            {
                continue;
            }

            if (!movementLookup.IsComponentEnabled(item))
            {
                continue;
            }

            occupyingItemCount++;
            if (occupyingItemCount >= GameConstants.MaxItemsPerBeltTile)
            {
                return false;
            }

            if (movementLookup[item].Progress < minimumGap)
            {
                return false;
            }
        }
        while (itemMap.TryGetNextValue(out item, ref iterator));

        return true;
    }
}
