using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

/// <summary>
/// 벨트 건물 엔티티들을 BeltSpatialIndex에 동기화하는 시스템.
/// 
/// [책임]
/// - SynchronizationGroup(Phase 6)에서 실행되어 비동기 잡 체인으로 공간 인덱스를 Clear하고
///   현재 월드에 배치된 유효한 벨트 건물([GridPosition, Direction, BeltComponent])을 일괄 등록합니다.
/// - BeltSpatialIndexFence를 통해 이전 Phase의 Reader 잡들이 모두 완료된 후 쓰기를 시작하며,
///   Map.Capacity 확장과 같은 메인 스레드 재할당 시에만 제한적으로 Complete()를 호출합니다.
/// - ISystem 및 [BurstCompile] 기반으로 멀티스레드 병렬 Job(IJob, IJobEntity)을 통해 비차단(Non-blocking) 고속 처리합니다.
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
        state.EntityManager.AddComponentData(singletonEntity, new BeltSpatialIndexFence());

        _beltQuery = SystemAPI.QueryBuilder()
            .WithAll<BeltComponent, GridPosition, Direction>()
            .Build();
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        if (SystemAPI.TryGetSingletonRW<BeltSpatialIndexFence>(out var fence))
        {
            fence.ValueRW.Complete();
        }

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
        if (!SystemAPI.HasSingleton<BeltSpatialIndex>() || !SystemAPI.HasSingleton<BeltSpatialIndexFence>())
        {
            return;
        }

        ref var fence = ref SystemAPI.GetSingletonRW<BeltSpatialIndexFence>().ValueRW;
        var index = SystemAPI.GetSingleton<BeltSpatialIndex>();

        int count = _beltQuery.CalculateEntityCount();
        if (count == 0)
        {
            // 벨트 건물이 없어도 맵은 비워져야 함 (비동기 ClearJob)
            var clearDep = fence.GetWriterDependency();
            var clearOnlyJob = new ClearBeltSpatialIndexJob
            {
                Map = index.Map
            };
            var clearOnlyHandle = clearOnlyJob.Schedule(clearDep);
            fence.SetWriter(clearOnlyHandle);
            state.Dependency = clearOnlyHandle;
            return;
        }

        // Capacity 변경처럼 Main Thread 직접 접근이 필요한 경우에만 제한적으로 Complete
        if (index.Map.Capacity < count)
        {
            fence.Complete();
            index.Map.Capacity = math.max(1024, count * 2);
        }

        // Writer 의존성: 마지막 Writer와 이전 모든 Readers 완료 대기
        var writerDep = fence.GetWriterDependency();

        // 1. ClearJob 비동기 스케줄링
        var clearJob = new ClearBeltSpatialIndexJob
        {
            Map = index.Map
        };
        var clearHandle = clearJob.Schedule(writerDep);

        // 2. PopulateJob 스케줄링 (Clear 완료 및 state.Dependency 결합)
        var populateDep = JobHandle.CombineDependencies(clearHandle, state.Dependency);
        var populateJob = new PopulateBeltSpatialIndexJob
        {
            Writer = index.Map.AsParallelWriter()
        };

        var populateHandle = populateJob.ScheduleParallel(_beltQuery, populateDep);

        // 3. Fence에 신규 Writer 등록 및 이전 Readers 초기화
        fence.SetWriter(populateHandle);

        state.Dependency = populateHandle;
    }
}

/// <summary>
/// 벨트 공간 해시맵을 비동기로 초기화하는 Burst Job.
/// </summary>
[BurstCompile]
public struct ClearBeltSpatialIndexJob : IJob
{
    public NativeParallelHashMap<int2, BeltInfo> Map;

    public void Execute()
    {
        Map.Clear();
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
