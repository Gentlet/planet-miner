using Unity.Mathematics;

public static class Float3Extension
{
    public static float2 ToFloat2(this float3 v)
    {
        return new float2(v.x, v.y);
    }

    public static int2 ToGridCell(this float3 v)
    {
        return new int2(
            (int)math.floor(v.x + 0.5f),
            (int)math.floor(v.y + 0.5f));
    }

    public static bool IsInsideCell(this float3 v, int2 cell)
    {
        const float cellHalfSize = 0.5f;
        const float epsilon = 0.0001f;

        return math.abs(v.x - cell.x) <= cellHalfSize + epsilon &&
               math.abs(v.y - cell.y) <= cellHalfSize + epsilon;
    }
}
