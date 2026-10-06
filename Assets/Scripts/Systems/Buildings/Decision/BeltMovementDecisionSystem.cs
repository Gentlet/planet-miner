using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: Decision에서 활성 월드 아이템이 벨트 위에서 전진할 거리와 정체 여부를 계산한다.
/// 입력·생성자: Synchronization의 벨트/아이템 인덱스, 이동 상태, 소유권과 Command의 철거 승인 상태.
/// 출력·소유권: 아이템의 BeltMovementDecision.PlannedProgress/IsBlocked만 갱신한다. 위치·진행도 원본은 쓰지 않는다.
/// 이용·정리: BeltMovementExecutionSystem이 계획을 적용하고 PlannedProgress를 소비한다. 결정 컴포넌트는 실물에 남는다.
/// 공간 인덱스 Reader를 Fence에 등록하며 다음 셀에 벨트가 없으면 현재 셀 끝까지만 전진한다. ECB 구조 변경은 없다.
/// </summary>
[UpdateInGroup(typeof(BuildingDecisionGroup))]
[BurstCompile]
public partial struct BeltMovementDecisionSystem : ISystem
{
    private ComponentLookup<BeltMovementState> _beltMovementStateLookup;
    private ComponentLookup<ItemOwnership> _itemOwnershipLookup;
    private ComponentLookup<PendingBuildingDemolition> _pendingBuildingDemolitionLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        _beltMovementStateLookup = state.GetComponentLookup<BeltMovementState>(true);
        _itemOwnershipLookup = state.GetComponentLookup<ItemOwnership>(true);
        _pendingBuildingDemolitionLookup = state.GetComponentLookup<PendingBuildingDemolition>(true);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.HasSingleton<BeltSpatialIndex>() || 
            !SystemAPI.HasSingleton<BeltSpatialIndexFence>() || 
            !SystemAPI.HasSingleton<ItemSpatialIndex>() ||
            !SystemAPI.HasSingleton<ItemSpatialIndexFence>())
        {
            return;
        }

        float dt = math.min(SystemAPI.Time.DeltaTime, GameConstants.MaxSimulationDeltaTime);
        if (dt <= 0.0f)
        {
            return;
        }

        var beltIndex = SystemAPI.GetSingleton<BeltSpatialIndex>();
        ref var beltFence = ref SystemAPI.GetSingletonRW<BeltSpatialIndexFence>().ValueRW;
        var itemIndex = SystemAPI.GetSingleton<ItemSpatialIndex>();
        ref var itemFence = ref SystemAPI.GetSingletonRW<ItemSpatialIndexFence>().ValueRW;

        _beltMovementStateLookup.Update(ref state);
        _itemOwnershipLookup.Update(ref state);
        _pendingBuildingDemolitionLookup.Update(ref state);

        var job = new BeltMovementDecisionJob
        {
            DeltaTime = dt,
            BeltMap = beltIndex.Map,
            ItemMap = itemIndex.Map,
            BeltMovementStateLookup = _beltMovementStateLookup,
            ItemOwnershipLookup = _itemOwnershipLookup,
            PendingBuildingDemolitionLookup = _pendingBuildingDemolitionLookup
        };

        // Reader 의존성: Belt 및 Item의 마지막 Writer가 끝난 뒤 읽기 실행
        var readersDep = JobHandle.CombineDependencies(beltFence.GetReaderDependency(), itemFence.GetReaderDependency());
        var jobDep = JobHandle.CombineDependencies(state.Dependency, readersDep);
        var jobHandle = job.ScheduleParallel(jobDep);

        // Fence에 Reader Handle 등록
        beltFence.AddReader(jobHandle);
        itemFence.AddReader(jobHandle);

        state.Dependency = jobHandle;
    }
}

/// <summary>
/// 벨트 위 활성화된 각 아이템의 이동 가능 거리를 병렬로 계산하는 Safe Burst Job.
/// </summary>
[BurstCompile]
public partial struct BeltMovementDecisionJob : IJobEntity
{
    public float DeltaTime;

    [ReadOnly]
    public NativeParallelHashMap<int2, BeltInfo> BeltMap;

    [ReadOnly]
    public NativeParallelMultiHashMap<int2, Entity> ItemMap;

    [ReadOnly]
    public ComponentLookup<BeltMovementState> BeltMovementStateLookup;

    [ReadOnly]
    public ComponentLookup<ItemOwnership> ItemOwnershipLookup;

    [ReadOnly]
    public ComponentLookup<PendingBuildingDemolition> PendingBuildingDemolitionLookup;

    public void Execute(
        Entity entity,
        ref BeltMovementDecision decision,
        in BeltMovementState state,
        in GridPosition gridPos,
        in ItemOwnership ownership)
    {
        // 1. 월드 아이템이 아니면 벨트 위에서 이동할 수 없음
        if (!ownership.IsWorldItem)
        {
            decision.PlannedProgress = 0.0f;
            decision.IsBlocked = true;
            return;
        }

        // 2. 현재 타일에 벨트가 존재하지 않으면 이동 불가 (바닥에 멈춤)
        if (!BeltMap.TryGetValue(gridPos.Value, out BeltInfo currentBelt))
        {
            decision.PlannedProgress = 0.0f;
            decision.IsBlocked = true;
            return;
        }

        // Command에서 철거가 승인된 벨트는 공간 인덱스에 남아 있어도 동작하지 않는다.
        if (PendingBuildingDemolitionLookup.HasComponent(currentBelt.Entity))
        {
            decision.PlannedProgress = 0.0f;
            decision.IsBlocked = true;
            return;
        }

        float desiredMove = currentBelt.Speed * DeltaTime;
        if (desiredMove <= 0.0f)
        {
            decision.PlannedProgress = 0.0f;
            decision.IsBlocked = false;
            return;
        }

        // 3. 같은 타일 내 앞선 아이템 검사
        float minAheadProgress = float.MaxValue;
        bool hasAheadItem = false;

        if (ItemMap.TryGetFirstValue(gridPos.Value, out Entity otherItem, out var it))
        {
            do
            {
                if (otherItem != entity &&
                    ItemOwnershipLookup.HasComponent(otherItem) &&
                    ItemOwnershipLookup[otherItem].IsWorldItem &&
                    BeltMovementStateLookup.HasComponent(otherItem) &&
                    BeltMovementStateLookup.IsComponentEnabled(otherItem))
                {
                    float otherProg = BeltMovementStateLookup[otherItem].Progress;
                    if (otherProg > state.Progress + GameConstants.AlignmentEpsilon)
                    {
                        minAheadProgress = math.min(minAheadProgress, otherProg);
                        hasAheadItem = true;
                    }
                }
            } while (ItemMap.TryGetNextValue(out otherItem, ref it));
        }

        if (hasAheadItem)
        {
            float headway = minAheadProgress - state.Progress;
            float availableDistance = math.max(0.0f, headway - GameConstants.ItemSpacing);
            float planned = math.min(desiredMove, availableDistance);
            decision.PlannedProgress = planned;
            decision.IsBlocked = (planned < desiredMove - GameConstants.AlignmentEpsilon);
            return;
        }

        // 4. 같은 타일에 앞선 아이템이 없는 경우 (타일 내 선두 아이템)
        int2 nextPos = gridPos.Value + currentBelt.Direction.ToInt2();
        if (!BeltMap.TryGetValue(nextPos, out BeltInfo nextBelt))
        {
            // 다음 타일에 벨트가 없음 (벨트 종단 지점): 현재 타일 끝(1.0f)까지만 이동 허용 및 정체
            float distanceToTileEnd = math.max(0.0f, 1.0f - state.Progress);
            float planned = math.min(desiredMove, distanceToTileEnd);
            decision.PlannedProgress = planned;
            decision.IsBlocked = (planned < desiredMove - GameConstants.AlignmentEpsilon || distanceToTileEnd <= GameConstants.AlignmentEpsilon);
            return;
        }

        if (PendingBuildingDemolitionLookup.HasComponent(nextBelt.Entity))
        {
            // Execution은 인덱스의 벨트로 진행도 1에서 넘어가므로 경계 직전에 멈춘다.
            float distanceBeforeBoundary = math.max(0.0f, (1.0f - GameConstants.AlignmentEpsilon) - state.Progress);
            float planned = math.min(desiredMove, distanceBeforeBoundary);
            decision.PlannedProgress = planned;
            decision.IsBlocked = (planned < desiredMove - GameConstants.AlignmentEpsilon || distanceBeforeBoundary <= GameConstants.AlignmentEpsilon);
            return;
        }

        // 5. 다음 타일에 벨트가 있는 경우, 다음 타일 내 선행 아이템들 검사
        float minNextProgress = float.MaxValue;
        bool hasNextItem = false;
        int nextItemCount = 0;

        if (ItemMap.TryGetFirstValue(nextPos, out Entity nextItem, out var nextIt))
        {
            do
            {
                if (nextItem != entity &&
                    ItemOwnershipLookup.HasComponent(nextItem) &&
                    ItemOwnershipLookup[nextItem].IsWorldItem &&
                    BeltMovementStateLookup.HasComponent(nextItem) &&
                    BeltMovementStateLookup.IsComponentEnabled(nextItem))
                {
                    float nextProg = BeltMovementStateLookup[nextItem].Progress;
                    minNextProgress = math.min(minNextProgress, nextProg);
                    hasNextItem = true;
                    nextItemCount++;
                }
            } while (ItemMap.TryGetNextValue(out nextItem, ref nextIt));
        }

        if (!hasNextItem)
        {
            // 다다음 셀과의 최소 간격은 생성 시 적용한 GameConstants.MaxBeltSpeed 상한으로 확보한다.
            // 1.0f 클램프는 기존 이동 계획의 범위 방어이며, 단독으로 최소 간격을 보장하지 않는다.
            float planned = math.min(desiredMove, 1.0f);
            decision.PlannedProgress = planned;
            decision.IsBlocked = (planned < desiredMove - GameConstants.AlignmentEpsilon);
            return;
        }

        // 다음 타일에 아이템이 있는 경우 최소 간격(ItemSpacing) 유지 확인
        // (1.0f - state.Progress) : 현재 타일 출구까지의 거리
        // minNextProgress : 다음 타일 입구로부터 앞선 아이템까지의 거리
        // 총 거리 = (1.0f - state.Progress) + minNextProgress
        float distanceToNext = (1.0f - state.Progress) + minNextProgress;
        float availableDistanceNext = math.max(0.0f, distanceToNext - GameConstants.ItemSpacing);

        // 다음 타일이 이미 최대 수용량(1.0f / ItemSpacing)에 도달한 경우, 타일 경계를 넘을 수 없도록 상한 제한
        if (nextItemCount >= GameConstants.MaxItemsPerBeltTile)
        {
            float maxMoveBeforeBoundary = math.max(0.0f, (1.0f - GameConstants.AlignmentEpsilon) - state.Progress);
            availableDistanceNext = math.min(availableDistanceNext, maxMoveBeforeBoundary);
        }

        float plannedNext = math.min(desiredMove, availableDistanceNext);
        plannedNext = math.min(plannedNext, 1.0f);
        decision.PlannedProgress = plannedNext;
        decision.IsBlocked = (plannedNext < desiredMove - GameConstants.AlignmentEpsilon);
    }
}
