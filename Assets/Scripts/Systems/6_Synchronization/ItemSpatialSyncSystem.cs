using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: Synchronization에서 현재 월드 실물을 셀별 ItemSpatialIndex로 재구축한다.
/// 입력·생성자: 실물 수명주기/Ownership/이동이 반영한 ItemIdentity·GridPosition·ItemOwnership. 수납 실물도 쿼리하되 Owner=Null만 등록한다.
/// 출력·소유권: Persistent 멀티맵을 Clear→병렬 등록하고 Fence에 최종 Writer를 게시한다. 소유권/위치 원본은 변경하지 않는다.
/// 이용·가시화: 배치·벨트 판단과 개발 검증이 Fence로 최종 Writer를 기다려 읽는다. 실물 인계 시점과 공간 맵 게시 시점은 구분한다.
/// 정리: 실물이 없어도 맵을 비운다. 용량 변경/종료 때 Fence를 완료하고 종료 시 맵을 Dispose한다. ECB 기록은 없다.
/// </summary>
[UpdateInGroup(typeof(SynchronizationGroup))]
[BurstCompile]
public partial struct ItemSpatialSyncSystem : ISystem
{
    // 월드 아이템 및 보관(Stored) 아이템을 모두 포함하는 아이템 쿼리
    // 실제 월드 아이템 필터링은 Job 내부에서 ItemOwnership.IsWorldItem으로 수행.
    private EntityQuery _itemQuery;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        var map = new NativeParallelMultiHashMap<int2, Entity>(1024, Allocator.Persistent);

        var singletonEntity = state.EntityManager.CreateEntity();
        state.EntityManager.AddComponentData(singletonEntity, new ItemSpatialIndex
        {
            Map = map
        });
        state.EntityManager.AddComponentData(singletonEntity, new ItemSpatialIndexFence());

        _itemQuery = SystemAPI.QueryBuilder()
            .WithAll<ItemIdentity, GridPosition, ItemOwnership>()
            .Build();
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        if (SystemAPI.TryGetSingletonRW<ItemSpatialIndexFence>(out var fence))
        {
            fence.ValueRW.Complete();
        }

        if (SystemAPI.TryGetSingleton<ItemSpatialIndex>(out var index))
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
        if (!SystemAPI.HasSingleton<ItemSpatialIndex>() || !SystemAPI.HasSingleton<ItemSpatialIndexFence>())
        {
            return;
        }

        ref var fence = ref SystemAPI.GetSingletonRW<ItemSpatialIndexFence>().ValueRW;
        var index = SystemAPI.GetSingleton<ItemSpatialIndex>();

        int count = _itemQuery.CalculateEntityCount();
        if (count == 0)
        {
            var clearDep = fence.GetWriterDependency();
            var clearOnlyJob = new ClearItemSpatialIndexJob
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
        var clearJob = new ClearItemSpatialIndexJob
        {
            Map = index.Map
        };
        var clearHandle = clearJob.Schedule(writerDep);

        // 2. PopulateJob 스케줄링 (Clear 완료 및 state.Dependency 결합)
        var populateDep = JobHandle.CombineDependencies(clearHandle, state.Dependency);
        var populateJob = new PopulateItemSpatialIndexJob
        {
            Writer = index.Map.AsParallelWriter()
        };

        var populateHandle = populateJob.ScheduleParallel(_itemQuery, populateDep);

        // 3. Fence에 신규 Writer 등록 및 이전 Readers 초기화
        fence.SetWriter(populateHandle);

        state.Dependency = populateHandle;
    }
}

/// <summary>
/// 월드 아이템 공간 멀티 해시맵을 비동기로 초기화하는 Burst Job.
/// </summary>
[BurstCompile]
public struct ClearItemSpatialIndexJob : IJob
{
    public NativeParallelMultiHashMap<int2, Entity> Map;

    public void Execute()
    {
        Map.Clear();
    }
}

/// <summary>
/// 월드 아이템 엔티티들을 공간 멀티 해시맵에 병렬 등록하는 Burst Job.
/// </summary>
[BurstCompile]
public partial struct PopulateItemSpatialIndexJob : IJobEntity
{
    public NativeParallelMultiHashMap<int2, Entity>.ParallelWriter Writer;

    public void Execute(Entity entity, in GridPosition pos, in ItemOwnership ownership)
    {
        if (ownership.IsWorldItem)
        {
            Writer.Add(pos.Value, entity);
        }
    }
}
