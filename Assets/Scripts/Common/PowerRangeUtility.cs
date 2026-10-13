using Unity.Mathematics;

/// <summary>단일 셀 반경의 경계 포함 정사각형 판정. 캐시나 연결 상태를 소유하지 않는다.</summary>
public static class PowerRangeUtility
{
    public static bool Contains(int2 center, int range, int2 cell)
    {
        if (range < 0)
        {
            return false;
        }
        long deltaX = (long)cell.x - center.x;
        long deltaY = (long)cell.y - center.y;
        return deltaX >= -(long)range && deltaX <= range && deltaY >= -(long)range && deltaY <= range;
    }

    public static bool AreCentersConnected(int2 firstCenter, int firstRange, int2 secondCenter, int secondRange)
    {
        return Contains(firstCenter, firstRange, secondCenter) || Contains(secondCenter, secondRange, firstCenter);
    }

    /// <summary>회전된 직사각형 전체에서 가장 가까운 점유 셀로 공급 경계와 최소 제곱거리를 구한다.</summary>
    public static bool TryGetFootprintDistanceSquared(int2 center, int range, int2 origin,
        int2 effectiveSize, out long distanceSquared)
    {
        distanceSquared = 0;
        if (effectiveSize.x <= 0 || effectiveSize.y <= 0)
        {
            return false;
        }
        int2 closestCell = new int2(
            ClosestCellCoordinate(center.x, origin.x, effectiveSize.x),
            ClosestCellCoordinate(center.y, origin.y, effectiveSize.y));
        if (!Contains(center, range, closestCell))
        {
            return false;
        }
        // 공급 범위 안의 차이만 제곱하므로 int range의 최댓값에서도 long을 넘지 않는다.
        long deltaX = (long)closestCell.x - center.x;
        long deltaY = (long)closestCell.y - center.y;
        distanceSquared = deltaX * deltaX + deltaY * deltaY;
        return true;
    }

    private static int ClosestCellCoordinate(int center, int minimum, int length)
    {
        if (center < minimum)
        {
            return minimum;
        }
        long maximum = (long)minimum + length - 1;
        if (center > maximum)
        {
            return (int)maximum;
        }
        return center;
    }
}
