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
}
