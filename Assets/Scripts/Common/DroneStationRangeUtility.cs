using Unity.Burst;
using Unity.Mathematics;

/// <summary>포함 경계를 가진 격자 활동 영역. int 위치/범위의 합산·청크 배수는 long으로 계산해 오버플로를 막는다.</summary>
public struct DroneStationActivityBounds
{
    public long MinX;
    public long MinY;
    public long MaxX;
    public long MaxY;

    public bool Contains(int2 position) => position.x >= MinX && position.x <= MaxX &&
        position.y >= MinY && position.y <= MaxY;

    public bool Overlaps(in DroneStationActivityBounds other) => MinX <= other.MaxX && MaxX >= other.MinX &&
        MinY <= other.MaxY && MaxY >= other.MinY;
}

/// <summary>상태를 저장하지 않는 정사각형 범위 계산. 회전·크기는 기존 공통 유틸리티로 한 번 적용한다.</summary>
[BurstCompile]
public static class DroneStationRangeUtility
{
    public static bool IsStationType(BuildingTypeEnum type) =>
        type == BuildingTypeEnum.MainFacility || type == BuildingTypeEnum.DroneStation;

    /// <summary>
    /// 긴 변에 맞춰 짧은 축을 양쪽으로 확장한다. 홀수 여백의 남는 1셀은 양의 축에 둔다.
    /// 이 정사각형의 각 경계를 단일 Range×ChunkSize만큼 확장하며 실제 건물 footprint는 변경하지 않는다.
    /// </summary>
    public static DroneStationActivityBounds GetActivityBounds(int2 position, int2 baseFootprint,
        DirectionEnum direction, int rangeInChunks)
    {
        int2 size = BuildingFootprintUtility.GetEffectiveSize(baseFootprint, direction);
        long side = math.max(size.x, size.y);
        long expansion = (long)math.max(0, rangeInChunks) * GameConstants.ChunkSize;
        long squareMinX = (long)position.x - (side - size.x) / 2;
        long squareMinY = (long)position.y - (side - size.y) / 2;
        return new DroneStationActivityBounds
        {
            MinX = squareMinX - expansion,
            MinY = squareMinY - expansion,
            MaxX = squareMinX + side - 1 + expansion,
            MaxY = squareMinY + side - 1 + expansion
        };
    }
}
