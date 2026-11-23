using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

/// <summary>공개 정거장 활동 영역과 유효한 배치 후보를 비교해 드론 범위만 표시한다.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera), typeof(WorldConstructionController))]
public sealed class WorldDroneCoverageView : PeriodicWorldView
{
    private static readonly Color CoverageFill = new(0.14f, 0.82f, 0.64f, 0.06f);
    private static readonly Color CoverageEdge = new(0.26f, 0.86f, 0.67f, 0.54f);
    private static readonly Color ProposedFill = new(1f, 0.70f, 0.28f, 0.11f);
    private static readonly Color ProposedEdge = new(1f, 0.76f, 0.33f, 0.90f);
    private static readonly Color CoveredCell = new(0.23f, 1f, 0.75f, 0.34f);
    private static readonly Color UncoveredFootprint = new(1f, 0.58f, 0.32f, 0.19f);

    private readonly List<DroneStationActivityBounds> _stationBounds = new();
    private readonly Dictionary<int, int> _stationRanges = new();
    private readonly List<BuildingToolPreviewItem> _placements = new();
    private readonly Plane[] _viewFrustum = new Plane[6];
    private World _world;
    private WorldConstructionController _tools;
    private Camera _camera;
    private WorldOverlayMesh _mesh;
    private bool _hasStationSnapshot;

    public string Status { get; private set; } = string.Empty;
    public event Action StatusChanged;

    private void OnEnable()
    {
        var shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            Debug.LogError("드론 범위 오버레이 셰이더가 없습니다.", this);
            enabled = false;
            return;
        }

        _tools = GetComponent<WorldConstructionController>();
        _camera = GetComponent<Camera>();
        _mesh = new WorldOverlayMesh(shader, "Drone coverage overlay", 1400);
        _tools.Changed += OnToolChanged;
        Refresh();
    }

    private void OnDisable()
    {
        if (_tools != null)
        {
            _tools.Changed -= OnToolChanged;
        }
        _mesh?.Dispose();
        _mesh = null;
        ResetWorld(null);
        SetStatus(string.Empty);
    }

    public override void Refresh() => UpdateOverlay(refreshStations: true);

    private void OnToolChanged() => UpdateOverlay(refreshStations: false);

    private void UpdateOverlay(bool refreshStations)
    {
        if (_mesh == null)
        {
            return;
        }

        World current = World.DefaultGameObjectInjectionWorld;
        if (!ReferenceEquals(current, _world))
        {
            ResetWorld(current);
        }
        if (!BuildingToolWorld.IsAvailable(_world) || !_tools.isActiveAndEnabled || _tools.Preview.Count == 0)
        {
            _placements.Clear();
            _hasStationSnapshot = false;
            _mesh.Hide();
            SetStatus(string.Empty);
            return;
        }

        bool placementsChanged = CapturePlacements();
        if (refreshStations || !_hasStationSnapshot)
        {
            var manager = _world.EntityManager;
            ReadStationRanges(manager);
            _hasStationSnapshot = TryReadStationBounds(manager);
            if (!_hasStationSnapshot)
            {
                _mesh.Hide();
                SetStatus("드론 범위 확인 대기");
                return;
            }
        }
        else if (!placementsChanged)
        {
            return;
        }

        GeometryUtility.CalculateFrustumPlanes(_camera, _viewFrustum);
        _mesh.Begin();
        DrawExistingBounds();
        DrawPlacements();
        _mesh.Publish();
    }

    private void ResetWorld(World world)
    {
        _world = world;
        _stationRanges.Clear();
        _stationBounds.Clear();
        _placements.Clear();
        _hasStationSnapshot = false;
    }

    private bool CapturePlacements()
    {
        int count = 0;
        bool changed = false;
        foreach (var item in _tools.Preview)
        {
            if (!item.Validation.IsValid)
            {
                continue;
            }
            if (count == _placements.Count)
            {
                _placements.Add(item);
                changed = true;
            }
            else if (!_placements[count].MatchesPlacement(item))
            {
                _placements[count] = item;
                changed = true;
            }
            count++;
        }
        if (count < _placements.Count)
        {
            _placements.RemoveRange(count, _placements.Count - count);
            changed = true;
        }
        return changed;
    }

    private void ReadStationRanges(EntityManager manager)
    {
        if (_stationRanges.Count > 0)
        {
            return;
        }
        using var query = manager.CreateEntityQuery(typeof(BuildingConfig), typeof(BuildingConfigElement));
        if (!query.HasSingleton<BuildingConfig>())
        {
            return;
        }
        foreach (var config in manager.GetBuffer<BuildingConfigElement>(query.GetSingletonEntity(), true))
        {
            if (config.DroneStation.IsPresent)
            {
                _stationRanges.Add(config.BuildingId, config.DroneStation.RangeInChunks);
            }
        }
    }

    private bool TryReadStationBounds(EntityManager manager)
    {
        _stationBounds.Clear();
        using var query = manager.CreateEntityQuery(typeof(DroneStationSpatialIndex), typeof(DroneStationSpatialIndexFence));
        if (!query.HasSingleton<DroneStationSpatialIndex>())
        {
            return false;
        }
        // 공개 맵의 Writer만 기다리고 같은 호출에서 값으로 복사한다. Native 맵을 프레임 사이에 보관하지 않는다.
        query.GetSingleton<DroneStationSpatialIndexFence>().State.GetReaderDependency().Complete();
        var index = query.GetSingleton<DroneStationSpatialIndex>();
        if (!index.Map.IsCreated)
        {
            return false;
        }
        foreach (var entry in index.AsReadOnly())
        {
            _stationBounds.Add(entry.Value.Bounds);
        }
        return true;
    }

    private void DrawExistingBounds()
    {
        foreach (var bounds in _stationBounds)
        {
            WorldOverlayMesh.GetCellBounds(bounds.MinX, bounds.MinY, bounds.MaxX, bounds.MaxY, out var min, out var max);
            if (WorldOverlayMesh.IsVisible(_viewFrustum, min, max))
            {
                _mesh.Fill(min, max, CoverageFill);
                _mesh.Outline(min, max, CoverageEdge);
            }
        }
    }

    private void DrawPlacements()
    {
        int stations = 0;
        int connectedStations = 0;
        int coveredBuildings = 0;
        DroneStationActivityBounds proposedBounds = default;
        foreach (var item in _placements)
        {
            var candidate = item.Candidate;
            if (_stationRanges.TryGetValue(candidate.BuildingId, out int range))
            {
                stations++;
                proposedBounds = DroneStationRangeUtility.GetActivityBounds(candidate.OriginPosition,
                    candidate.FootprintSize, candidate.Direction, range);
                if (DrawProposedStation(proposedBounds))
                {
                    connectedStations++;
                }
            }
            else if (DrawBuildingCoverage(candidate))
            {
                coveredBuildings++;
            }
        }

        int buildings = _placements.Count - stations;
        string stationStatus = string.Empty;
        if (stations == 1)
        {
            long width = proposedBounds.MaxX - proposedBounds.MinX + 1L;
            string network = connectedStations > 0 ? "기존 드론망과 연결 예상" : "독립 드론망 예상";
            stationStatus = $"드론 활동 범위 {width}×{width}칸 / {network}";
        }
        else if (stations > 1)
        {
            stationStatus = $"기존 드론망 직접 연결 예상 {connectedStations}/{stations}";
        }
        string buildingStatus = buildings switch
        {
            0 => string.Empty,
            1 => coveredBuildings > 0 ? "드론 범위 안" : "드론 범위 밖",
            _ => $"드론 범위 안 {coveredBuildings}/{buildings}"
        };
        SetStatus(stations > 0 && buildings > 0 ? stationStatus + " / " + buildingStatus : stationStatus + buildingStatus);
    }

    private bool DrawProposedStation(in DroneStationActivityBounds bounds)
    {
        WorldOverlayMesh.GetCellBounds(bounds.MinX, bounds.MinY, bounds.MaxX, bounds.MaxY, out var min, out var max);
        _mesh.Fill(min, max, ProposedFill);
        _mesh.Outline(min, max, ProposedEdge, dashed: true);
        foreach (var existing in _stationBounds)
        {
            if (existing.Overlaps(bounds))
            {
                return true;
            }
        }
        return false;
    }

    private bool DrawBuildingCoverage(in PlacementRequestCandidateElement candidate)
    {
        var origin = candidate.OriginPosition;
        var size = BuildingFootprintUtility.GetEffectiveSize(candidate.FootprintSize, candidate.Direction);
        var bounds = new DroneStationActivityBounds
        {
            MinX = origin.x,
            MinY = origin.y,
            MaxX = (long)origin.x + size.x - 1,
            MaxY = (long)origin.y + size.y - 1
        };
        bool covered = false;
        foreach (var station in _stationBounds)
        {
            if (station.Overlaps(bounds))
            {
                WorldOverlayMesh.GetCellBounds(Math.Max(station.MinX, bounds.MinX), Math.Max(station.MinY, bounds.MinY),
                    Math.Min(station.MaxX, bounds.MaxX), Math.Min(station.MaxY, bounds.MaxY), out var min, out var max);
                _mesh.Fill(min, max, CoveredCell);
                covered = true;
            }
        }
        if (!covered)
        {
            WorldOverlayMesh.GetCellBounds(bounds.MinX, bounds.MinY, bounds.MaxX, bounds.MaxY, out var min, out var max);
            _mesh.Fill(min, max, UncoveredFootprint);
        }
        return covered;
    }

    private void SetStatus(string status)
    {
        if (Status != status)
        {
            Status = status;
            StatusChanged?.Invoke();
        }
    }
}
