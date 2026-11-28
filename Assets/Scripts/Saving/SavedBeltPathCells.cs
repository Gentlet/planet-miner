using System.Collections.Generic;
using System.IO;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>저장하거나 복원하는 건물 원본으로 입구와 합류기 출력 연결을 계산한다.</summary>
internal readonly struct SavedBeltPathCells : IBeltPathCells
{
    public readonly Dictionary<int2, DirectionEnum> Belts;
    private readonly HashSet<int2> _outputCells;
    private readonly Dictionary<int2, int2> _mergerOutputs;

    public SavedBeltPathCells(Dictionary<int2, DirectionEnum> belts, HashSet<int2> outputCells,
        Dictionary<int2, int2> mergerOutputs = null)
    {
        Belts = belts;
        _outputCells = outputCells;
        _mergerOutputs = mergerOutputs;
    }

    public bool PointsToward(int2 cell, DirectionEnum direction) =>
        Belts.TryGetValue(cell, out var exit) && exit == direction;

    public bool HasBuildingOutput(int2 source, int2 target) =>
        _outputCells?.Contains(source) == true ||
        (_mergerOutputs != null && _mergerOutputs.TryGetValue(source, out var output) && output.Equals(target));

    public static SavedBeltPathCells FromSnapshot(EntityManager manager, GameWorldSnapshot snapshot)
    {
        using var configQuery = manager.CreateEntityQuery(typeof(BuildingConfig), typeof(BuildingConfigElement));
        var configurations = manager.GetBuffer<BuildingConfigElement>(configQuery.GetSingletonEntity(), true);
        var pendingDemolitions = new HashSet<string>();
        if (snapshot.DroneLogistics != null)
        {
            foreach (var row in snapshot.DroneLogistics.Entities)
            {
                if (row.Kind == SavedDroneKindEnum.Building && row.Has<PendingBuildingDemolition>())
                {
                    pendingDemolitions.Add(row.Id);
                }
            }
        }

        var belts = new Dictionary<int2, DirectionEnum>();
        var routingBelts = new SavedRoutingBeltCells { Candidates = new Dictionary<int2, RoutingBeltCandidate>() };
        var beltCellsById = new Dictionary<string, int2>();
        var outputCells = new HashSet<int2>();
        var mergers = new Dictionary<int2, string>();
        foreach (var building in snapshot.Buildings)
        {
            if (building.Disabled)
            {
                continue;
            }
            if (!BuildingConfigLookupUtility.TryGetConfig(configurations, building.BuildingTypeId, out var config))
            {
                throw new InvalidDataException("벨트 연결에 사용하는 건물 정의가 없습니다.");
            }

            int2 origin = GameWorldSnapshot.ReadCell(building.Origin);
            bool pendingDemolition = pendingDemolitions.Contains(building.Id);
            if (config.BeltTransport.IsPresent)
            {
                belts.Add(origin, building.Direction);
                if (!pendingDemolition)
                {
                    beltCellsById.Add(building.Id, origin);
                    routingBelts.Candidates.Add(origin, new RoutingBeltCandidate
                    {
                        Direction = building.Direction,
                        Stamp = new PlacementStamp(building.Stamp.Tick, building.Stamp.Order, building.Stamp.Receipt),
                        HasStamp = true
                    });
                }
            }
            if (HasBuildingOutput(config))
            {
                int2 size = BuildingFootprintUtility.GetEffectiveSize(config.Footprint, building.Direction);
                AddOutputFootprint(outputCells, origin, size);
            }
            if (config.BeltRouting == BeltRoutingMode.Merge && !pendingDemolition)
            {
                mergers.Add(origin, building.Routing?.BeltId);
            }
        }

        var mergerOutputs = new Dictionary<int2, int2>();
        foreach (var entry in mergers)
        {
            int2 preferred = default;
            bool hasPreferred = entry.Value != null && beltCellsById.TryGetValue(entry.Value, out preferred);
            if (BeltPathUtility.TrySelectMergerOutput(entry.Key, hasPreferred, preferred, routingBelts, out var outlet, out _))
            {
                mergerOutputs.Add(entry.Key, outlet);
            }
        }
        return new SavedBeltPathCells(belts, outputCells, mergerOutputs);
    }

    private static bool HasBuildingOutput(in BuildingConfigElement config) =>
        config.Mining.IsPresent || config.Crafting.IsPresent || config.BeltRouting == BeltRoutingMode.Split ||
        config.ItemStorage.IsPresent || config.DroneStation.IsPresent || config.Power.Generation.FuelStorage.IsPresent;

    private static void AddOutputFootprint(HashSet<int2> cells, int2 origin, int2 size)
    {
        for (int y = 0; y < size.y; y++)
        {
            for (int x = 0; x < size.x; x++)
            {
                cells.Add(origin + new int2(x, y));
            }
        }
    }

    private struct SavedRoutingBeltCells : IRoutingBeltCells
    {
        public Dictionary<int2, RoutingBeltCandidate> Candidates;

        public bool TryGetRoutingBelt(int2 cell, out RoutingBeltCandidate candidate) =>
            Candidates.TryGetValue(cell, out candidate);
    }
}
