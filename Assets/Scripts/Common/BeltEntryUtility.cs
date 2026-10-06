using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: 건물 출고/분배·합류가 같은 기준으로 벨트 입구의 현재 여유를 읽게 한다.
/// 입력·출력: ItemSpatialIndex와 Owner/이동 Lookup에서 활성 월드 이동품의 개수·진입 간격을 검사하며 ECS 상태는 쓰지 않는다.
/// 이용: StorageItemOutputDecisionSystem, ProductItemOutputDecisionSystem, SplitterDecisionSystem, MergerDecisionSystem이 후보 판단에서 호출한다.
/// 수명·경계: 별도 캐시나 엔티티를 만들지 않는다. 연결 검사·새 후보 간 경합·Job 의존성과 Fence 등록은 호출자/Reservation이 담당한다.
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

        // 입구에서 가까운 이동품이 최소 간격을 확보하지 못하면 타일 전체 수용량에 여유가 있어도 진입을 거부한다.
        // 수납품/정지 월드 아이템은 활성 벨트 이동 점유로 계산하지 않는다.
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
