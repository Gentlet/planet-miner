using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 입력 반영 뒤 생성 완료 Map을 메인 스레드에서 읽는다. 바닥 표시 객체와 메시만 시야 범위에 맞춰 관리한다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
[DefaultExecutionOrder(100)]
public sealed class FloorChunkRenderController : MonoBehaviour
{
    private const int RenderMarginInChunks = 1;
    private const float FloorDepth = 1f;
    private const int FloorSortingOrder = -1001;

    private readonly struct ChunkDisplay
    {
        public readonly GameObject Object;
        public readonly Mesh Mesh;

        public ChunkDisplay(GameObject displayObject, Mesh mesh)
        {
            Object = displayObject;
            Mesh = mesh;
        }
    }

    [SerializeField] private Material _materialTemplate;

    private readonly Dictionary<int2, ChunkDisplay> _displays = new();
    private readonly List<int2> _outsideRange = new();
    private readonly List<int2> _candidates = new();
    private Camera _camera;
    private World _world;
    private FloorChunkMeshBuilder _builder;
    private bool _materialSetupFailed;
    private bool _hasCandidateRange;
    private int2 _minimum;
    private int2 _maximum;
    private int _completedCount;

    private void OnEnable()
    {
        _camera = GetComponent<Camera>();
    }

    private void LateUpdate()
    {
        Refresh();
    }

    private void Refresh()
    {
        World current = World.DefaultGameObjectInjectionWorld;
        if (!ReferenceEquals(_world, current))
        {
            ReleaseWorldResources();
            _world = current;
        }
        if (_world == null)
        {
            return;
        }
        if (!_world.IsCreated)
        {
            ReleaseWorldResources();
            return;
        }
        if (_materialSetupFailed)
        {
            return;
        }
        if (!CameraChunkLoadUtility.TryGetVisibleChunkRange(_camera, RenderMarginInChunks,
            out int2 minimum, out int2 maximum, out Vector2 center))
        {
            ClearDisplays();
            return;
        }

        var manager = _world.EntityManager;
        using var trackers = manager.CreateEntityQuery(ComponentType.ReadOnly<GeneratedChunkTracker>());
        if (trackers.CalculateEntityCount() != 1)
        {
            ClearDisplays();
            return;
        }

        var completed = manager.GetComponentData<GeneratedChunkTracker>(trackers.GetSingletonEntity()).Map;
        RefreshCandidates(completed, minimum, maximum);
        if (_candidates.Count == 0)
        {
            return;
        }

        using var configurations = manager.CreateEntityQuery(ComponentType.ReadOnly<ResourceGenerationSettings>(),
            ComponentType.ReadOnly<FloorGenerationSettings>(), ComponentType.ReadOnly<FloorBiomeElement>(),
            ComponentType.ReadOnly<FloorVariantElement>());
        if (configurations.CalculateEntityCount() != 1)
        {
            return;
        }

        Entity config = configurations.GetSingletonEntity();
        var variants = manager.GetBuffer<FloorVariantElement>(config, true);
        if (_builder == null)
        {
            if (!TryPrepareBuilder(variants))
            {
                return;
            }
        }

        var settings = manager.GetComponentData<FloorGenerationSettings>(config);
        var biomes = manager.GetBuffer<FloorBiomeElement>(config, true);
        uint seed = manager.GetComponentData<ResourceGenerationSettings>(config).WorldSeed;
        BuildNearestChunks(center, seed, settings, biomes, variants);
    }

    private void RefreshCandidates(NativeParallelHashSet<int2> completed, int2 minimum, int2 maximum)
    {
        int count = completed.Count();
        bool rangeChanged = !_hasCandidateRange || !_minimum.Equals(minimum) || !_maximum.Equals(maximum);
        if (!rangeChanged && _completedCount == count)
        {
            return;
        }

        if (rangeChanged)
        {
            ReleaseOutsideRange(minimum, maximum);
        }
        _hasCandidateRange = true;
        _minimum = minimum;
        _maximum = maximum;
        _completedCount = count;
        _candidates.Clear();

        // 완료 집합은 World 수명 동안 좌표가 추가된다. 범위나 완료 수가 바뀔 때만 표시 후보를 다시 읽는다.
        // 큰 시야는 사각형 전체를 돌지 않고 더 작은 완료 집합에서 교집합을 찾는다.
        long area = ((long)maximum.x - minimum.x + 1) * ((long)maximum.y - minimum.y + 1);
        if (area > count)
        {
            foreach (int2 chunk in completed)
            {
                if (IsInside(chunk, minimum, maximum) && !_displays.ContainsKey(chunk))
                {
                    _candidates.Add(chunk);
                }
            }
            return;
        }

        for (int y = minimum.y; y <= maximum.y; y++)
        {
            for (int x = minimum.x; x <= maximum.x; x++)
            {
                var chunk = new int2(x, y);
                if (completed.Contains(chunk) && !_displays.ContainsKey(chunk))
                {
                    _candidates.Add(chunk);
                }
            }
        }
    }

    private bool TryPrepareBuilder(DynamicBuffer<FloorVariantElement> variants)
    {
        var builder = new FloorChunkMeshBuilder();
        try
        {
            if (builder.TryPrepare(_materialTemplate, variants, out string error))
            {
                _builder = builder;
                return true;
            }
            _materialSetupFailed = true;
            Debug.LogError(error, this);
            return false;
        }
        finally
        {
            if (_builder == null)
            {
                builder.Dispose();
            }
        }
    }

    private void BuildNearestChunks(Vector2 center, uint seed, FloorGenerationSettings settings,
        DynamicBuffer<FloorBiomeElement> biomes, DynamicBuffer<FloorVariantElement> variants)
    {
        // 가장 가까운 후보를 목록 끝에서 꺼낼 수 있도록 먼 순서로 정렬한다.
        _candidates.Sort((left, right) =>
        {
            var leftCandidate = new CameraChunkLoadUtility.ChunkCandidate(left, center);
            var rightCandidate = new CameraChunkLoadUtility.ChunkCandidate(right, center);
            return rightCandidate.CompareTo(leftCandidate);
        });
        int count = Math.Min(_candidates.Count, GameConstants.MaxFloorChunkBuildsPerFrame);
        for (int i = 0; i < count; i++)
        {
            int last = _candidates.Count - 1;
            int2 chunk = _candidates[last];
            ChunkUtility.GetChunkBounds(chunk, out int2 minimumCell, out _);
            Mesh mesh = _builder.Build(minimumCell, seed, settings, biomes, variants, out var materials);
            CreateDisplay(chunk, minimumCell, mesh, materials);
            _candidates.RemoveAt(last);
        }
    }

    private void CreateDisplay(int2 chunk, int2 minimumCell, Mesh mesh, Material[] materials)
    {
        GameObject display = null;
        try
        {
            // 카메라 자식으로 두면 이미 만든 청크의 월드 위치까지 이동한다.
            display = new GameObject($"Floor Chunk ({chunk.x}, {chunk.y})") { hideFlags = HideFlags.DontSave };
            display.layer = gameObject.layer;
            display.transform.position = new Vector3(minimumCell.x, minimumCell.y, FloorDepth);
            display.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = display.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = materials;
            renderer.sortingOrder = FloorSortingOrder;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            _displays.Add(chunk, new ChunkDisplay(display, mesh));
        }
        catch
        {
            if (display != null)
            {
                display.SetActive(false);
                FloorChunkMeshBuilder.DestroyOwned(display);
            }
            FloorChunkMeshBuilder.DestroyOwned(mesh);
            throw;
        }
    }

    private static bool IsInside(int2 chunk, int2 minimum, int2 maximum)
    {
        return chunk.x >= minimum.x && chunk.x <= maximum.x && chunk.y >= minimum.y && chunk.y <= maximum.y;
    }

    private void ReleaseOutsideRange(int2 minimum, int2 maximum)
    {
        _outsideRange.Clear();
        foreach (var entry in _displays)
        {
            if (!IsInside(entry.Key, minimum, maximum))
            {
                _outsideRange.Add(entry.Key);
                ReleaseDisplay(entry.Value);
            }
        }
        foreach (int2 chunk in _outsideRange)
        {
            _displays.Remove(chunk);
        }
        _outsideRange.Clear();
    }

    private static void ReleaseDisplay(ChunkDisplay display)
    {
        if (display.Object != null)
        {
            display.Object.SetActive(false);
            FloorChunkMeshBuilder.DestroyOwned(display.Object);
        }
        FloorChunkMeshBuilder.DestroyOwned(display.Mesh);
    }

    private void ClearDisplays()
    {
        foreach (var display in _displays.Values)
        {
            ReleaseDisplay(display);
        }
        _displays.Clear();
        _candidates.Clear();
        _hasCandidateRange = false;
    }

    private void ReleaseWorldResources()
    {
        ClearDisplays();
        _builder?.Dispose();
        _builder = null;
        _materialSetupFailed = false;
        _world = null;
    }

    private void OnDisable()
    {
        ReleaseWorldResources();
    }
}
