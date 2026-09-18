using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 벨트 위 아이템의 이동 가능 거리를 계산하고 막힘(Backpressure) 상태를 판정하는 의사결정 시스템.
/// 
/// [책임]
/// - DecisionGroup(Phase 2)에서 실행됩니다.
/// - 현재 벨트의 속도, 선행 아이템과의 간격(ItemSpacing), 다음 타일의 벨트 유무 및 정체 여부를 계산하여
///   이번 프레임에 전진할 거리인 PlannedMovement와 IsBlocked 플래그를 BeltMovementDecision에 기록합니다.
/// - [상태-의사결정 분리]: 상태 컴포넌트(BeltMovementState)는 오직 읽기(in)만 수행하고,
///   산출물은 의사결정 컴포넌트(BeltMovementDecision)에 기록(ref)하므로 Job 내 컨테이너 Aliasing이 발생하지 않는 완전한 Safe Job입니다.
/// - [엄격한 책임 분리]: 이 시스템은 좌표나 진행률(Progress, GridPosition)을 수정하지 않으며,
///   실제 위치 반영은 ExecutionGroup의 BeltMovementExecutionSystem에서 수행됩니다.
/// </summary>
[UpdateInGroup(typeof(DecisionGroup))]
[BurstCompile]
public partial struct BeltMovementDecisionSystem : ISystem
{
    private ComponentLookup<BeltMovementState> _beltMovementStateLookup;
    private ComponentLookup<ItemOwnership> _itemOwnershipLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        _beltMovementStateLookup = state.GetComponentLookup<BeltMovementState>(true);
        _itemOwnershipLookup = state.GetComponentLookup<ItemOwnership>(true);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.HasSingleton<BeltSpatialIndex>() || !SystemAPI.HasSingleton<ItemSpatialIndex>())
        {
            return;
        }

        float dt = SystemAPI.Time.DeltaTime;
        if (dt <= 0.0f)
        {
            return;
        }

        var beltIndex = SystemAPI.GetSingleton<BeltSpatialIndex>();
        var itemIndex = SystemAPI.GetSingleton<ItemSpatialIndex>();

        _beltMovementStateLookup.Update(ref state);
        _itemOwnershipLookup.Update(ref state);

        var job = new BeltMovementDecisionJob
        {
            DeltaTime = dt,
            BeltMap = beltIndex.Map,
            ItemMap = itemIndex.Map,
            BeltMovementStateLookup = _beltMovementStateLookup,
            ItemOwnershipLookup = _itemOwnershipLookup
        };

        state.Dependency = job.ScheduleParallel(state.Dependency);
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
            decision.PlannedMovement = 0.0f;
            decision.IsBlocked = true;
            return;
        }

        // 2. 현재 타일에 벨트가 존재하지 않으면 이동 불가 (바닥에 멈춤)
        if (!BeltMap.TryGetValue(gridPos.Value, out BeltInfo currentBelt))
        {
            decision.PlannedMovement = 0.0f;
            decision.IsBlocked = true;
            return;
        }

        float desiredMove = currentBelt.Speed * DeltaTime;
        if (desiredMove <= 0.0f)
        {
            decision.PlannedMovement = 0.0f;
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
            decision.PlannedMovement = planned;
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
            decision.PlannedMovement = planned;
            decision.IsBlocked = (planned < desiredMove - GameConstants.AlignmentEpsilon || distanceToTileEnd <= GameConstants.AlignmentEpsilon);
            return;
        }

        // 5. 다음 타일에 벨트가 있는 경우, 다음 타일 내 선행 아이템들 검사
        float minNextProgress = float.MaxValue;
        bool hasNextItem = false;

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
                }
            } while (ItemMap.TryGetNextValue(out nextItem, ref nextIt));
        }

        if (!hasNextItem)
        {
            // 다음 타일이 완전히 비어 있으므로 원하는 만큼 이동 가능
            decision.PlannedMovement = desiredMove;
            decision.IsBlocked = false;
            return;
        }

        // 다음 타일에 아이템이 있는 경우 최소 간격(ItemSpacing) 유지 확인
        // (1.0f - state.Progress) : 현재 타일 출구까지의 거리
        // minNextProgress : 다음 타일 입구로부터 앞선 아이템까지의 거리
        // 총 거리 = (1.0f - state.Progress) + minNextProgress
        float distanceToNext = (1.0f - state.Progress) + minNextProgress;
        float availableDistanceNext = math.max(0.0f, distanceToNext - GameConstants.ItemSpacing);
        float plannedNext = math.min(desiredMove, availableDistanceNext);
        decision.PlannedMovement = plannedNext;
        decision.IsBlocked = (plannedNext < desiredMove - GameConstants.AlignmentEpsilon);
    }
}
