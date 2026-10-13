using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

/// <summary>공개 연결만 수집한다. 참조 망의 유효성은 연결 Writer가 보장한다.</summary>
[UpdateInGroup(typeof(SynchronizationGroup))]
[BurstCompile]
public partial struct PowerGridConnectionSyncSystem : ISystem
{
    private EntityQuery _connectionQuery;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        var entity = state.EntityManager.CreateEntity();
        state.EntityManager.AddComponentData(entity, new PowerGridConnectionIndex
        {
            Map = new NativeParallelHashMap<Entity, PowerGridConnection>(1024, Allocator.Persistent)
        });
        state.EntityManager.AddComponentData(entity, new PowerGridConnectionIndexFence());
        _connectionQuery = SystemAPI.QueryBuilder().WithAll<PowerGridConnection>().Build();
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        if (SystemAPI.TryGetSingletonRW<PowerGridConnectionIndexFence>(out var fence))
        {
            fence.ValueRW.Complete();
        }
        if (SystemAPI.TryGetSingleton<PowerGridConnectionIndex>(out var index) && index.Map.IsCreated)
        {
            index.Map.Dispose();
        }
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        ref var fence = ref SystemAPI.GetSingletonRW<PowerGridConnectionIndexFence>().ValueRW;
        var index = SystemAPI.GetSingleton<PowerGridConnectionIndex>();
        int count = _connectionQuery.CalculateEntityCount();
        if (index.Map.Capacity < count)
        {
            fence.Complete();
            index.Map.Capacity = math.max(1024, count * 2);
        }

        var dependency = JobHandle.CombineDependencies(state.Dependency, fence.GetWriterDependency());
        var clearHandle = new ClearPowerGridConnectionIndexJob { Map = index.Map }.Schedule(dependency);
        var populateHandle = new PopulatePowerGridConnectionIndexJob
        {
            Writer = index.Map.AsParallelWriter()
        }.ScheduleParallel(_connectionQuery, clearHandle);
        fence.SetWriter(populateHandle);
        state.Dependency = populateHandle;
    }
}

[BurstCompile]
internal struct ClearPowerGridConnectionIndexJob : IJob
{
    public NativeParallelHashMap<Entity, PowerGridConnection> Map;
    public void Execute() => Map.Clear();
}

[BurstCompile]
internal partial struct PopulatePowerGridConnectionIndexJob : IJobEntity
{
    public NativeParallelHashMap<Entity, PowerGridConnection>.ParallelWriter Writer;

    public void Execute(Entity entity, in PowerGridConnection connection)
    {
        // Query의 공개 값을 수집한다. 미연결 값은 등록하지 않으며 Entity Lookup을 추가하지 않는다.
        if (connection.PowerPole != Entity.Null && connection.Grid != Entity.Null)
        {
            Writer.TryAdd(entity, connection);
        }
    }
}
