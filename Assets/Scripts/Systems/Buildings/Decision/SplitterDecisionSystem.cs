using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: Decision에서 분배기의 입력 종단 실물을 가용 출력 포트로 전달할 후보를 판단한다.
/// 입력·생성자: RoutingApplySystem의 입력 기준선/출력 커서, Synchronization 인덱스와 벨트 PlacementStamp.
/// 출력·소유권: 분배기의 RoutingTransferDecision과 enable 상태만 갱신한다. 기준선·커서·실물은 변경하지 않는다.
/// 이용·정리: BeltDestinationReservationSystem이 목적지 경합을 중재하고 RoutingApplySystem이 인계 성공 시 상태/커서를 반영한다.
/// 기준 입력이 무효면 다시 찾고 커서부터 가능한 출력을 찾는다. 결정은 다음 틱 초기화하며 철거 대상은 후보에서 제외한다.
/// </summary>
[UpdateInGroup(typeof(BuildingDecisionGroup))]
[BurstCompile]
public partial struct SplitterDecisionSystem : ISystem
{
    private ComponentLookup<BeltMovementState> _beltMovementStateLookup;
    private ComponentLookup<DestroyItemRequest> _destroyRequestLookup;
    private ComponentLookup<GridPosition> _gridPositionLookup;
    private ComponentLookup<Direction> _directionLookup;
    private ComponentLookup<PlacementStamp> _placementStampLookup;
    private ComponentLookup<PendingBuildingDemolition> _pendingBuildingDemolitionLookup;

    private EntityQuery _splitterQuery;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        _beltMovementStateLookup = state.GetComponentLookup<BeltMovementState>(true);
        _destroyRequestLookup = state.GetComponentLookup<DestroyItemRequest>(true);
        _gridPositionLookup = state.GetComponentLookup<GridPosition>(true);
        _directionLookup = state.GetComponentLookup<Direction>(true);
        _placementStampLookup = state.GetComponentLookup<PlacementStamp>(true);
        _pendingBuildingDemolitionLookup = state.GetComponentLookup<PendingBuildingDemolition>(true);

        _splitterQuery = SystemAPI.QueryBuilder()
            .WithAllRW<RoutingTransferDecision>()
            .WithAll<BuildingType, SplitterRoutingState, GridPosition>()
            .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
            .Build();
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

        var beltIndex = SystemAPI.GetSingleton<BeltSpatialIndex>();
        ref var beltFence = ref SystemAPI.GetSingletonRW<BeltSpatialIndexFence>().ValueRW;

        var itemIndex = SystemAPI.GetSingleton<ItemSpatialIndex>();
        ref var itemFence = ref SystemAPI.GetSingletonRW<ItemSpatialIndexFence>().ValueRW;

        _beltMovementStateLookup.Update(ref state);
        _destroyRequestLookup.Update(ref state);
        _gridPositionLookup.Update(ref state);
        _directionLookup.Update(ref state);
        _placementStampLookup.Update(ref state);
        _pendingBuildingDemolitionLookup.Update(ref state);

        var decisionJob = new SplitterDecisionJob
        {
            BeltMap = beltIndex.Map,
            ItemMap = itemIndex.Map,
            BeltMovementStateLookup = _beltMovementStateLookup,
            DestroyItemRequestLookup = _destroyRequestLookup,
            GridPositionLookup = _gridPositionLookup,
            DirectionLookup = _directionLookup,
            PlacementStampLookup = _placementStampLookup,
            PendingBuildingDemolitionLookup = _pendingBuildingDemolitionLookup
        };

        var readDep = JobHandle.CombineDependencies(beltFence.State.GetReaderDependency(), itemFence.State.GetReaderDependency());
        var jobDep = JobHandle.CombineDependencies(state.Dependency, readDep);

        var jobHandle = decisionJob.ScheduleParallel(_splitterQuery, jobDep);

        beltFence.State.AddReader(jobHandle);
        itemFence.State.AddReader(jobHandle);

        state.Dependency = jobHandle;
    }
}

/// <summary>
/// 개별 Splitter의 분배 결정을 병렬 처리하는 Burst Job.
/// </summary>
[BurstCompile]
public partial struct SplitterDecisionJob : IJobEntity
{
    [ReadOnly] public NativeParallelHashMap<int2, BeltInfo> BeltMap;
    [ReadOnly] public NativeParallelMultiHashMap<int2, Entity> ItemMap;

    [ReadOnly] public ComponentLookup<BeltMovementState> BeltMovementStateLookup;
    [ReadOnly] public ComponentLookup<DestroyItemRequest> DestroyItemRequestLookup;
    [ReadOnly] public ComponentLookup<GridPosition> GridPositionLookup;
    [ReadOnly] public ComponentLookup<Direction> DirectionLookup;
    [ReadOnly] public ComponentLookup<PlacementStamp> PlacementStampLookup;
    [ReadOnly] public ComponentLookup<PendingBuildingDemolition> PendingBuildingDemolitionLookup;

    public void Execute(
        Entity entity,
        ref RoutingTransferDecision decision,
        EnabledRefRW<RoutingTransferDecision> decisionEnabled,
        in BuildingType buildingType,
        in SplitterRoutingState routingState,
        in GridPosition gridPos)
    {
        // 이전 프레임의 활성 결정을 남기지 않고 이번 프레임 후보만 생성한다.
        decision = default;
        decisionEnabled.ValueRW = false;

        if (PendingBuildingDemolitionLookup.HasComponent(entity))
        {
            return;
        }

        if (buildingType.Type != BuildingTypeEnum.Splitter)
        {
            decisionEnabled.ValueRW = false;
            return;
        }

        int2 splitterPos = gridPos.Value;

        // 영속 기준선은 실제 전달 성공 때만 Apply가 바꾼다. 여기서는 무효 연결을 다시 찾되 상태 원본은 유지한다.
        // 1. 기준 입력 벨트 결정
        Entity inputBelt = routingState.InputBelt;
        int2 inputBeltPos = default;
        bool hasInputPosition = false;
        DirectionEnum forwardDir = routingState.ForwardDirection;

        if (PendingBuildingDemolitionLookup.HasComponent(inputBelt))
        {
            inputBelt = Entity.Null;
        }

        if (inputBelt == Entity.Null)
        {
            inputBelt = BeltEntryUtility.FindRoutingBelt(splitterPos, BeltMap, PendingBuildingDemolitionLookup, PlacementStampLookup, incoming: true, out forwardDir);
        }
        else if (!GridPositionLookup.TryGetComponent(inputBelt, out var currentInputPosition))
        {
            inputBelt = BeltEntryUtility.FindRoutingBelt(splitterPos, BeltMap, PendingBuildingDemolitionLookup, PlacementStampLookup, incoming: true, out forwardDir);
        }
        else
        {
            inputBeltPos = currentInputPosition.Value;
            hasInputPosition = true;
            if (!BeltMap.TryGetValue(inputBeltPos, out BeltInfo currentInputInfo) ||
                !RoutingDirectionUtility.IsIncomingBelt(splitterPos, inputBeltPos, currentInputInfo.Direction))
            {
                inputBelt = BeltEntryUtility.FindRoutingBelt(splitterPos, BeltMap, PendingBuildingDemolitionLookup, PlacementStampLookup, incoming: true, out forwardDir);
                hasInputPosition = false;
            }
        }

        if (inputBelt == Entity.Null)
        {
            decisionEnabled.ValueRW = false;
            return;
        }

        // 2. 입력 벨트 종단(Progress >= 1.0f - Epsilon)에 도달한 아이템 탐색
        if (!hasInputPosition)
        {
            inputBeltPos = GridPositionLookup[inputBelt].Value;
        }
        Entity candidateItem = BeltEntryUtility.FindItemAtBeltEnd(
            inputBeltPos, ItemMap, DestroyItemRequestLookup, BeltMovementStateLookup);

        if (candidateItem == Entity.Null)
        {
            decisionEnabled.ValueRW = false;
            return;
        }

        // 3. OutputCursor부터 forward -> right -> left 순서로 출력 벨트 탐색 (Work-conserving)
        // 기준선이 변경된 경우 0번 포트부터 탐색
        byte startCursor = (inputBelt != routingState.InputBelt) ? (byte)0 : routingState.OutputCursor;
        Entity targetBelt = Entity.Null;

        for (byte offset = 0; offset < RoutingDirectionUtility.RoutingPortCount; offset++)
        {
            byte portIndex = (byte)((startCursor + offset) % RoutingDirectionUtility.RoutingPortCount);
            DirectionEnum outDir = RoutingDirectionUtility.GetSplitterOutput(forwardDir, portIndex);
            int2 targetPos = splitterPos + outDir.ToInt2();

            if (BeltMap.TryGetValue(targetPos, out BeltInfo beltInfo))
            {
                if (PendingBuildingDemolitionLookup.HasComponent(beltInfo.Entity))
                {
                    continue;
                }

                if (RoutingDirectionUtility.IsOutgoingBelt(splitterPos, targetPos, beltInfo.Direction))
                {
                    if (BeltEntryUtility.HasEntrySpace(targetPos, ItemMap, DestroyItemRequestLookup, BeltMovementStateLookup))
                    {
                        targetBelt = beltInfo.Entity;
                        break;
                    }
                }
            }
        }

        if (targetBelt == Entity.Null)
        {
            decisionEnabled.ValueRW = false;
            return;
        }

        // 4. 결정 생성 및 활성화
        decision.Item = candidateItem;
        decision.SourceBelt = inputBelt;
        decision.TargetBelt = targetBelt;
        decisionEnabled.ValueRW = true;
    }
}
