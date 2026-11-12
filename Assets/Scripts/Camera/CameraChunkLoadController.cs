using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// 카메라 입력 반영 뒤 초기화가 만든 Queue에 시야의 신규 청크 요청만 추가한다.
/// Tracker의 집합 Writer는 Command 메인 스레드이며 LateUpdate는 같은 메인 스레드에서 읽는다.
/// 탐색 집합은 이번 시야의 순회 scratch로만 쓰고 매 호출 정리하며 생성 이력을 복제하지 않는다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
[DefaultExecutionOrder(100)]
public sealed class CameraChunkLoadController : MonoBehaviour
{
    [SerializeField, Min(0)] private int _prefetchMarginInChunks = 1;
    private Camera _camera;
    private readonly SortedSet<CameraChunkLoadUtility.ChunkCandidate> _frontier = new();
    private readonly HashSet<int2> _visited = new HashSet<int2>();

    private void OnEnable()
    {
        _camera = GetComponent<Camera>();
    }

    private void LateUpdate()
    {
        RequestVisibleChunks();
    }

    private int RequestVisibleChunks()
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null)
        {
            return 0;
        }
        if (!world.IsCreated)
        {
            return 0;
        }
        var manager = world.EntityManager;
        if (SimulationFailureUtility.HasFatalError(manager))
        {
            return 0;
        }
        if (!CameraChunkLoadUtility.TryGetVisibleChunkRange(_camera, _prefetchMarginInChunks,
            out int2 minimum, out int2 maximum, out Vector2 center))
        {
            return 0;
        }
        using var queues = manager.CreateEntityQuery(typeof(ChunkLoadRequestQueue), typeof(ChunkLoadRequestElement));
        if (queues.CalculateEntityCount() != 1)
        {
            return 0;
        }
        using var trackers = manager.CreateEntityQuery(typeof(GeneratedChunkTracker));
        if (trackers.CalculateEntityCount() != 1)
        {
            return 0;
        }
        var tracker = manager.GetComponentData<GeneratedChunkTracker>(trackers.GetSingletonEntity());
        var requests = manager.GetBuffer<ChunkLoadRequestElement>(queues.GetSingletonEntity());
        return AppendNearestRequests(requests, tracker, minimum, maximum, center);
    }

    private int AppendNearestRequests(DynamicBuffer<ChunkLoadRequestElement> requests, GeneratedChunkTracker tracker,
        int2 minimum, int2 maximum, Vector2 center)
    {
        // 등거리 경계에서는 작은 Y/X 쪽을 시작점으로 골라 첫 후보에도 동률 순서를 적용한다.
        int2 seed = new int2(
            (int)Math.Clamp(Math.Ceiling(((double)center.x + 0.5d) / ChunkUtility.ChunkSize) - 1d, minimum.x, maximum.x),
            (int)Math.Clamp(Math.Ceiling(((double)center.y + 0.5d) / ChunkUtility.ChunkSize) - 1d, minimum.y, maximum.y));
        int submitted = 0;
        try
        {
            AddCandidate(seed, minimum, maximum, center);
            while (_frontier.Count > 0 && submitted < GameConstants.MaxCameraChunkRequestsPerFrame)
            {
                var candidate = _frontier.Min;
                _frontier.Remove(candidate);
                int2 coordinate = candidate.Coordinate;
                // 거리순으로 인접 후보만 확장해 큰 시야의 미생성 영역 전체를 먼저 열거하지 않는다.
                AddCandidate(coordinate + new int2(-1, 0), minimum, maximum, center);
                AddCandidate(coordinate + new int2(1, 0), minimum, maximum, center);
                AddCandidate(coordinate + new int2(0, -1), minimum, maximum, center);
                AddCandidate(coordinate + new int2(0, 1), minimum, maximum, center);
                if (tracker.Map.Contains(coordinate) || tracker.Pending.Contains(coordinate))
                {
                    continue;
                }
                if (ContainsRequest(requests, coordinate))
                {
                    continue;
                }
                requests.Add(new ChunkLoadRequestElement(coordinate));
                submitted++;
            }
            return submitted;
        }
        finally
        {
            _frontier.Clear();
            _visited.Clear();
        }
    }

    private void AddCandidate(int2 coordinate, int2 minimum, int2 maximum, Vector2 center)
    {
        if (coordinate.x < minimum.x || coordinate.y < minimum.y || coordinate.x > maximum.x || coordinate.y > maximum.y)
        {
            return;
        }
        if (!_visited.Add(coordinate))
        {
            return;
        }
        _frontier.Add(new CameraChunkLoadUtility.ChunkCandidate(coordinate, center));
    }

    private static bool ContainsRequest(DynamicBuffer<ChunkLoadRequestElement> requests, int2 coordinate)
    {
        foreach (var request in requests)
        {
            if (request.ChunkCoord.Equals(coordinate))
            {
                return true;
            }
        }
        return false;
    }

    private void OnDisable()
    {
        _frontier.Clear();
        _visited.Clear();
        _camera = null;
    }
}
