using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

/// <summary>
/// Splitter 분배 의사결정 시스템.
/// 
/// [책임]
/// - DecisionGroup (Phase 2)에서 실행.
/// - Splitter의 입력 벨트 종단(Progress >= 1.0f - Epsilon)에 도달한 아이템 감지.
/// - 현재 OutputCursor부터 forward -> right -> left 순서로 유효한 출력 벨트를 탐색(Work-conserving).
/// - 후보가 확정되면 RoutingTransferDecision(Item, SourceBelt, TargetBelt) 활성화.
/// - 영속 상태(SplitterRoutingState)를 직접 수정하지 않으며, 순수 의사결정만 산출.
/// </summary>
[UpdateInGroup(typeof(DecisionGroup))]
[BurstCompile]
public partial struct SplitterDecisionSystem : ISystem
{
    private ComponentLookup<BeltMovementState> _beltMovementStateLookup;
    private ComponentLookup<ItemOwnership> _itemOwnershipLookup;
    private ComponentLookup<GridPosition> _gridPositionLookup;
    private ComponentLookup<Direction> _directionLookup;
    private ComponentLookup<PlacementStamp> _placementStampLookup;

    private EntityQuery _splitterQuery;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        _beltMovementStateLookup = state.GetComponentLookup<BeltMovementState>(true);
        _itemOwnershipLookup = state.GetComponentLookup<ItemOwnership>(true);
        _gridPositionLookup = state.GetComponentLookup<GridPosition>(true);
        _directionLookup = state.GetComponentLookup<Direction>(true);
        _placementStampLookup = state.GetComponentLookup<PlacementStamp>(true);

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

        var decisionJob = new SplitterDecisionJob
        {
            BeltMap = beltIndex.Map,
            ItemMap = itemIndex.Map,
            BeltMovementStateLookup = _beltMovementStateLookup,
            ItemOwnershipLookup = _itemOwnershipLookup,
            GridPositionLookup = _gridPositionLookup,
            DirectionLookup = _directionLookup,
            PlacementStampLookup = _placementStampLookup
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

    public void Execute(
        Entity entity,
        ref RoutingTransferDecision decision,
        EnabledRefRW<RoutingTransferDecision> decisionEnabled,
        in BuildingType buildingType,
        in SplitterRoutingState routingState,
        in GridPosition gridPos)
    {
        if (buildingType.Type != BuildingTypeEnum.Splitter)
        {
            decisionEnabled.ValueRW = false;
            return;
        }

        int2 splitterPos = gridPos.Value;

        // 1. 기준 입력 벨트 결정
        Entity inputBelt = routingState.InputBelt;
        DirectionEnum forwardDir = routingState.ForwardDirection;

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
        byte startCursor = routingState.OutputCursor;
        Entity targetBelt = Entity.Null;

        for (byte offset = 0; offset < RoutingDirectionUtility.RoutingPortCount; offset++)
        {
            byte portIndex = (byte)((startCursor + offset) % RoutingDirectionUtility.RoutingPortCount);
            DirectionEnum outDir = RoutingDirectionUtility.GetSplitterOutput(forwardDir, portIndex);
            int2 targetPos = splitterPos + outDir.ToInt2();

            if (BeltMap.TryGetValue(targetPos, out BeltInfo beltInfo))
            {
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
        forwardDir = DirectionEnum.Up;

        for (int i = 0; i < (int)DirectionEnum.Count; i++)
        {
            DirectionEnum dir = (DirectionEnum)i;
            int2 neighborPos = routerPos + dir.ToInt2();

            if (BeltMap.TryGetValue(neighborPos, out BeltInfo beltInfo))
            {
                if (RoutingDirectionUtility.IsIncomingBelt(routerPos, neighborPos, beltInfo.Direction))
                {
                    PlacementStamp stamp = default;
                    bool hasStamp = PlacementStampLookup.TryGetComponent(beltInfo.Entity, out stamp);

                    if (bestBelt == Entity.Null)
                    {
                        bestBelt = beltInfo.Entity;
                        bestStamp = stamp;
                        hasBestStamp = hasStamp;
                        forwardDir = beltInfo.Direction;
                    }
                    else if (hasStamp && (!hasBestStamp || stamp.IsEarlierThan(bestStamp)))
                    {
                        bestBelt = beltInfo.Entity;
                        bestStamp = stamp;
                        hasBestStamp = true;
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
        int itemCount = 0;
        float minProgress = 1.0f;

        if (ItemMap.TryGetFirstValue(targetPos, out Entity item, out var iterator))
        {
            do
            {
                if (ItemOwnershipLookup.TryGetComponent(item, out var ownership) && ownership.IsWorldItem)
                {
                    itemCount++;
                    if (BeltMovementStateLookup.TryGetComponent(item, out var movement))
                    {
                        if (movement.Progress < minProgress)
                        {
                            minProgress = movement.Progress;
                        }
                    }
                }
            } while (ItemMap.TryGetNextValue(out item, ref iterator));
        }

        if (itemCount >= GameConstants.MaxItemsPerBeltTile)
        {
            return false;
        }

        if (itemCount > 0 && minProgress < GameConstants.ItemSpacing)
        {
            return false;
        }

        return true;
    }
}

