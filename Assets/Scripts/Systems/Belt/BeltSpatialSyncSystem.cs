using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 벨트 건물 엔티티들을 BeltSpatialIndex에 동기화하는 시스템.
/// 
/// [책임]
/// - SynchronizationGroup(Phase 6)에서 실행되어 매 프레임 공간 인덱스를 Clear하고
///   현재 월드에 배치된 유효한 벨트 건물([GridPosition, Direction, BeltComponent])을 일괄 등록합니다.
/// - ISystem 및 [BurstCompile] 기반으로 멀티스레드 병렬 Job(IJobEntity)을 통해 고속 처리합니다.
/// </summary>
[UpdateInGroup(typeof(SynchronizationGroup))]
[BurstCompile]
public partial struct BeltSpatialSyncSystem : ISystem
{
    private EntityQuery _beltQuery;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        var map = new NativeParallelHashMap<int2, BeltInfo>(1024, Allocator.Persistent);

        var singletonEntity = state.EntityManager.CreateEntity();
        state.EntityManager.AddComponentData(singletonEntity, new BeltSpatialIndex
        {
            Map = map
        });

        _beltQuery = SystemAPI.QueryBuilder()
            .WithAll<BeltComponent, GridPosition, Direction>()
            .Build();
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        if (SystemAPI.TryGetSingleton<BeltSpatialIndex>(out var index))
        {
            if (index.Map.IsCreated)
            {
                index.Map.Dispose();
            }
        }
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        ref var index = ref SystemAPI.GetSingletonRW<BeltSpatialIndex>().ValueRW;

        index.Map.Clear();

        int count = _beltQuery.CalculateEntityCount();
        if (count == 0) return;

        if (index.Map.Capacity < count)
        {
            index.Map.Capacity = math.max(1024, count * 2);
        }

        var job = new PopulateBeltSpatialIndexJob
        {
            Writer = index.Map.AsParallelWriter()
        };

        state.Dependency = job.ScheduleParallel(_beltQuery, state.Dependency);
        state.Dependency.Complete();
    }
}

/// <summary>
/// 벨트 건물 엔티티들을 벨트 공간 해시맵에 병렬 등록하는 Burst Job.
/// </summary>
[BurstCompile]
public partial struct PopulateBeltSpatialIndexJob : IJobEntity
{
    public NativeParallelHashMap<int2, BeltInfo>.ParallelWriter Writer;

    public void Execute(Entity entity, in GridPosition pos, in Direction dir, in BeltComponent belt)
    {
        Writer.TryAdd(pos.Value, new BeltInfo(entity, dir.dir, belt.Speed));
    }
}
