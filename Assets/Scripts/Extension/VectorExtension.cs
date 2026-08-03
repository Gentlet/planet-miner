using Unity.Mathematics;
using UnityEngine;

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
