using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: 건물 출고와 분배/합류가 벨트 입구 여유와 출구 이동품을 같은 기준으로 판단하게 한다.
/// 입력·출력: 벨트/아이템 인덱스와 Lookup을 읽어 진입 여유나 출구 후보를 반환하며 ECS 상태는 쓰지 않는다.
/// 이용: StorageItemOutputDecisionSystem, ProductItemOutputDecisionSystem, SplitterDecisionSystem, MergerDecisionSystem이 후보 판단에서 호출한다.
/// 수명·경계: 호출자가 인덱스의 Job 의존성과 Fence를 등록하며, 출고 후보 간 경합은 Reservation이 중재한다.
/// </summary>
public static class BeltEntryUtility
{
    internal static bool TryFindBuildingOutputBelt(
        int2 anchor,
        int2 size,
        in NativeParallelHashMap<int2, BeltInfo> beltMap,
        in NativeParallelMultiHashMap<int2, Entity> itemMap,
        in ComponentLookup<PendingBuildingDemolition> pendingDemolitionLookup,
        in ComponentLookup<ItemOwnership> ownershipLookup,
        in ComponentLookup<BeltMovementState> movementLookup,
        out int2 beltPosition)
    {
        // 하단, 우측, 상단, 좌측 순으로 첫 후보를 선택한다. 목적지 경합은 Reservation에서 판단한다.
        for (int x = 0; x < size.x; x++)
        {
            beltPosition = new int2(anchor.x + x, anchor.y - 1);
            if (CanOutputToBelt(beltPosition, anchor, size, beltMap, itemMap, pendingDemolitionLookup,
                    ownershipLookup, movementLookup))
            {
                return true;
            }
        }

        for (int y = 0; y < size.y; y++)
        {
            beltPosition = new int2(anchor.x + size.x, anchor.y + y);
            if (CanOutputToBelt(beltPosition, anchor, size, beltMap, itemMap, pendingDemolitionLookup,
                    ownershipLookup, movementLookup))
            {
                return true;
            }
        }

        for (int x = 0; x < size.x; x++)
        {
            beltPosition = new int2(anchor.x + x, anchor.y + size.y);
            if (CanOutputToBelt(beltPosition, anchor, size, beltMap, itemMap, pendingDemolitionLookup,
                    ownershipLookup, movementLookup))
            {
                return true;
            }
        }

        for (int y = 0; y < size.y; y++)
        {
            beltPosition = new int2(anchor.x - 1, anchor.y + y);
            if (CanOutputToBelt(beltPosition, anchor, size, beltMap, itemMap, pendingDemolitionLookup,
                    ownershipLookup, movementLookup))
            {
                return true;
            }
        }

        beltPosition = default;
        return false;
    }

    private static bool CanOutputToBelt(
        int2 beltPosition,
        int2 anchor,
        int2 size,
        in NativeParallelHashMap<int2, BeltInfo> beltMap,
        in NativeParallelMultiHashMap<int2, Entity> itemMap,
        in ComponentLookup<PendingBuildingDemolition> pendingDemolitionLookup,
        in ComponentLookup<ItemOwnership> ownershipLookup,
        in ComponentLookup<BeltMovementState> movementLookup)
    {
        if (!beltMap.TryGetValue(beltPosition, out BeltInfo belt))
        {
            return false;
        }

        if (pendingDemolitionLookup.HasComponent(belt.Entity))
        {
            return false;
        }

        // 벨트의 이전 타일이 건물 내부에 있어야 건물에서 외향으로 연결된다.
        int2 previousPosition = beltPosition - belt.Direction.ToInt2();
        if (previousPosition.x < anchor.x || previousPosition.x >= anchor.x + size.x ||
            previousPosition.y < anchor.y || previousPosition.y >= anchor.y + size.y)
        {
            return false;
        }

        return HasEntrySpace(beltPosition, itemMap, ownershipLookup, movementLookup);
    }

    internal static Entity FindItemAtBeltEnd(
        int2 beltPosition,
        in NativeParallelMultiHashMap<int2, Entity> itemMap,
        in ComponentLookup<ItemOwnership> ownershipLookup,
        in ComponentLookup<BeltMovementState> movementLookup)
    {
        if (!itemMap.TryGetFirstValue(beltPosition, out Entity item, out var iterator))
        {
            return Entity.Null;
        }

        Entity bestItem = Entity.Null;
        float maxProgress = 1f - GameConstants.AlignmentEpsilon;
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

            if (!movementLookup.TryGetComponent(item, out var movement))
            {
                continue;
            }

            // 잔여 진행도가 있더라도 이동이 꺼진 바닥 아이템은 라우팅하지 않는다.
            if (!movementLookup.IsComponentEnabled(item))
            {
                continue;
            }

            if (movement.Progress >= maxProgress)
            {
                bestItem = item;
                maxProgress = movement.Progress;
            }
        }
        while (itemMap.TryGetNextValue(out item, ref iterator));

        return bestItem;
    }

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

            if (!movementLookup.TryGetComponent(item, out var movement))
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

            if (movement.Progress < minimumGap)
            {
                return false;
            }
        }
        while (itemMap.TryGetNextValue(out item, ref iterator));

        return true;
    }
}
