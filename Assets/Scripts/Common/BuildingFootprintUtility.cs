using Unity.Burst;
using Unity.Mathematics;

/// <summary>
/// 배치 검증과 실제 건물의 채굴·입출고·공간 동기화가 공유하는 점유 크기 계산.
/// </summary>
[BurstCompile]
public static class BuildingFootprintUtility
{
    /// <summary>
    /// 각 축을 최소 1칸으로 정규화하고 Left / Right 방향이면 가로·세로를 교환한다.
    /// </summary>
    public static int2 GetEffectiveSize(int2 footprintSize, DirectionEnum direction)
    {
        int2 normalized = math.max(footprintSize, new int2(1, 1));
        return (direction == DirectionEnum.Left || direction == DirectionEnum.Right)
            ? new int2(normalized.y, normalized.x)
            : normalized;
    }

    public static int2 GetEffectiveSize(this BuildingFootprint footprint, DirectionEnum direction)
    {
        return GetEffectiveSize(footprint.Size, direction);
    }
}
