using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

/// <summary>
/// 월드의 자원 노드 엔티티들을 ResourceSpatialIndex에 동기화하는 시스템.
/// 
/// [책임]
/// - SynchronizationGroup(Phase 6)에서 실행되어 비동기 잡 체인으로 자원 공간 인덱스를 Clear하고
///   현재 월드에 존재하는 유효한 자원 노드([ResourceNode, GridPosition])를 1:1 좌표 단위로 일괄 등록.
/// - ResourceSpatialIndexFence를 통해 이전 Phase의 Reader 잡들이 모두 완료된 후 쓰기를 시작하며,
///   Map.Capacity 확장과 같은 메인 스레드 재할당 시에만 제한적으로 Complete()를 호출.
/// - ISystem/Burst 기반 병렬 Job으로 Spatial Index 갱신.
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
