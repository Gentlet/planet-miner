using Unity.Mathematics;
using UnityEngine;

public static class Int2Extension
{
    public static Vector2 ToVector2(this int2 i)
    {
        return new Vector2(i.x, i.y);
    }
}
