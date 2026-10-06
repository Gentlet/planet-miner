using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: Synchronization에서 살아 있는 자원 노드의 셀→엔티티 인덱스를 재구축한다.
/// 입력·생성자: ResourceGenerationCommandSystem의 생성과 MinerExecutionSystem의 고갈 삭제가 반영된 ResourceNode·GridPosition.
/// 출력·소유권: 시스템 소유 Persistent 맵을 Clear→병렬 등록하고 Fence에 최종 Writer를 게시한다. 매장량/위치 원본은 변경하지 않는다.
/// 이용·가시화: 다음 채굴 Decision/배치 검증과 개발 검증이 최종 Writer에 의존해 읽는다. 생성/고갈 즉시 맵 갱신을 가정하지 않는다.
/// 정리: 자원이 없어도 Clear한다. 재할당/종료 전 Fence를 완료하고 종료 시 맵을 Dispose한다. ECB 기록은 없다.
/// </summary>
[UpdateInGroup(typeof(SynchronizationGroup))]
[BurstCompile]
public partial struct ResourceSpatialSyncSystem : ISystem
{
    private EntityQuery _resourceQuery;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        var map = new NativeParallelHashMap<int2, Entity>(1024, Allocator.Persistent);

        var singletonEntity = state.EntityManager.CreateEntity();
        state.EntityManager.AddComponentData(singletonEntity, new ResourceSpatialIndex
        {
            Map = map
        });
        state.EntityManager.AddComponentData(singletonEntity, new ResourceSpatialIndexFence());

        _resourceQuery = SystemAPI.QueryBuilder()
            .WithAll<ResourceNode, GridPosition>()
            .Build();
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        if (SystemAPI.TryGetSingletonRW<ResourceSpatialIndexFence>(out var fence))
        {
            fence.ValueRW.Complete();
        }

        if (SystemAPI.TryGetSingleton<ResourceSpatialIndex>(out var index))
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
        if (!SystemAPI.HasSingleton<ResourceSpatialIndex>() || !SystemAPI.HasSingleton<ResourceSpatialIndexFence>())
        {
            return;
        }

        ref var fence = ref SystemAPI.GetSingletonRW<ResourceSpatialIndexFence>().ValueRW;
        var index = SystemAPI.GetSingleton<ResourceSpatialIndex>();

        int count = _resourceQuery.CalculateEntityCount();
        if (count == 0)
        {
            // 자원이 없어도 맵은 비워져야 함 (비동기 ClearJob)
            var clearDep = fence.GetWriterDependency();
            var clearOnlyJob = new ClearResourceSpatialIndexJob
            {
                Map = index.Map
            };
            var clearOnlyHandle = clearOnlyJob.Schedule(clearDep);
            fence.SetWriter(clearOnlyHandle);
            state.Dependency = clearOnlyHandle;
            return;
        }

        if (index.Map.Capacity < count)
        {
            fence.Complete();
            index.Map.Capacity = math.max(1024, count * 2);
        }

        // Native 맵 Clear는 ECS 값 의존성만으로 보호되지 않는다. 기존 Reader/Writer를 Fence로 함께 기다린다.
        // Writer 의존성: 마지막 Writer와 이전 모든 Readers 완료 대기
        var writerDep = fence.GetWriterDependency();

        // 1. ClearJob 비동기 스케줄링
        var clearJob = new ClearResourceSpatialIndexJob
        {
            Map = index.Map
        };
        var clearHandle = clearJob.Schedule(writerDep);

        // 2. PopulateJob 스케줄링 (Clear 완료 및 state.Dependency 결합)
        var populateDep = JobHandle.CombineDependencies(clearHandle, state.Dependency);
        var populateJob = new PopulateResourceSpatialIndexJob
        {
            Writer = index.Map.AsParallelWriter()
        };

        var populateHandle = populateJob.ScheduleParallel(_resourceQuery, populateDep);

        // 3. Fence에 신규 Writer 등록 및 이전 Readers 초기화
        fence.SetWriter(populateHandle);

        state.Dependency = populateHandle;
    }
}

/// <summary>
/// 자원 공간 해시맵을 비동기로 초기화하는 Burst Job.
/// </summary>
[BurstCompile]
public struct ClearResourceSpatialIndexJob : IJob
{
    public NativeParallelHashMap<int2, Entity> Map;

    public void Execute()
    {
        Map.Clear();
    }
}

/// <summary>
/// 자원 노드 엔티티들을 자원 공간 해시맵에 병렬 등록하는 Burst Job.
/// </summary>
[BurstCompile]
public partial struct PopulateResourceSpatialIndexJob : IJobEntity
{
    public NativeParallelHashMap<int2, Entity>.ParallelWriter Writer;

    public void Execute(Entity entity, in GridPosition gridPos, in ResourceNode resourceNode)
    {
        Writer.TryAdd(gridPos.Value, entity);
    }
}
