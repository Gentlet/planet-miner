using System.Collections.Generic;
using Unity.Mathematics;

public static class BuildingFootprintUtility
{
    public static int2 NormalizeSize(int2 size)
    {
        return math.max(size, new int2(1, 1));
    }

    public static int2 RotateOffset(int2 offset, DirectionEnum direction)
    {
        return direction switch
        {
            DirectionEnum.Right => new int2(offset.y, -offset.x),
            DirectionEnum.Down => -offset,
            DirectionEnum.Left => new int2(-offset.y, offset.x),
            _ => offset
        };
    }

    public static float2 RotateOffset(float2 offset, DirectionEnum direction)
    {
        return direction switch
        {
            DirectionEnum.Right => new float2(offset.y, -offset.x),
            DirectionEnum.Down => -offset,
            DirectionEnum.Left => new float2(-offset.y, offset.x),
            _ => offset
        };
    }

    public static float2 GetVisualCenterOffset(int2 size, DirectionEnum direction)
    {
        int2 normalizedSize = NormalizeSize(size);
        float2 localCenter = new(
            (normalizedSize.x - 1) * 0.5f,
            (normalizedSize.y - 1) * 0.5f);
        return RotateOffset(localCenter, direction);
    }

    public static void GetOccupiedCells(
        int2 anchor,
        int2 size,
        DirectionEnum direction,
        List<int2> results)
    {
        results.Clear();
        int2 normalizedSize = NormalizeSize(size);

        for (int y = 0; y < normalizedSize.y; y++)
        {
            for (int x = 0; x < normalizedSize.x; x++)
            {
                results.Add(
                    anchor + RotateOffset(new int2(x, y), direction));
            }
        }
    }
}
