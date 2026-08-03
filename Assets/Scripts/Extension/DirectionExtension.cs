using Unity.Mathematics;
using UnityEngine;

public static class DirectionExtension
{
    public static DirectionEnum NextDirection(this DirectionEnum dir, DirectionEnum next)
    {
        return (DirectionEnum)(((int)dir + (int)next) % (int)DirectionEnum.Count);
    }

    public static DirectionEnum NextDirection(this DirectionEnum dir)
    {
        return (DirectionEnum)(((int)dir + 1) % (int)DirectionEnum.Count);
    }

    public static float3 Tofloat3(this DirectionEnum dir)
    {
        switch (dir)
        {
            case DirectionEnum.Up:
                return new float3(0, 1, 0);
            case DirectionEnum.Down:
                return new float3(0, -1, 0);
            case DirectionEnum.Left:
                return new float3(-1, 0, 0);
            case DirectionEnum.Right:
                return new float3(1, 0, 0);
            default:
                return new float3(0, 0, 0);
        }
    }

    public static Vector2 ToVector2(this DirectionEnum dir)
    {
        switch (dir)
        {
            case DirectionEnum.Up:
                return new Vector2(0, 1);
            case DirectionEnum.Down:
                return new Vector2(0, -1);
            case DirectionEnum.Left:
                return new Vector2(-1, 0);
            case DirectionEnum.Right:
                return new Vector2(1, 0);
            default:
                return new Vector2(0, 0);
        }
    }

    public static int2 ToInt2(this DirectionEnum dir)
    {
        switch (dir)
        {
            case DirectionEnum.Up:
                return new int2(0, 1);
            case DirectionEnum.Down:
                return new int2(0, -1);
            case DirectionEnum.Left:
                return new int2(-1, 0);
            case DirectionEnum.Right:
                return new int2(1, 0);
            default:
                return int2.zero;
        }
    }

    public static DirectionEnum GetMoveDirection(float3 from, float3 to)
    {
        float2 offset = to.xy - from.xy;

        if (math.abs(offset.x) > math.abs(offset.y))
            return offset.x > 0f ? DirectionEnum.Right : DirectionEnum.Left;

        return offset.y > 0f ? DirectionEnum.Up : DirectionEnum.Down;
    }

    public static int ToDegrees(this DirectionEnum dir)
    {
        switch (dir)
        {
            case DirectionEnum.Up:
                return 0;
            case DirectionEnum.Down:
                return 180;
            case DirectionEnum.Left:
                return 90;
            case DirectionEnum.Right:
                return -90;
            default:
                return 0;
        }
    }
}
