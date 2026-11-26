using Unity.Burst;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: 배치 검증·채굴·입출고·공간 동기화에서 동일한 회전 footprint를 계산한다.
/// 입력·출력: 방향 적용 전 기본 크기를 최소 1칸으로 정규화하고 Left/Right이면 축을 교환한 값을 반환한다.
/// 이용·수명: 각 Reader가 호출하는 순수 계산이며 상태·캐시·엔티티를 만들지 않는다. 반환값을 원본 Size로 저장해 이중 회전하지 않는다.
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

    public static bool Contains(int2 origin, int2 effectiveSize, int2 cell) =>
        cell.x >= origin.x && cell.y >= origin.y &&
        (long)cell.x < (long)origin.x + effectiveSize.x && (long)cell.y < (long)origin.y + effectiveSize.y;

    public static int2 GetEffectiveSize(this BuildingFootprint footprint, DirectionEnum direction)
    {
        return GetEffectiveSize(footprint.Size, direction);
    }

    // 렌더 회전 전 메시 축으로 중심 오프셋을 되돌려 격자 원점과 회전 후 점유 영역을 맞춘다.
    public static float4x4 GetVisualPostTransform(int2 footprint, DirectionEnum direction, float textureHeightToWidth)
    {
        int2 effectiveSize = GetEffectiveSize(footprint, direction);
        float3 centerOffset = new float3(((float2)effectiveSize - 1f) * 0.5f, 0f);
        float3 localOffset = math.rotate(math.inverse(direction.ToWorldRotation()), centerOffset);
        float width = footprint.x;
        float height = textureHeightToWidth > 0f ? width * textureHeightToWidth : footprint.y;
        return float4x4.TRS(localOffset, quaternion.identity, new float3(width, height, 1f));
    }
}
