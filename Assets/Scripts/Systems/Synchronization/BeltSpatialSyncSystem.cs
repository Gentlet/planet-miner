using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: Synchronization에서 현재 벨트 ECS 원본을 셀별 BeltSpatialIndex로 재구축한다.
/// 입력·생성자: 건물 생성/방향 변경이 유지하는 GridPosition·Direction·BeltComponent. 이전 Reader/Writer는 해당 Fence가 추적한다.
/// 출력·소유권: 이 시스템이 Persistent 맵과 Fence를 생성하고 매 틱 Clear→병렬 등록의 최종 Writer를 게시한다. 원본 컴포넌트는 쓰지 않는다.
/// 이용·가시화: 다음 Decision/Reservation/Execution의 Reader는 게시된 Writer에 의존한다. 같은 틱 앞선 생성/철거가 맵에 즉시 반영된다고 간주하지 않는다.
/// 정리: 대상이 없어도 Clear한다. 용량 변경/종료 때 Fence를 완료하고 시스템 종료 시 맵을 Dispose한다. ECB 기록은 없다.
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

        // Native 맵 Clear는 ECS 값 의존성만으로 보호되지 않는다. 기존 Reader/Writer를 Fence로 함께 기다린다.
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
