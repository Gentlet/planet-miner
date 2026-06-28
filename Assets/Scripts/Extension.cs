using Unity.Mathematics;
using UnityEngine;

public static class DirectionExtension
{
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

public static class VectorExtension
{
    public static Vector2 Floor(this Vector2 v)
    {
        return new Vector2(Mathf.Floor(v.x), Mathf.Floor(v.y));
    }
    public static int2 ToInt2(this Vector2 v)
    {
        return new int2((int)v.x, (int)v.y);
    }
}

public static class Int2Extension
{
    public static Vector2 ToVector2(this int2 i)
    {
        return new Vector2(i.x, i.y);
    }
}