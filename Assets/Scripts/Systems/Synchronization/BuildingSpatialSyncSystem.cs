using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: Synchronization에서 현장/완공 건물의 회전 footprint 전체 점유를 BuildingSpatialIndex로 재구축한다.
/// 입력·생성자: 배치/완공/철거가 반영한 BuildingType·방향 적용 전 BuildingFootprint·GridPosition·Direction.
/// 출력·소유권: 시스템 소유 Persistent 맵을 Clear→병렬 등록하고 Fence에 최종 Writer를 게시한다. 건물 원본을 변경하지 않는다.
/// 이용·가시화: 다음 배치 검증/건물 입고 판단이 Fence를 통해 현재 점유를 읽는다. Command 삭제 직후의 이전 점유는 이 재구축 전까지 남는다.
/// 정리: 대상이 없어도 Clear한다. 용량은 건물 수가 아닌 점유 면적 합으로 확보하고 재할당/종료 전 Fence를 완료한 뒤 종료 시 맵을 Dispose한다.
/// </summary>
[UpdateInGroup(typeof(SynchronizationGroup))]
[BurstCompile]
public partial struct BuildingSpatialSyncSystem : ISystem
{
    private EntityQuery _buildingQuery;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        var map = new NativeParallelHashMap<int2, BuildingInfo>(1024, Allocator.Persistent);

        var singletonEntity = state.EntityManager.CreateEntity();
        state.EntityManager.AddComponentData(singletonEntity, new BuildingSpatialIndex
        {
            Map = map
        });
        state.EntityManager.AddComponentData(singletonEntity, new BuildingSpatialIndexFence());

        _buildingQuery = SystemAPI.QueryBuilder()
            .WithAll<BuildingType, BuildingFootprint, GridPosition, Direction>()
            .Build();
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        if (SystemAPI.TryGetSingletonRW<BuildingSpatialIndexFence>(out var fence))
        {
            fence.ValueRW.Complete();
        }

        if (SystemAPI.TryGetSingleton<BuildingSpatialIndex>(out var index))
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
        if (!SystemAPI.HasSingleton<BuildingSpatialIndex>() || !SystemAPI.HasSingleton<BuildingSpatialIndexFence>())
        {
            return;
        }

        ref var fence = ref SystemAPI.GetSingletonRW<BuildingSpatialIndexFence>().ValueRW;
        var index = SystemAPI.GetSingleton<BuildingSpatialIndex>();

        int count = _buildingQuery.CalculateEntityCount();
        if (count == 0)
        {
            // 건물이 없어도 맵은 비워져야 함 (비동기 ClearJob)
            var clearDep = fence.GetWriterDependency();
            var clearOnlyJob = new ClearBuildingSpatialIndexJob
            {
                Map = index.Map
            };
            var clearOnlyHandle = clearOnlyJob.Schedule(clearDep);
            fence.SetWriter(clearOnlyHandle);
            state.Dependency = clearOnlyHandle;
            return;
        }

        // 건물 점유 타일 수(Footprint 면적) 총합 기준 필요 용량 산출 및 안전 확장
        int requiredCapacity = 0;
        foreach (var footprint in SystemAPI.Query<RefRO<BuildingFootprint>>().WithAll<BuildingType, GridPosition, Direction>())
        {
            int2 size = math.max(footprint.ValueRO.Size, new int2(1, 1));
            requiredCapacity += size.x * size.y;
        }

        if (index.Map.Capacity < requiredCapacity)
        {
            fence.Complete();
            index.Map.Capacity = math.max(1024, requiredCapacity * 2);
        }

        // 회전은 footprint 면적을 바꾸지 않는다. 등록은 회전된 셀을 사용하며 맵 Clear는 기존 Reader/Writer를 Fence로 기다린다.
        // Writer 의존성: 마지막 Writer와 이전 모든 Readers 완료 대기
        var writerDep = fence.GetWriterDependency();

        // 1. ClearJob 비동기 스케줄링
        var clearJob = new ClearBuildingSpatialIndexJob
        {
            Map = index.Map
        };
        var clearHandle = clearJob.Schedule(writerDep);

        // 2. PopulateJob 스케줄링 (Clear 완료 및 state.Dependency 결합)
        var populateDep = JobHandle.CombineDependencies(clearHandle, state.Dependency);
        var populateJob = new PopulateBuildingSpatialIndexJob
        {
            Writer = index.Map.AsParallelWriter()
        };

        var populateHandle = populateJob.ScheduleParallel(_buildingQuery, populateDep);

        // 3. Fence에 신규 Writer 등록 및 이전 Readers 초기화
        fence.SetWriter(populateHandle);

        state.Dependency = populateHandle;
    }
}

/// <summary>
/// 건물 공간 해시맵을 비동기로 초기화하는 Burst Job.
/// </summary>
[BurstCompile]
public struct ClearBuildingSpatialIndexJob : IJob
{
    public NativeParallelHashMap<int2, BuildingInfo> Map;

    public void Execute()
    {
        Map.Clear();
    }
}

/// <summary>
/// 건물 엔티티들을 회전 스왑이 반영된 다중 타일 단위로 건물 공간 해시맵에 병렬 등록하는 Burst Job.
/// </summary>
[BurstCompile]
public partial struct PopulateBuildingSpatialIndexJob : IJobEntity
{
    public NativeParallelHashMap<int2, BuildingInfo>.ParallelWriter Writer;

    public void Execute(
        Entity entity,
        in BuildingType type,
        in BuildingFootprint footprint,
        in GridPosition pos,
        in Direction dir)
    {
        int2 effectiveSize = footprint.GetEffectiveSize(dir.dir);
        var info = new BuildingInfo(entity, type.Type, dir.dir);

        for (int y = 0; y < effectiveSize.y; y++)
        {
            for (int x = 0; x < effectiveSize.x; x++)
            {
                Writer.TryAdd(pos.Value + new int2(x, y), info);
            }
        }
    }
}
