using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>이번 틱 준비 결과의 범위만 판정한다. 이전 공개 인덱스나 영속 드론 소속을 만들지 않는다.</summary>
public static class DroneTaskNetworkUtility
{
    internal static void CollectNetworkBoundsIfNeeded(NativeArray<DroneStationNetworkDecision> networks,
        NativeParallelMultiHashMap<Entity, DroneStationActivityBounds> networkBounds)
    {
        if (!networkBounds.IsEmpty)
        {
            return;
        }

        foreach (var station in networks)
        {
            networkBounds.Add(station.RepresentativeStation, station.ActivityBounds);
        }
    }

    internal static NativeHashMap<Entity, FixedBitSet> ReadDemolitionDestinations(EntityManager manager,
        in DroneLogisticsTask task, NativeArray<DroneStationNetworkDecision> networks,
        in StorageCandidatesByNetwork storages, NativeParallelMultiHashMap<Entity, DroneStationActivityBounds> networkBounds,
        bool crossNetwork)
    {
        CollectNetworkBoundsIfNeeded(networks, networkBounds);
        var result = new NativeHashMap<Entity, FixedBitSet>(8, Allocator.Temp);
        using var visited = new NativeHashSet<Entity>(4, Allocator.Temp);
        var targetBounds = DroneTaskNetworkUtility.ReadTargetBounds(manager, task.Target);
        var allItems = new FixedBitSet { Part0 = ulong.MaxValue, Part1 = ulong.MaxValue };
        foreach (var station in networks)
        {
            if (!crossNetwork && !station.ActivityBounds.Overlaps(targetBounds)) continue;
            Entity network = station.RepresentativeStation;
            if (!visited.Add(network)) continue;
            foreach (Entity storage in storages.GetStorages(network, networkBounds.AsReadOnly()))
            {
                if (storage != task.Target && DroneSchedulingUtility.IsStorage(manager, storage) &&
                    !manager.HasComponent<PreparingStationDemolition>(storage))
                    result.TryAdd(storage, allItems);
            }
        }
        return result;
    }

    public static Entity FindWorkerNetwork(NativeArray<DroneStationNetworkDecision> stations, float3 position)
    {
        int2 cell = (int2)math.floor(position.xy);
        for (int i = 0; i < stations.Length; i++)
        {
            if (stations[i].ActivityBounds.Contains(cell))
            {
                return stations[i].RepresentativeStation;
            }
        }
        return Entity.Null;
    }

    public static bool IsCellInNetwork(NativeArray<DroneStationNetworkDecision> stations, Entity network, int2 position)
    {
        for (int i = 0; i < stations.Length; i++)
        {
            if (stations[i].RepresentativeStation == network && stations[i].ActivityBounds.Contains(position))
            {
                return true;
            }
        }
        return false;
    }

    public static bool IsTargetInNetwork(EntityManager manager, NativeArray<DroneStationNetworkDecision> stations,
        Entity network, Entity target)
    {
        var bounds = ReadTargetBounds(manager, target);
        for (int i = 0; i < stations.Length; i++)
        {
            if (stations[i].RepresentativeStation == network && stations[i].ActivityBounds.Overlaps(bounds))
            {
                return true;
            }
        }
        return false;
    }

    internal static DroneStationActivityBounds ReadTargetBounds(EntityManager manager, Entity target)
    {
        int2 position = manager.GetComponentData<GridPosition>(target).Value;
        int2 size = new int2(1, 1);
        if (manager.HasComponent<BuildingIdentity>(target) || manager.HasComponent<ConstructionSite>(target))
        {
            size = BuildingFootprintUtility.GetEffectiveSize(manager.GetComponentData<BuildingFootprint>(target).Size,
                manager.GetComponentData<Direction>(target).dir);
        }
        return new DroneStationActivityBounds
        {
            MinX = position.x,
            MinY = position.y,
            MaxX = (long)position.x + size.x - 1,
            MaxY = (long)position.y + size.y - 1
        };
    }
}

// 이번 호출에서 읽은 망과 창고의 회전 범위만 사용한다. 창고는 겹치는 각 망의 목록에 독립적으로 들어간다.
internal readonly struct StorageCandidatesByNetwork : IDisposable
{
    private readonly EntityManager _manager;
    private readonly bool _requireStorageRole;
    private readonly NativeArray<Entity> _storages;
    private readonly NativeList<StorageFootprint> _storageFootprints;
    private readonly NativeList<int> _matchingIndexes;
    private readonly NativeList<Entity> _candidates;
    private readonly NativeHashMap<Entity, int2> _ranges;

    public StorageCandidatesByNetwork(EntityManager manager, NativeArray<Entity> storages, bool requireStorageRole = false)
    {
        _manager = manager;
        _requireStorageRole = requireStorageRole;
        _storages = storages;
        _storageFootprints = new NativeList<StorageFootprint>(Allocator.Temp);
        _matchingIndexes = new NativeList<int>(Allocator.Temp);
        _candidates = new NativeList<Entity>(Allocator.Temp);
        _ranges = new NativeHashMap<Entity, int2>(1, Allocator.Temp);
    }

    // 반환한 목록은 다른 망의 후보를 추가하기 전에 순회한다.
    public NativeArray<Entity> GetStorages(Entity network,
        NativeParallelMultiHashMap<Entity, DroneStationActivityBounds>.ReadOnly networkBounds)
    {
        if (!_ranges.TryGetValue(network, out int2 range))
        {
            int start = _candidates.Length;
            AppendCandidates(network, networkBounds);
            range = new int2(start, _candidates.Length - start);
            _ranges.Add(network, range);
        }
        return _candidates.AsArray().GetSubArray(range.x, range.y);
    }

    private void AppendCandidates(Entity network,
        NativeParallelMultiHashMap<Entity, DroneStationActivityBounds>.ReadOnly networkBounds)
    {
        if (_ranges.IsEmpty)
        {
            for (int i = 0; i < _storages.Length; i++)
            {
                Entity storage = _storages[i];
                if (_requireStorageRole && !DroneSchedulingUtility.IsStorage(_manager, storage))
                {
                    continue;
                }
                _storageFootprints.Add(new StorageFootprint
                {
                    QueryIndex = i,
                    Bounds = DroneTaskNetworkUtility.ReadTargetBounds(_manager, storage)
                });
            }
            var footprints = _storageFootprints.AsArray();
            footprints.Sort();
            long maxX = long.MinValue;
            for (int i = 0; i < footprints.Length; i++)
            {
                var footprint = footprints[i];
                maxX = math.max(maxX, footprint.Bounds.MaxX);
                footprint.MaxXThroughIndex = maxX;
                footprints[i] = footprint;
            }
        }

        if (!networkBounds.TryGetFirstValue(network, out var firstBounds, out var firstIterator))
        {
            return;
        }
        long minNetworkX = firstBounds.MinX;
        long maxNetworkX = firstBounds.MaxX;
        var boundsIterator = firstIterator;
        while (networkBounds.TryGetNextValue(out var bounds, ref boundsIterator))
        {
            minNetworkX = math.min(minNetworkX, bounds.MinX);
            maxNetworkX = math.max(maxNetworkX, bounds.MaxX);
        }

        _matchingIndexes.Clear();
        for (int i = FindFirstStorageReaching(minNetworkX); i < _storageFootprints.Length; i++)
        {
            var footprint = _storageFootprints[i];
            if (footprint.Bounds.MinX > maxNetworkX)
            {
                break;
            }
            var stationBounds = firstBounds;
            boundsIterator = firstIterator;
            do
            {
                if (stationBounds.Overlaps(footprint.Bounds))
                {
                    _matchingIndexes.Add(footprint.QueryIndex);
                    break;
                }
            }
            while (networkBounds.TryGetNextValue(out stationBounds, ref boundsIterator));
        }

        // 위치 기준으로 줄인 뒤 기존 창고 쿼리 순서로 후보를 반환한다.
        _matchingIndexes.AsArray().Sort();
        foreach (int index in _matchingIndexes)
        {
            _candidates.Add(_storages[index]);
        }
    }

    private int FindFirstStorageReaching(long minX)
    {
        // X 시작점 순서만 보면 앞쪽의 폭이 넓은 창고를 놓칠 수 있다.
        int first = 0;
        int end = _storageFootprints.Length;
        while (first < end)
        {
            int middle = first + (end - first) / 2;
            if (_storageFootprints[middle].MaxXThroughIndex < minX)
            {
                first = middle + 1;
            }
            else
            {
                end = middle;
            }
        }
        return first;
    }

    public void Dispose()
    {
        _ranges.Dispose();
        _candidates.Dispose();
        _matchingIndexes.Dispose();
        _storageFootprints.Dispose();
    }

    private struct StorageFootprint : IComparable<StorageFootprint>
    {
        public int QueryIndex;
        public DroneStationActivityBounds Bounds;
        public long MaxXThroughIndex;

        public int CompareTo(StorageFootprint other) => Bounds.MinX.CompareTo(other.Bounds.MinX);
    }
}
