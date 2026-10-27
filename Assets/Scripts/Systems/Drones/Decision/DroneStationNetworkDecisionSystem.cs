using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

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
            ConfigLookup = state.GetBufferLookup<BuildingConfigElement>(true),
            TypeLookup = state.GetComponentLookup<BuildingType>(true),
            PositionLookup = state.GetComponentLookup<GridPosition>(true),
            DirectionLookup = state.GetComponentLookup<Direction>(true),
            FootprintLookup = state.GetComponentLookup<BuildingFootprint>(true),
            StampLookup = state.GetComponentLookup<PlacementStamp>(true),
            DecisionLookup = state.GetComponentLookup<DroneStationNetworkDecision>(),
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
        for (int i = 0; i < Stations.Length; i++)
        {
            Entity station = Stations[i];
            var type = TypeLookup[station];
            if (!BuildingConfigLookupUtility.TryGetConfig(configs, type.Type, type.Tier, out var config))
            {
                SimulationFailureUtility.Record(ref ECB, "[DroneStationNetwork] Missing station type/tier configuration.");
                return;
            }
            Parents[i] = i;
            Bounds[i] = DroneStationRangeUtility.GetActivityBounds(PositionLookup[station].Value,
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
                if (CompareStations(Stations[left], Stations[right]) <= 0) Parents[right] = left;
                else Parents[left] = right;
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

    private int CompareStations(Entity left, Entity right)
    {
        bool hasLeft = StampLookup.HasComponent(left);
        bool hasRight = StampLookup.HasComponent(right);
        return PlacementStamp.Compare(hasLeft, hasLeft ? StampLookup[left] : default, PositionLookup[left].Value,
            hasRight, hasRight ? StampLookup[right] : default, PositionLookup[right].Value);
    }
}
