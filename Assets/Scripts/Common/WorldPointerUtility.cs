using System;
using Unity.Mathematics;
using UnityEngine;

/// <summary>XY 게임 평면과 정수 중심 셀을 변환한다. 공간 원본이나 검색 인덱스를 변경하지 않는다.</summary>
public static class WorldPointerUtility
{
    private static readonly Plane WorldPlane = new Plane(Vector3.forward, Vector3.zero);

    public static bool TryGetWorldPosition(Camera camera, Vector2 screenPosition, out Vector3 worldPosition)
    {
        worldPosition = default;
        if (camera == null)
        {
            return false;
        }

        if (!camera.pixelRect.Contains(screenPosition))
        {
            return false;
        }

        Ray ray = camera.ScreenPointToRay(screenPosition);
        if (!WorldPlane.Raycast(ray, out float distance))
        {
            return false;
        }

        worldPosition = ray.GetPoint(distance);
        return math.all(math.isfinite((float3)worldPosition));
    }

    public static bool TryGetGridCell(Vector3 worldPosition, out int2 cell)
    {
        cell = default;
        // 정수 위치가 셀 중심인 기존 경계를 유지한다. 음수도 같은 반열린 구간으로 변환한다.
        double x = Math.Floor((double)worldPosition.x + 0.5d);
        double y = Math.Floor((double)worldPosition.y + 0.5d);
        if (double.IsNaN(x) || double.IsNaN(y))
        {
            return false;
        }

        if (x < int.MinValue || x > int.MaxValue || y < int.MinValue || y > int.MaxValue)
        {
            return false;
        }

        cell = new int2((int)x, (int)y);
        return true;
    }
}
