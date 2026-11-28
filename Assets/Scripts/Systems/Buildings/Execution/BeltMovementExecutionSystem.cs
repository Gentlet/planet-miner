using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// 역할·목적: Execution에서 Decision의 전진 계획을 활성 월드 실물의 실제 이동 상태에 반영한다.
/// 입력·생성자: BeltMovementDecisionSystem의 PlannedProgress와 Synchronization의 벨트/건물 인덱스.
/// 출력·소유권: BeltMovementState.Progress, GridPosition과 LocalTransform을 함께 갱신한다. 소유권/렌더 태그는 쓰지 않는다.
/// 정리: PlannedProgress는 적용 전에 0으로 소비하여 같은 계획을 반복 실행하지 않는다. 다음 셀의 입구가 이어질 때 경계를 넘고 남은 이동량을 이월한다.
/// 가시화: 값은 Job 완료 후 Reader에 보이며 아이템 인덱스는 Synchronization에서 재구축한다. ECB 구조 변경은 없다.
/// </summary>
[UpdateInGroup(typeof(BuildingExecutionGroup))]
[BurstCompile]
public partial struct BeltMovementExecutionSystem : ISystem
{

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.HasSingleton<BeltSpatialIndex>() || !SystemAPI.HasSingleton<BeltSpatialIndexFence>() ||
            !SystemAPI.HasSingleton<BuildingSpatialIndex>() || !SystemAPI.HasSingleton<BuildingSpatialIndexFence>())
        {
            return;
        }

        var beltIndex = SystemAPI.GetSingleton<BeltSpatialIndex>();
        ref var beltFence = ref SystemAPI.GetSingletonRW<BeltSpatialIndexFence>().ValueRW;

        var buildingIndex = SystemAPI.GetSingleton<BuildingSpatialIndex>();
        ref var buildingFence = ref SystemAPI.GetSingletonRW<BuildingSpatialIndexFence>().ValueRW;

        var job = new BeltMovementExecutionJob
        {
            Cells = new RuntimeBeltPathCells
            {
                RoutingBelts = new RuntimeRoutingBeltCells
                {
                    Belts = beltIndex.Map,
                    Directions = SystemAPI.GetComponentLookup<Direction>(true),
                    PlacementStamps = SystemAPI.GetComponentLookup<PlacementStamp>(true),
                    PendingDemolitions = SystemAPI.GetComponentLookup<PendingBuildingDemolition>(true)
                },
                Buildings = buildingIndex.Map,
                OutputDecisions = SystemAPI.GetComponentLookup<BuildingItemOutputDecision>(true),
                Splitters = SystemAPI.GetComponentLookup<SplitterRoutingState>(true),
                Mergers = SystemAPI.GetComponentLookup<MergerRoutingState>(true)
            },
            PendingDemolitions = SystemAPI.GetComponentLookup<PendingBuildingDemolition>(true)
        };

        // Reader 의존성: 마지막 Writer가 끝난 뒤 읽기 실행
        var jobDep = JobHandle.CombineDependencies(state.Dependency, beltFence.State.GetReaderDependency(),
            buildingFence.State.GetReaderDependency());
        var jobHandle = job.ScheduleParallel(jobDep);

        // Fence에 Reader Handle 등록
        beltFence.State.AddReader(jobHandle);
        buildingFence.State.AddReader(jobHandle);

        state.Dependency = jobHandle;
    }
}

/// <summary>
/// 벨트 위 활성화된 각 아이템의 위치 및 진행 상태를 병렬로 갱신하고 의사결정 값을 소비하는 Safe Burst Job.
/// </summary>
[BurstCompile]
[WithNone(typeof(DestroyItemRequest))]
public partial struct BeltMovementExecutionJob : IJobEntity
{
    internal RuntimeBeltPathCells Cells;
    [ReadOnly] public ComponentLookup<PendingBuildingDemolition> PendingDemolitions;

    public void Execute(
        Entity entity,
        ref BeltMovementState state,
        ref GridPosition gridPos,
        ref LocalTransform transform,
        ref BeltMovementDecision decision)
    {
        float planned = decision.PlannedProgress;
        decision.PlannedProgress = 0f;
        if (!Cells.TryGetBelt(gridPos.Value, out BeltInfo currentBelt))
        {
            return;
        }

        if (planned > 0f)
        {
            float progress = state.Progress + planned;
            for (int hop = 0; hop < GameConstants.MaxTileHopsPerFrame; hop++)
            {
                int2 nextPos = gridPos.Value + currentBelt.Direction.ToInt2();
                if (!Cells.TryGetBelt(nextPos, out BeltInfo nextBelt))
                {
                    break;
                }
                if (BeltPathUtility.IsHeadOn(currentBelt.Direction, nextBelt.Direction))
                {
                    float stopProgress = hop == 0
                        ? math.max(state.Progress, BeltPathUtility.HeadOnStopProgress)
                        : BeltPathUtility.HeadOnStopProgress;
                    progress = math.min(progress, stopProgress);
                    break;
                }
                if (progress < 1f || PendingDemolitions.HasComponent(nextBelt.Entity))
                {
                    break;
                }

                if (BeltPathUtility.GetEntryDirection(nextPos, nextBelt.Direction, Cells) != currentBelt.Direction)
                {
                    progress = math.min(progress, hop == 0
                        ? math.max(state.Progress, 1f - GameConstants.ItemSpacing)
                        : 1f - GameConstants.ItemSpacing);
                    break;
                }

                state.EntryDirection = currentBelt.Direction;
                gridPos.Value = nextPos;
                currentBelt = nextBelt;
                progress -= 1f;
            }

            state.Progress = math.min(progress, 1f);
        }

        if (state.Progress < 0.5f && (state.EntryDirection == DirectionEnum.Count ||
            BeltPathUtility.IsHeadOn(state.EntryDirection, currentBelt.Direction)))
        {
            state.EntryDirection = BeltPathUtility.GetEntryDirection(gridPos.Value, currentBelt.Direction, Cells);
        }
        transform.Position = BeltPathUtility.Position(gridPos.Value, state.Progress, state.EntryDirection, currentBelt.Direction);
    }
}
