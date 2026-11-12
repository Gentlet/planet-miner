using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

/// <summary>
/// EndBuilding 이후 원본 정거장의 활동 영역으로 연결 성분과 대표를 판단한다.
/// 같은 틱 판단만 작성하며 공개 연결·배정·예약·전력은 변경하지 않는다.
/// </summary>
[UpdateInGroup(typeof(DroneDecisionGroup), OrderFirst = true)]
[UpdateBefore(typeof(DroneTaskDecisionSystem))]
[BurstCompile]
public partial struct DroneStationNetworkDecisionSystem : ISystem
{
    private EntityQuery _allStations;
    private EntityQuery _activeStations;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        _allStations = SystemAPI.QueryBuilder().WithAll<DroneStation, DroneStationNetworkDecision>()
            .WithOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IgnoreComponentEnabledState).Build();
        _activeStations = SystemAPI.QueryBuilder().WithAll<DroneStation, BuildingType, GridPosition, Direction, BuildingFootprint>()
            .WithAll<DroneStationNetworkDecision>().WithNone<PendingBuildingDemolition>()
            .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).Build();
        state.RequireForUpdate<BuildingConfig>();
    }

    public void OnUpdate(ref SystemState state)
    {
        var reset = new ResetDroneStationNetworkDecisionJob().ScheduleParallel(_allStations, state.Dependency);
        var stations = _activeStations.ToEntityListAsync(Allocator.TempJob, reset, out var entitiesReady);
        var bounds = new NativeList<DroneStationActivityBounds>(Allocator.TempJob);
        var parents = new NativeList<int>(Allocator.TempJob);
        var ecbSystem = state.World.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>();
        var handle = new DecideDroneStationNetworkJob
        {
            Stations = stations.AsDeferredJobArray(), Bounds = bounds, Parents = parents,
            Config = SystemAPI.GetSingletonEntity<BuildingConfig>(),
            ConfigLookup = SystemAPI.GetBufferLookup<BuildingConfigElement>(true),
            TypeLookup = SystemAPI.GetComponentLookup<BuildingType>(true),
            PositionLookup = SystemAPI.GetComponentLookup<GridPosition>(true),
            DirectionLookup = SystemAPI.GetComponentLookup<Direction>(true),
            FootprintLookup = SystemAPI.GetComponentLookup<BuildingFootprint>(true),
            StampLookup = SystemAPI.GetComponentLookup<PlacementStamp>(true),
            DecisionLookup = SystemAPI.GetComponentLookup<DroneStationNetworkDecision>(),
            ECB = ecbSystem.CreateCommandBuffer()
        }.Schedule(entitiesReady);
        state.Dependency = JobHandle.CombineDependencies(stations.Dispose(handle), bounds.Dispose(handle), parents.Dispose(handle));
        ecbSystem.AddJobHandleForProducer(handle);
    }
}

[BurstCompile]
internal partial struct ResetDroneStationNetworkDecisionJob : IJobEntity
{
    public void Execute(ref DroneStationNetworkDecision decision, EnabledRefRW<DroneStationNetworkDecision> enabled)
    {
        decision = default;
        enabled.ValueRW = false;
    }
}

/// <summary>활동 영역의 겹침으로 연결 성분을 구한다. 경합하는 union/대표 변경은 단일 Job이 소유한다.</summary>
[BurstCompile]
internal struct DecideDroneStationNetworkJob : IJob
{
    [ReadOnly] public NativeArray<Entity> Stations;
    public NativeList<DroneStationActivityBounds> Bounds;
    public NativeList<int> Parents;
    public Entity Config;
    [ReadOnly] public BufferLookup<BuildingConfigElement> ConfigLookup;
    [ReadOnly] public ComponentLookup<BuildingType> TypeLookup;
    [ReadOnly] public ComponentLookup<GridPosition> PositionLookup;
    [ReadOnly] public ComponentLookup<Direction> DirectionLookup;
    [ReadOnly] public ComponentLookup<BuildingFootprint> FootprintLookup;
    [ReadOnly] public ComponentLookup<PlacementStamp> StampLookup;
    public ComponentLookup<DroneStationNetworkDecision> DecisionLookup;
    public EntityCommandBuffer ECB;

    public void Execute()
    {
        Bounds.ResizeUninitialized(Stations.Length);
        Parents.ResizeUninitialized(Stations.Length);
        var configs = ConfigLookup[Config];
        StationComparisonKey leftRootKey = default;
        for (int i = 0; i < Stations.Length; i++)
        {
            Entity station = Stations[i];
            var type = TypeLookup[station];
            if (!BuildingConfigLookupUtility.TryGetConfig(configs, type.BuildingId, out var config))
            {
                FixedString128Bytes message = "[DroneStationNetwork] Missing station configuration for BuildingId ";
                message.Append(type.BuildingId);
                SimulationFailureUtility.Record(ref ECB, message);
                return;
            }
            Parents[i] = i;
            int2 position = PositionLookup[station].Value;
            Bounds[i] = DroneStationRangeUtility.GetActivityBounds(position,
                FootprintLookup[station].Size, DirectionLookup[station].dir, config.DroneStationRangeInChunks);
        }
        for (int i = 0; i < Stations.Length; i++)
        {
            for (int j = i + 1; j < Stations.Length; j++)
            {
                if (!Bounds[i].Overlaps(Bounds[j])) continue;
                int left = FindRoot(i);
                int right = FindRoot(j);
                if (left == right) continue;
                if (CompareStations(Stations[left], Stations[right], ref leftRootKey) <= 0)
                {
                    Parents[right] = left;
                }
                else
                {
                    Parents[left] = right;
                }
            }
        }
        for (int i = 0; i < Stations.Length; i++)
        {
            Entity station = Stations[i];
            DecisionLookup[station] = new DroneStationNetworkDecision
            {
                RepresentativeStation = Stations[FindRoot(i)],
                ActivityBounds = Bounds[i]
            };
            DecisionLookup.SetComponentEnabled(station, true);
        }
    }

    private int FindRoot(int index)
    {
        while (Parents[index] != index)
        {
            Parents[index] = Parents[Parents[index]];
            index = Parents[index];
        }
        return index;
    }

    private struct StationComparisonKey
    {
        public Entity Station;
        public int2 Position;
        public bool HasStamp;
        public PlacementStamp Stamp;
    }

    private int CompareStations(Entity left, Entity right, ref StationComparisonKey leftRootKey)
    {
        if (leftRootKey.Station != left)
        {
            leftRootKey.Station = left;
            leftRootKey.HasStamp = StampLookup.TryGetComponent(left, out leftRootKey.Stamp);
            leftRootKey.Position = PositionLookup[left].Value;
        }
        bool hasRight = StampLookup.TryGetComponent(right, out var rightStamp);
        return PlacementStamp.Compare(leftRootKey.HasStamp, leftRootKey.Stamp, leftRootKey.Position,
            hasRight, rightStamp, PositionLookup[right].Value);
    }
}
