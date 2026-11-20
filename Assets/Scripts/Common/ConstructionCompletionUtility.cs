using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>완공 소유자와 같은 공개 경계의 생성 검사가 동일한 실제 자재/바닥 차단 조건을 사용하게 한다.</summary>
public static class ConstructionCompletionUtility
{
    /// <summary>호출자가 준비한 목록에 실제 월드 장애물을 추가한다. 활성 삭제 예정 실물은 종료 경계에서 사라지므로 제외한다.</summary>
    internal static void CollectWorldItemPositions(NativeArray<Entity> items,
        in ComponentLookup<DestroyItemRequest> destroyRequests,
        in ComponentLookup<GridPosition> positions, NativeList<int2> result)
    {
        for (int i = 0; i < items.Length; i++)
        {
            Entity item = items[i];
            if (destroyRequests.HasComponent(item) && destroyRequests.IsComponentEnabled(item))
            {
                continue;
            }
            result.Add(positions[item].Value);
        }
    }

    public static bool AreMaterialsSatisfied(in DynamicBuffer<ConstructionMaterialRequirementElement> requirements)
    {
        for (int i = 0; i < requirements.Length; i++)
        {
            if (!requirements[i].IsSatisfied)
            {
                return false;
            }
        }
        return true;
    }

    internal static bool HasBlockingWorldItem(int2 origin, int2 effectiveSize, in NativeParallelHashSet<int2> occupiedCells, int itemCount)
    {
        // 넓은 현장에 실물이 드문 경우 셀 면적 전체를 순회하지 않는다.
        if ((long)effectiveSize.x * effectiveSize.y > itemCount)
        {
            foreach (var cell in occupiedCells)
            {
                if (BuildingFootprintUtility.Contains(origin, effectiveSize, cell))
                {
                    return true;
                }
            }
            return false;
        }
        for (int y = 0; y < effectiveSize.y; y++)
        {
            for (int x = 0; x < effectiveSize.x; x++)
            {
                if (occupiedCells.Contains(origin + new int2(x, y)))
                {
                    return true;
                }
            }
        }
        return false;
    }

    public static bool HasBlockingWorldItem(int2 origin, int2 effectiveSize, in NativeArray<int2> worldPositions)
    {
        for (int i = 0; i < worldPositions.Length; i++)
        {
            int2 position = worldPositions[i];
            if (BuildingFootprintUtility.Contains(origin, effectiveSize, position))
            {
                return true;
            }
        }
        return false;
    }
}
