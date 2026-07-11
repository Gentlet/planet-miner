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

public static class ResourceTypeExtension
{
    public static ItemTypeEnum ToItemType(this ResourceTypeEnum resourceType)
    {
        switch (resourceType)
        {
            case ResourceTypeEnum.Iron_Ore:
                return ItemTypeEnum.Iron_Ore;
            case ResourceTypeEnum.Copper_Ore:
                return ItemTypeEnum.Copper_Ore;
            case ResourceTypeEnum.Coal:
                return ItemTypeEnum.Coal;
            case ResourceTypeEnum.Stone:
                return ItemTypeEnum.Stone;
            default:
                return ItemTypeEnum.None;
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
    public static int2 ToInt2(this Vector3 v)
    {
        return new int2((int)v.x, (int)v.y);
    }
    public static int2 ToGridCell(this Vector3 v)
    {
        return new int2(
            Mathf.FloorToInt(v.x + 0.5f),
            Mathf.FloorToInt(v.y + 0.5f));
    }
   
}

public static class Int2Extension
{
    public static Vector2 ToVector2(this int2 i)
    {
        return new Vector2(i.x, i.y);
    }
}

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
