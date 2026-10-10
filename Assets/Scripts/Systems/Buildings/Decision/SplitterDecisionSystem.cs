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
    private ComponentLookup<ItemOwnership> _itemOwnershipLookup;
    private ComponentLookup<GridPosition> _gridPositionLookup;
    private ComponentLookup<Direction> _directionLookup;
    private ComponentLookup<PlacementStamp> _placementStampLookup;
    private ComponentLookup<PendingBuildingDemolition> _pendingBuildingDemolitionLookup;

    private EntityQuery _splitterQuery;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        _beltMovementStateLookup = state.GetComponentLookup<BeltMovementState>(true);
        _itemOwnershipLookup = state.GetComponentLookup<ItemOwnership>(true);
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

        var beltIndex = SystemAPI.GetSingleton<BeltSpatialIndex>();
        ref var beltFence = ref SystemAPI.GetSingletonRW<BeltSpatialIndexFence>().ValueRW;

        var itemIndex = SystemAPI.GetSingleton<ItemSpatialIndex>();
        ref var itemFence = ref SystemAPI.GetSingletonRW<ItemSpatialIndexFence>().ValueRW;

        _beltMovementStateLookup.Update(ref state);
        _itemOwnershipLookup.Update(ref state);
        _gridPositionLookup.Update(ref state);
        _directionLookup.Update(ref state);
        _placementStampLookup.Update(ref state);
        _pendingBuildingDemolitionLookup.Update(ref state);

        var decisionJob = new SplitterDecisionJob
        {
            BeltMap = beltIndex.Map,
            ItemMap = itemIndex.Map,
            BeltMovementStateLookup = _beltMovementStateLookup,
            ItemOwnershipLookup = _itemOwnershipLookup,
            GridPositionLookup = _gridPositionLookup,
            DirectionLookup = _directionLookup,
            PlacementStampLookup = _placementStampLookup,
            PendingBuildingDemolitionLookup = _pendingBuildingDemolitionLookup
        };

        var readDep = JobHandle.CombineDependencies(beltFence.GetReaderDependency(), itemFence.GetReaderDependency());
        var jobDep = JobHandle.CombineDependencies(state.Dependency, readDep);

        var jobHandle = decisionJob.ScheduleParallel(_splitterQuery, jobDep);

        beltFence.AddReader(jobHandle);
        itemFence.AddReader(jobHandle);

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
    [ReadOnly] public ComponentLookup<ItemOwnership> ItemOwnershipLookup;
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
        DirectionEnum forwardDir = routingState.ForwardDirection;

        if (PendingBuildingDemolitionLookup.HasComponent(inputBelt))
        {
            inputBelt = Entity.Null;
        }

        if (inputBelt == Entity.Null || !GridPositionLookup.HasComponent(inputBelt))
        {
            inputBelt = FindBestIncomingBelt(splitterPos, out forwardDir);
        }
        else
        {
            int2 inputPos = GridPositionLookup[inputBelt].Value;
            if (!BeltMap.TryGetValue(inputPos, out BeltInfo currentInputInfo) ||
                !RoutingDirectionUtility.IsIncomingBelt(splitterPos, inputPos, currentInputInfo.Direction))
            {
                inputBelt = FindBestIncomingBelt(splitterPos, out forwardDir);
            }
        }

        if (inputBelt == Entity.Null)
        {
            decisionEnabled.ValueRW = false;
            return;
        }

        // 2. 입력 벨트 종단(Progress >= 1.0f - Epsilon)에 도달한 아이템 탐색
        int2 inputBeltPos = GridPositionLookup[inputBelt].Value;
        Entity candidateItem = FindItemAtBeltEnd(inputBeltPos);

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
                    if (IsTargetBeltAvailable(targetPos))
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

    private Entity FindBestIncomingBelt(int2 routerPos, out DirectionEnum forwardDir)
    {
        Entity bestBelt = Entity.Null;
        PlacementStamp bestStamp = default;
        bool hasBestStamp = false;
        int2 bestPosition = default;
        forwardDir = DirectionEnum.Up;

        for (int i = 0; i < (int)DirectionEnum.Count; i++)
        {
            DirectionEnum dir = (DirectionEnum)i;
            int2 neighborPos = routerPos + dir.ToInt2();

            if (BeltMap.TryGetValue(neighborPos, out BeltInfo beltInfo))
            {
                if (PendingBuildingDemolitionLookup.HasComponent(beltInfo.Entity))
                {
                    continue;
                }

                if (RoutingDirectionUtility.IsIncomingBelt(routerPos, neighborPos, beltInfo.Direction))
                {
                    PlacementStamp stamp = default;
                    bool hasStamp = PlacementStampLookup.TryGetComponent(beltInfo.Entity, out stamp);

                    if (bestBelt == Entity.Null)
                    {
                        bestBelt = beltInfo.Entity;
                        bestStamp = stamp;
                        hasBestStamp = hasStamp;
                        bestPosition = neighborPos;
                        forwardDir = beltInfo.Direction;
                    }
                    else if (PlacementStamp.Compare(hasStamp, stamp, neighborPos,
                                 hasBestStamp, bestStamp, bestPosition) < 0)
                    {
                        bestBelt = beltInfo.Entity;
                        bestStamp = stamp;
                        hasBestStamp = hasStamp;
                        bestPosition = neighborPos;
                        forwardDir = beltInfo.Direction;
                    }
                }
            }
        }

        return bestBelt;
    }

    private Entity FindItemAtBeltEnd(int2 beltPos)
    {
        Entity bestItem = Entity.Null;
        float maxProgress = 1.0f - GameConstants.AlignmentEpsilon;

        if (ItemMap.TryGetFirstValue(beltPos, out Entity item, out var iterator))
        {
            do
            {
                if (ItemOwnershipLookup.TryGetComponent(item, out var ownership) && ownership.IsWorldItem)
                {
                    if (BeltMovementStateLookup.TryGetComponent(item, out var movement))
                    {
                        if (movement.Progress >= maxProgress)
                        {
                            bestItem = item;
                            maxProgress = movement.Progress;
                        }
                    }
                }
            } while (ItemMap.TryGetNextValue(out item, ref iterator));
        }

        return bestItem;
    }

    private bool IsTargetBeltAvailable(int2 targetPos)
    {
        return BeltEntryUtility.HasEntrySpace(
            targetPos, ItemMap, ItemOwnershipLookup, BeltMovementStateLookup);
    }
}
