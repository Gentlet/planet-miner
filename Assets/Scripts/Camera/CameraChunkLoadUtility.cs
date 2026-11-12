using System;
using Unity.Mathematics;
using UnityEngine;

/// <summary>카메라의 월드 평면 시야에서 청크 범위와 가까운 순서를 계산한다.</summary>
public static class CameraChunkLoadUtility
{
    internal readonly struct ChunkCandidate : IComparable<ChunkCandidate>
    {
        internal readonly int2 Coordinate;
        private readonly double _distanceSquared;

        internal ChunkCandidate(int2 coordinate, Vector2 worldCenter)
        {
            Coordinate = coordinate;
            double offset = (ChunkUtility.ChunkSize - 1) * 0.5d;
            double x = coordinate.x * (double)ChunkUtility.ChunkSize + offset - worldCenter.x;
            double y = coordinate.y * (double)ChunkUtility.ChunkSize + offset - worldCenter.y;
            _distanceSquared = x * x + y * y;
        }

        public int CompareTo(ChunkCandidate other)
        {
            int distance = _distanceSquared.CompareTo(other._distanceSquared);
            if (distance != 0)
            {
                return distance;
            }
            int row = Coordinate.y.CompareTo(other.Coordinate.y);
            return row != 0 ? row : Coordinate.x.CompareTo(other.Coordinate.x);
        }
    }

    private static readonly Plane WorldPlane = new Plane(Vector3.forward, Vector3.zero);

    public static bool TryGetVisibleChunkRange(Camera camera, int marginInChunks,
        out int2 minimumChunk, out int2 maximumChunk, out Vector2 worldCenter)
    {
        minimumChunk = default;
        maximumChunk = default;
        worldCenter = default;
        if (camera == null)
        {
            return false;
        }
        if (!camera.isActiveAndEnabled || !camera.orthographic || marginInChunks < 0)
        {
            return false;
        }
        Vector2 minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        for (int corner = 0; corner < 4; corner++)
        {
            // 포인터의 pixelRect.Contains 검사와 달리 시야 끝의 viewport=1도 투영한다.
            Ray ray = camera.ViewportPointToRay(new Vector3(corner & 1, corner >> 1, 0f));
            if (!WorldPlane.Raycast(ray, out float distance))
            {
                return false;
            }
            Vector3 point = ray.GetPoint(distance);
            if (!float.IsFinite(point.x) || !float.IsFinite(point.y))
            {
                return false;
            }
            minimum = Vector2.Min(minimum, point);
            maximum = Vector2.Max(maximum, point);
        }
        if (minimum.x >= maximum.x || minimum.y >= maximum.y)
        {
            return false;
        }

        // 여유 적용 전 시야에는 정수 셀이 중심인 반열린 영역을 쓴다. 최대 경계에 닿기만 하는 다음 청크는 제외한다.
        double minX = Math.Floor(((double)minimum.x + 0.5d) / ChunkUtility.ChunkSize) - marginInChunks;
        double minY = Math.Floor(((double)minimum.y + 0.5d) / ChunkUtility.ChunkSize) - marginInChunks;
        double maxX = Math.Ceiling(((double)maximum.x + 0.5d) / ChunkUtility.ChunkSize) - 1d + marginInChunks;
        double maxY = Math.Ceiling(((double)maximum.y + 0.5d) / ChunkUtility.ChunkSize) - 1d + marginInChunks;
        minX = Math.Max(minX, ChunkUtility.MinChunkCoordinate);
        minY = Math.Max(minY, ChunkUtility.MinChunkCoordinate);
        maxX = Math.Min(maxX, ChunkUtility.MaxChunkCoordinate);
        maxY = Math.Min(maxY, ChunkUtility.MaxChunkCoordinate);
        if (minX > maxX || minY > maxY)
        {
            return false;
        }
        minimumChunk = new int2((int)minX, (int)minY);
        maximumChunk = new int2((int)maxX, (int)maxY);
        worldCenter = new Vector2((float)(((double)minimum.x + maximum.x) * 0.5d),
            (float)(((double)minimum.y + maximum.y) * 0.5d));
        return true;
    }
}
