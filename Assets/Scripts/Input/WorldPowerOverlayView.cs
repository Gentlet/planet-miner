using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

/// <summary>배치 후보의 연결 예상과 선택 건물의 공개 전력 상태를 월드에 표시한다.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera), typeof(WorldConstructionController), typeof(WorldSelectionController))]
public sealed class WorldPowerOverlayView : PeriodicWorldView
{
    private static readonly Color CoverageFill = new(0.18f, 0.55f, 1f, 0.10f);
    private static readonly Color CoverageEdge = new(0.24f, 0.65f, 1f, 0.36f);
    private static readonly Color FocusedFill = new(0.18f, 0.55f, 1f, 0.20f);
    private static readonly Color FocusedEdge = new(0.42f, 0.77f, 1f, 0.9f);
    private static readonly Color ConnectionRange = new(1f, 0.69f, 0.28f, 0.86f);
    private static readonly Color Connected = new(0.48f, 1f, 0.78f, 0.95f);
    private static readonly Color OverlapFill = new(0.48f, 1f, 0.78f, 0.32f);
    private static readonly Color Disconnected = new(1f, 0.47f, 0.38f, 0.90f);

    // 전력망의 원본은 ECS에 있다. 이 읽기 사본은 다음 표시 갱신까지만 사용한다.
    private struct Pole
    {
        public Entity Entity;
        public Entity Grid;
        public int2 Position;
        public BuildingPowerGridConfig Power;
        public PlacementStamp Stamp;
        public bool HasStamp;
    }

    private readonly Dictionary<int, BuildingConfigElement> _definitions = new();
    private readonly List<Pole> _poles = new();
    private readonly List<BuildingToolPreviewItem> _placements = new();
    private readonly Plane[] _viewFrustum = new Plane[6];
    private WorldConstructionController _tools;
    private WorldSelectionController _selection;
    private Camera _camera;
    private World _world;
    private WorldOverlayMesh _mesh;
    private bool _hasPoleSnapshot;
    private bool _hasTool;

    public string Status { get; private set; } = string.Empty;
    public event Action StatusChanged;

    private void OnEnable()
    {
        var shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            Debug.LogError("전력 오버레이 셰이더가 없습니다.", this);
            enabled = false;
            return;
        }

        _tools = GetComponent<WorldConstructionController>();
        _selection = GetComponent<WorldSelectionController>();
        _camera = GetComponent<Camera>();
        _mesh = new WorldOverlayMesh(shader, "Power overlay", 1500);
        _tools.Changed += OnToolChanged;
        _selection.Changed += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        if (_tools != null)
        {
            _tools.Changed -= OnToolChanged;
        }
        if (_selection != null)
        {
            _selection.Changed -= Refresh;
        }
        _mesh?.Dispose();
        _mesh = null;
        ResetSnapshot(null);
        SetStatus(string.Empty);
    }

    public override void Refresh() => UpdateOverlay(refreshPoles: true);

    private void OnToolChanged() => UpdateOverlay(refreshPoles: false);

    private void UpdateOverlay(bool refreshPoles)
    {
        if (_mesh == null)
        {
            return;
        }
        World current = World.DefaultGameObjectInjectionWorld;
        if (!ReferenceEquals(current, _world))
        {
            ResetSnapshot(current);
            refreshPoles = true;
        }
        if (!BuildingToolWorld.IsAvailable(_world))
        {
            Hide();
            return;
        }

        bool hasTool = _tools.isActiveAndEnabled && _tools.Mode != BuildingToolModeEnum.None;
        bool toolChanged = hasTool != _hasTool;
        _hasTool = hasTool;
        var manager = _world.EntityManager;
        if (hasTool)
        {
            if (_tools.Preview.Count == 0)
            {
                _placements.Clear();
                Hide();
                return;
            }
            ReadDefinitions(manager);
            bool placementsChanged = CapturePlacements();
            if (_placements.Count == 0)
            {
                Hide();
                return;
            }
            if (!refreshPoles && !placementsChanged && !toolChanged)
            {
                return;
            }
            PrepareGeometry(manager, refreshPoles || toolChanged);
            DrawPlacement();
        }
        else
        {
            _placements.Clear();
            if (!refreshPoles && !toolChanged)
            {
                return;
            }
            Entity selected = _selection.Selected;
            if (!ReferenceEquals(_selection.SelectedWorld, _world) || !IsPowerBuilding(manager, selected))
            {
                Hide();
                return;
            }
            ReadDefinitions(manager);
            PrepareGeometry(manager, refreshPoles || toolChanged);
            DrawSelection(manager, selected);
        }
        _mesh.Publish();
    }

    private void ResetSnapshot(World world)
    {
        _world = world;
        _definitions.Clear();
        _placements.Clear();
        _poles.Clear();
        _hasPoleSnapshot = false;
        _hasTool = false;
    }

    private void ReadDefinitions(EntityManager manager)
    {
        if (_definitions.Count > 0)
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
            if (config.Power.Grid.IsPresent || config.DroneCharging ||
                config.Power.Generation.MaximumGeneration > 0f || config.Power.Work.MaximumConsumption > 0f)
            {
                _definitions.Add(config.BuildingId, config);
            }
        }
    }

    private bool CapturePlacements()
    {
        int count = 0;
        bool changed = false;
        foreach (var item in _tools.Preview)
        {
            var candidate = item.Candidate;
            if (!_definitions.ContainsKey(candidate.BuildingId))
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

    private void PrepareGeometry(EntityManager manager, bool refreshPoles)
    {
        if (refreshPoles || !_hasPoleSnapshot)
        {
            ReadPoles(manager);
        }
        GeometryUtility.CalculateFrustumPlanes(_camera, _viewFrustum);
        _mesh.Begin();
    }

    private void ReadPoles(EntityManager manager)
    {
        _poles.Clear();
        using var query = manager.CreateEntityQuery(new EntityQueryDesc
        {
            All = new[] { ComponentType.ReadOnly<PowerPole>(), ComponentType.ReadOnly<BuildingIdentity>(),
                ComponentType.ReadOnly<GridPosition>(), ComponentType.ReadOnly<PowerGridConnection>() },
            None = new[] { ComponentType.ReadOnly<PendingBuildingDemolition>() }
        });
        using var entities = query.ToEntityArray(Allocator.Temp);
        foreach (Entity entity in entities)
        {
            int buildingId = manager.GetComponentData<BuildingIdentity>(entity).BuildingId;
            if (!_definitions.TryGetValue(buildingId, out var config))
            {
                continue;
            }
            bool hasStamp = manager.HasComponent<PlacementStamp>(entity);
            _poles.Add(new Pole
            {
                Entity = entity,
                Grid = manager.GetComponentData<PowerGridConnection>(entity).Grid,
                Position = manager.GetComponentData<GridPosition>(entity).Value,
                Power = config.Power.Grid,
                HasStamp = hasStamp,
                Stamp = hasStamp ? manager.GetComponentData<PlacementStamp>(entity) : default
            });
        }
        _hasPoleSnapshot = true;
    }

    private void DrawPlacement()
    {
        DrawExistingRanges(Entity.Null, Entity.Null);
        int valid = 0;
        int connected = 0;
        bool placesPole = false;
        foreach (var item in _placements)
        {
            if (!item.Validation.IsValid)
            {
                continue;
            }
            var candidate = item.Candidate;
            var config = _definitions[candidate.BuildingId];
            valid++;
            if (config.Power.Grid.IsPresent)
            {
                placesPole = true;
                if (DrawProposedPole(candidate.OriginPosition, config.Power.Grid))
                {
                    connected++;
                }
            }
            else if (DrawProposedParticipant(candidate))
            {
                connected++;
            }
        }

        if (valid == 0)
        {
            SetStatus(string.Empty);
        }
        else if (valid > 1)
        {
            SetStatus($"현재 전신주 기준 연결 예상 {connected}/{valid}");
        }
        else if (connected > 0)
        {
            SetStatus("현재 전신주에 연결 가능");
        }
        else
        {
            SetStatus(placesPole ? "기존 전신주 연결 범위 밖 / 새 전력망 예상" : "전력 공급 범위 밖");
        }
    }

    private bool DrawProposedPole(int2 position, BuildingPowerGridConfig power)
    {
        DrawSupplyRange(position, power.SupplyRange, focused: true);
        DrawConnectionRange(position, power.ConnectionRange);
        bool connected = false;
        foreach (var pole in _poles)
        {
            if (PowerRangeUtility.AreCentersConnected(position, power.ConnectionRange, pole.Position, pole.Power.ConnectionRange))
            {
                _mesh.Line(Cell(position), Cell(pole.Position), Connected, dashed: true);
                connected = true;
            }
        }
        return connected;
    }

    private bool DrawProposedParticipant(in PlacementRequestCandidateElement candidate)
    {
        int2 origin = candidate.OriginPosition;
        int2 size = BuildingFootprintUtility.GetEffectiveSize(candidate.FootprintSize, candidate.Direction);
        GetFootprintBounds(origin, size, out var min, out var max);
        if (!TryFindSupplyPole(origin, size, out var pole))
        {
            _mesh.Outline(min, max, Disconnected, dashed: true);
            return false;
        }
        GetRangeBounds(pole.Position, pole.Power.SupplyRange, out var supplyMin, out var supplyMax);
        _mesh.Fill(Vector2.Max(min, supplyMin), Vector2.Min(max, supplyMax), OverlapFill);
        _mesh.Line((min + max) * 0.5f, Cell(pole.Position), Connected, dashed: true);
        return true;
    }

    private bool TryFindSupplyPole(int2 origin, int2 size, out Pole selected)
    {
        selected = default;
        long bestDistance = long.MaxValue;
        foreach (var pole in _poles)
        {
            if (!PowerRangeUtility.TryGetFootprintDistanceSquared(pole.Position, pole.Power.SupplyRange, origin, size, out long distance))
            {
                continue;
            }
            if (selected.Entity != Entity.Null && (distance > bestDistance || distance == bestDistance &&
                PlacementStamp.Compare(pole.HasStamp, pole.Stamp, pole.Position, selected.HasStamp, selected.Stamp, selected.Position) >= 0))
            {
                continue;
            }
            selected = pole;
            bestDistance = distance;
        }
        return selected.Entity != Entity.Null;
    }

    private static bool IsPowerBuilding(EntityManager manager, Entity entity)
    {
        return DroneSchedulingUtility.IsActiveEntity(manager, entity) &&
            !manager.HasComponent<PendingBuildingDemolition>(entity) &&
            manager.HasComponent<PowerGridConnection>(entity) &&
            (manager.HasComponent<PowerPole>(entity) || manager.HasComponent<PowerGenerator>(entity) || manager.HasComponent<PowerConsumer>(entity));
    }

    private void DrawSelection(EntityManager manager, Entity selected)
    {
        var connection = manager.GetComponentData<PowerGridConnection>(selected);
        if (manager.HasComponent<PowerPole>(selected))
        {
            DrawSelectedPole(selected, connection.Grid);
            return;
        }

        Pole pole = default;
        bool connected = connection.Grid != Entity.Null && TryGetPole(connection.PowerPole, connection.Grid, out pole);
        DrawExistingRanges(connected ? connection.PowerPole : Entity.Null, connected ? connection.Grid : Entity.Null);
        if (!connected)
        {
            SetStatus("전력망 미연결");
            return;
        }
        int2 origin = manager.GetComponentData<GridPosition>(selected).Value;
        var footprint = manager.GetComponentData<BuildingFootprint>(selected);
        var direction = manager.GetComponentData<Direction>(selected);
        GetFootprintBounds(origin, footprint.GetEffectiveSize(direction.dir), out var min, out var max);
        _mesh.Line((min + max) * 0.5f, Cell(pole.Position), Connected);
        SetStatus("전력망 연결");
    }

    private bool TryGetPole(Entity entity, Entity grid, out Pole result)
    {
        foreach (var pole in _poles)
        {
            if (pole.Entity == entity && pole.Grid == grid)
            {
                result = pole;
                return true;
            }
        }
        result = default;
        return false;
    }

    private void DrawSelectedPole(Entity entity, Entity grid)
    {
        if (!TryGetPole(entity, grid, out var selected))
        {
            SetStatus("전력망 미연결");
            return;
        }
        DrawExistingRanges(entity, grid);
        DrawConnectionRange(selected.Position, selected.Power.ConnectionRange);
        if (grid != Entity.Null)
        {
            foreach (var other in _poles)
            {
                if (other.Entity != entity && other.Grid == grid &&
                    PowerRangeUtility.AreCentersConnected(selected.Position, selected.Power.ConnectionRange,
                        other.Position, other.Power.ConnectionRange))
                {
                    _mesh.Line(Cell(selected.Position), Cell(other.Position), Connected);
                }
            }
        }
        long supplyWidth = 2L * selected.Power.SupplyRange + 1;
        long connectionWidth = 2L * selected.Power.ConnectionRange + 1;
        SetStatus($"공급 {supplyWidth}×{supplyWidth}칸 / 연결 {connectionWidth}×{connectionWidth}칸");
    }

    private void DrawExistingRanges(Entity focus, Entity grid)
    {
        foreach (var pole in _poles)
        {
            if (grid == Entity.Null || pole.Grid == grid)
            {
                DrawSupplyRange(pole.Position, pole.Power.SupplyRange, pole.Entity == focus);
            }
        }
    }

    private void DrawSupplyRange(int2 position, int range, bool focused)
    {
        GetRangeBounds(position, range, out var min, out var max);
        if (!WorldOverlayMesh.IsVisible(_viewFrustum, min, max))
        {
            return;
        }
        _mesh.Fill(min, max, focused ? FocusedFill : CoverageFill);
        _mesh.Outline(min, max, focused ? FocusedEdge : CoverageEdge);
    }

    private void DrawConnectionRange(int2 position, int range)
    {
        GetRangeBounds(position, range, out var min, out var max);
        _mesh.Outline(min, max, ConnectionRange, dashed: true);
    }

    private static Vector2 Cell(int2 cell) => new(cell.x, cell.y);

    private static void GetRangeBounds(int2 center, int range, out Vector2 min, out Vector2 max)
    {
        WorldOverlayMesh.GetCellBounds((long)center.x - range, (long)center.y - range,
            (long)center.x + range, (long)center.y + range, out min, out max);
    }

    private static void GetFootprintBounds(int2 origin, int2 size, out Vector2 min, out Vector2 max)
    {
        WorldOverlayMesh.GetCellBounds(origin.x, origin.y, (long)origin.x + size.x - 1,
            (long)origin.y + size.y - 1, out min, out max);
    }

    private void Hide()
    {
        _mesh.Hide();
        SetStatus(string.Empty);
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
