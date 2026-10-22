using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

/// <summary>최종 공개 연결과 건물 원본으로 정거장별 범위 맵을 재구축한다. 망 식별/범위의 별도 원본은 만들지 않는다.</summary>
[UpdateInGroup(typeof(SynchronizationGroup))]
[BurstCompile]
public partial struct DroneStationSpatialSyncSystem : ISystem
{
    private EntityQuery _stations;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        var entity = state.EntityManager.CreateEntity();
        state.EntityManager.AddComponentData(entity, new DroneStationSpatialIndex
        {
            Map = new NativeParallelHashMap<Entity, DroneStationCoverage>(16, Allocator.Persistent)
        });
        state.EntityManager.AddComponent<DroneStationSpatialIndexFence>(entity);
        _stations = SystemAPI.QueryBuilder().WithAll<DroneStation, DroneStationNetwork, BuildingType, GridPosition, Direction>()
            .WithAll<BuildingFootprint>().WithNone<PendingBuildingDemolition>().Build();
        state.RequireForUpdate<BuildingConfig>();
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        if (SystemAPI.TryGetSingletonRW<DroneStationSpatialIndexFence>(out var fence)) fence.ValueRW.Complete();
        if (SystemAPI.TryGetSingleton<DroneStationSpatialIndex>(out var index) && index.Map.IsCreated) index.Map.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        ref var fence = ref SystemAPI.GetSingletonRW<DroneStationSpatialIndexFence>().ValueRW;
        var index = SystemAPI.GetSingleton<DroneStationSpatialIndex>();
        int count = _stations.CalculateEntityCount();
        if (index.Map.Capacity < count)
        {
            fence.Complete();
            index.Map.Capacity = math.max(16, count * 2);
        }
        var dependency = JobHandle.CombineDependencies(state.Dependency, fence.GetWriterDependency());
        var clear = new ClearDroneStationSpatialIndexJob { Map = index.Map }.Schedule(dependency);
        var populate = new PopulateDroneStationSpatialIndexJob
        {
            Config = SystemAPI.GetSingletonEntity<BuildingConfig>(),
            ConfigLookup = state.GetBufferLookup<BuildingConfigElement>(true),
            Writer = index.Map.AsParallelWriter()
        }.ScheduleParallel(_stations, clear);
        fence.SetWriter(populate);
        state.Dependency = populate;
    }
}

[BurstCompile]
internal struct ClearDroneStationSpatialIndexJob : IJob
{
    public NativeParallelHashMap<Entity, DroneStationCoverage> Map;
    public void Execute() => Map.Clear();
}

[BurstCompile]
internal partial struct PopulateDroneStationSpatialIndexJob : IJobEntity
{
    public Entity Config;
    [ReadOnly] public BufferLookup<BuildingConfigElement> ConfigLookup;
    public NativeParallelHashMap<Entity, DroneStationCoverage>.ParallelWriter Writer;

    public void Execute(Entity entity, in DroneStationNetwork network, in BuildingType type, in GridPosition position,
        in Direction direction, in BuildingFootprint footprint)
    {
        if (network.RepresentativeStation == Entity.Null) return;
        // 연결 Writer 이후 설정/위치/크기는 바뀌지 않는다. 같은 타입·티어 조회 성공은 Decision의 게시 계약이다.
        BuildingConfigLookupUtility.TryGetConfig(ConfigLookup[Config], type.Type, type.Tier, out var config);
        Writer.TryAdd(entity, new DroneStationCoverage
        {
            RepresentativeStation = network.RepresentativeStation,
            Bounds = DroneStationRangeUtility.GetActivityBounds(position.Value, footprint.Size, direction.dir,
                config.DroneStationRangeInChunks)
        });
    }
}
