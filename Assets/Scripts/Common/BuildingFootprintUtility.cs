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
    // 벨트와 건물 사이의 경계를 덮는 표시 전용 겹침 폭이다. 단위는 변당 월드 칸 수다.
    public const float VisualOverlapPerSide = 0.2f;

    /// <summary>
    /// 각 축을 최소 1칸으로 정규화하고 Left / Right 방향이면 가로·세로를 교환한다.
    /// </summary>
    public static int2 GetEffectiveSize(int2 footprintSize, DirectionEnum direction)
    {
        int2 normalized = math.max(footprintSize, new int2(1, 1));
        return SwapsAxes(direction) ? normalized.yx : normalized;
    }

    public static bool Contains(int2 origin, int2 effectiveSize, int2 cell) =>
        cell.x >= origin.x && cell.y >= origin.y &&
        (long)cell.x < (long)origin.x + effectiveSize.x && (long)cell.y < (long)origin.y + effectiveSize.y;

    public static int2 GetEffectiveSize(this BuildingFootprint footprint, DirectionEnum direction)
    {
        return GetEffectiveSize(footprint.Size, direction);
    }

    public static float GetVisualOverlapPerSide(in BuildingConfigElement config) =>
        config.BeltTransport.IsPresent ? 0f : math.max(0f, VisualOverlapPerSide);

    public static float4x4 GetVisualPostTransform(int2 footprint, DirectionEnum direction, float textureHeightToWidth,
        float overlapPerSide)
    {
        int2 effectiveSize = GetEffectiveSize(footprint, direction);
        float width = footprint.x;
        float height = textureHeightToWidth > 0f ? width * textureHeightToWidth : footprint.y;
        float2 localSize = new float2(width, height);
        float2 rotatedSize = SwapsAxes(direction) ? localSize.yx : localSize;
        float scaleToFitWidth = math.min(1f, effectiveSize.x / rotatedSize.x);
        float heightOverflow = math.max(0f, rotatedSize.y * scaleToFitWidth - effectiveSize.y);

        // 기본 이미지의 하단을 맞춘 뒤 같은 중심에서 각 변을 바깥쪽으로 늘린다.
        float3 centerOffset = new float3(((float2)effectiveSize - 1f) * 0.5f, 0f);
        centerOffset.y += heightOverflow * 0.5f;
        float3 localOffset = math.rotate(math.inverse(direction.ToWorldRotation()), centerOffset);
        float2 visualSize = localSize * scaleToFitWidth + 2f * overlapPerSide;
        return float4x4.TRS(localOffset, quaternion.identity, new float3(visualSize, 1f));
    }

    private static bool SwapsAxes(DirectionEnum direction) =>
        direction == DirectionEnum.Left || direction == DirectionEnum.Right;
}
