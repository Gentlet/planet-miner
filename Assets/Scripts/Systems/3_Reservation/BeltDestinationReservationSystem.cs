using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 외부 건물 출고 및 라우팅 후보의 대상 벨트 진입 경합을 조율하는 시스템.
/// 
/// [책임]
/// - ReservationGroup (Phase 3)에서 실행.
/// - 건물 출고(BuildingItemOutputDecision)와 라우팅(RoutingTransferDecision)의 대상 벨트 진입 경합 해결.
/// - 대상 벨트의 여유 공간(정원 및 입구 간격) 확인 후 진입 허용 여부 결정.
/// - 동일 대상 벨트로 진입하려는 후보 중 PlacementStamp 우선순위가 가장 높은 1개만 승인.
/// - 탈락하거나 공간 부족으로 거부된 후보의 결정 컴포넌트를 비활성화.
/// </summary>
[UpdateInGroup(typeof(ReservationGroup))]
[BurstCompile]
public partial struct BeltDestinationReservationSystem : ISystem
{
    private ComponentLookup<BeltMovementState> _beltMovementStateLookup;
    private ComponentLookup<BuildingItemOutputDecision> _buildingOutputDecisionLookup;
    private ComponentLookup<RoutingTransferDecision> _routingTransferDecisionLookup;
    private ComponentLookup<ItemOwnership> _itemOwnershipLookup;
    private ComponentLookup<GridPosition> _gridPositionLookup;
    private ComponentLookup<PlacementStamp> _placementStampLookup;

    private EntityQuery _buildingOutputQuery;
    private EntityQuery _routingTransferQuery;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        _beltMovementStateLookup = state.GetComponentLookup<BeltMovementState>(true);
        _buildingOutputDecisionLookup = state.GetComponentLookup<BuildingItemOutputDecision>(false);
        _routingTransferDecisionLookup = state.GetComponentLookup<RoutingTransferDecision>(false);
        _itemOwnershipLookup = state.GetComponentLookup<ItemOwnership>(true);
        _gridPositionLookup = state.GetComponentLookup<GridPosition>(true);
        _placementStampLookup = state.GetComponentLookup<PlacementStamp>(true);

        _buildingOutputQuery = SystemAPI.QueryBuilder()
            .WithAllRW<BuildingItemOutputDecision>()
            .Build();

        _routingTransferQuery = SystemAPI.QueryBuilder()
            .WithAllRW<RoutingTransferDecision>()
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

        int outputCount = _buildingOutputQuery.CalculateEntityCount();
        int routingCount = _routingTransferQuery.CalculateEntityCount();

        // 출고 또는 라우팅 요청이 없으면 조기 반환
        if (outputCount == 0 && routingCount == 0)
        {
            return;
        }

        var beltIndex = SystemAPI.GetSingleton<BeltSpatialIndex>();
        ref var beltFence = ref SystemAPI.GetSingletonRW<BeltSpatialIndexFence>().ValueRW;

        var itemIndex = SystemAPI.GetSingleton<ItemSpatialIndex>();
        ref var itemFence = ref SystemAPI.GetSingletonRW<ItemSpatialIndexFence>().ValueRW;

        _beltMovementStateLookup.Update(ref state);
        _buildingOutputDecisionLookup.Update(ref state);
        _routingTransferDecisionLookup.Update(ref state);
        _itemOwnershipLookup.Update(ref state);
        _gridPositionLookup.Update(ref state);
        _placementStampLookup.Update(ref state);

        var buildingOutputEntities = _buildingOutputQuery.ToEntityArray(Allocator.TempJob);
        var routingTransferEntities = _routingTransferQuery.ToEntityArray(Allocator.TempJob);

        var reservationJob = new BeltDestinationReservationJob
        {
            BeltMap = beltIndex.Map,
            ItemMap = itemIndex.Map,
            BuildingOutputEntities = buildingOutputEntities,
            RoutingTransferEntities = routingTransferEntities,
            BeltMovementStateLookup = _beltMovementStateLookup,
            BuildingOutputDecisionLookup = _buildingOutputDecisionLookup,
            RoutingTransferDecisionLookup = _routingTransferDecisionLookup,
            ItemOwnershipLookup = _itemOwnershipLookup,
            GridPositionLookup = _gridPositionLookup,
            PlacementStampLookup = _placementStampLookup
        };

        var readDep = Unity.Jobs.JobHandle.CombineDependencies(beltFence.GetReaderDependency(), itemFence.GetReaderDependency());
        var jobDep = Unity.Jobs.JobHandle.CombineDependencies(state.Dependency, readDep);

        var jobHandle = Unity.Jobs.IJobExtensions.Schedule(reservationJob, jobDep);

        buildingOutputEntities.Dispose(jobHandle);
        routingTransferEntities.Dispose(jobHandle);

        beltFence.AddReader(jobHandle);
        itemFence.AddReader(jobHandle);

        state.Dependency = jobHandle;
    }
}

public enum DestinationCandidateType : byte
{
    BuildingOutput = 0,
    RoutingTransfer = 1
}

public struct DestinationCandidate
{
    public DestinationCandidateType Type;
    public Entity SourceEntity;      // 건물 엔티티 또는 라우팅 엔티티
    public Entity ItemEntity;        // 전송 대상 아이템
    public PlacementStamp Stamp;
    public bool HasStamp;
}

[BurstCompile]
public struct BeltDestinationReservationJob : Unity.Jobs.IJob
{
    [ReadOnly] public NativeParallelHashMap<int2, BeltInfo> BeltMap;
    [ReadOnly] public NativeParallelMultiHashMap<int2, Entity> ItemMap;

    [ReadOnly] public NativeArray<Entity> BuildingOutputEntities;
    [ReadOnly] public NativeArray<Entity> RoutingTransferEntities;

    [ReadOnly] public ComponentLookup<BeltMovementState> BeltMovementStateLookup;
    public ComponentLookup<BuildingItemOutputDecision> BuildingOutputDecisionLookup;
    public ComponentLookup<RoutingTransferDecision> RoutingTransferDecisionLookup;
    [ReadOnly] public ComponentLookup<ItemOwnership> ItemOwnershipLookup;
    [ReadOnly] public ComponentLookup<GridPosition> GridPositionLookup;
    [ReadOnly] public ComponentLookup<PlacementStamp> PlacementStampLookup;

    public void Execute()
    {
        // 1. 대상 타일별 후보자 수집 (대상 벨트 위치 -> 후보 목록)
        int totalCandidates = BuildingOutputEntities.Length + RoutingTransferEntities.Length;
        int initialCapacity = math.max(16, totalCandidates * 2);

        var candidatesByTarget = new NativeParallelMultiHashMap<int2, DestinationCandidate>(initialCapacity, Allocator.Temp);
        var targetSet = new NativeParallelHashSet<int2>(initialCapacity, Allocator.Temp);

        // A. 건물 출고 후보 수집
        for (int i = 0; i < BuildingOutputEntities.Length; i++)
        {
            Entity buildingEntity = BuildingOutputEntities[i];
            if (!BuildingOutputDecisionLookup.HasComponent(buildingEntity) ||
                !BuildingOutputDecisionLookup.IsComponentEnabled(buildingEntity))
            {
                continue;
            }

            var decision = BuildingOutputDecisionLookup[buildingEntity];
            if (!decision.CanOutput || decision.ItemToOutput == Entity.Null)
            {
                continue;
            }

            int2 targetPos = decision.TargetBeltPosition;
            if (!BeltMap.ContainsKey(targetPos))
            {
                // 대상 위치에 벨트가 없으면 출고 불가 처리
                decision.CanOutput = false;
                BuildingOutputDecisionLookup[buildingEntity] = decision;
                BuildingOutputDecisionLookup.SetComponentEnabled(buildingEntity, false);
                continue;
            }

            PlacementStamp stamp = default;
            bool hasStamp = PlacementStampLookup.TryGetComponent(buildingEntity, out stamp);

            var cand = new DestinationCandidate
            {
                Type = DestinationCandidateType.BuildingOutput,
                SourceEntity = buildingEntity,
                ItemEntity = decision.ItemToOutput,
                Stamp = stamp,
                HasStamp = hasStamp
            };

            candidatesByTarget.Add(targetPos, cand);
            targetSet.Add(targetPos);
        }

        // B. 라우팅 전달 후보 수집
        for (int i = 0; i < RoutingTransferEntities.Length; i++)
        {
            Entity routingEntity = RoutingTransferEntities[i];
            if (!RoutingTransferDecisionLookup.HasComponent(routingEntity) ||
                !RoutingTransferDecisionLookup.IsComponentEnabled(routingEntity))
            {
                continue;
            }

            var decision = RoutingTransferDecisionLookup[routingEntity];
            if (decision.TargetBelt == Entity.Null || !GridPositionLookup.HasComponent(decision.TargetBelt))
            {
                RoutingTransferDecisionLookup.SetComponentEnabled(routingEntity, false);
                continue;
            }

            int2 targetPos = GridPositionLookup[decision.TargetBelt].Value;
            if (!BeltMap.ContainsKey(targetPos))
            {
                // 대상 위치에 벨트가 없으면 라우팅 요청 비활성화
                RoutingTransferDecisionLookup.SetComponentEnabled(routingEntity, false);
                continue;
            }

            PlacementStamp stamp = default;
            bool hasStamp = PlacementStampLookup.TryGetComponent(routingEntity, out stamp);

            var cand = new DestinationCandidate
            {
                Type = DestinationCandidateType.RoutingTransfer,
                SourceEntity = routingEntity,
                ItemEntity = decision.Item,
                Stamp = stamp,
                HasStamp = hasStamp
            };

            candidatesByTarget.Add(targetPos, cand);
            targetSet.Add(targetPos);
        }

        // 2. 경합 해결 실행
        ExecuteArbitration(ref candidatesByTarget, ref targetSet);

        candidatesByTarget.Dispose();
        targetSet.Dispose();
    }

    private void ExecuteArbitration(
        ref NativeParallelMultiHashMap<int2, DestinationCandidate> candidatesByTarget,
        ref NativeParallelHashSet<int2> targetSet)
    {
        var targetArray = targetSet.ToNativeArray(Allocator.Temp);

        for (int t = 0; t < targetArray.Length; t++)
        {
            int2 targetPos = targetArray[t];

            // 1. 대상 벨트 타일의 여유 공간 확인 (수용량 및 입구 최소 간격)
            bool spaceAvailable = CheckTargetSpace(targetPos);

            // 2. 해당 대상 타일로 진입하려는 외부 후보 목록 수집
            var candidates = new NativeList<DestinationCandidate>(4, Allocator.Temp);
            if (candidatesByTarget.TryGetFirstValue(targetPos, out DestinationCandidate cand, out var it))
            {
                do
                {
                    candidates.Add(cand);
                } while (candidatesByTarget.TryGetNextValue(out cand, ref it));
            }

            if (candidates.Length == 0)
            {
                candidates.Dispose();
                continue;
            }

            // 공간 부족: 모든 외부 진입 후보 거부
            if (!spaceAvailable)
            {
                for (int i = 0; i < candidates.Length; i++)
                {
                    RejectCandidate(candidates[i]);
                }
                candidates.Dispose();
                continue;
            }

            // 3. PlacementStamp(설치 시점) 기준으로 가장 우선순위가 높은 후보 선별
            int bestCandidateIndex = 0;
            for (int i = 1; i < candidates.Length; i++)
            {
                if (IsCandidateEarlier(candidates[i], candidates[bestCandidateIndex]))
                {
                    bestCandidateIndex = i;
                }
            }

            // 최고 우선순위 1개만 승인(유지)하고 나머지 탈락 후보는 비활성화
            for (int i = 0; i < candidates.Length; i++)
            {
                if (i != bestCandidateIndex)
                {
                    RejectCandidate(candidates[i]);
                }
            }

            candidates.Dispose();
        }

        targetArray.Dispose();
    }

    private bool CheckTargetSpace(int2 targetPos)
    {
        // 대상 타일에 기존 아이템이 없으면 공간 충분
        if (!ItemMap.TryGetFirstValue(targetPos, out Entity itemEntity, out var it))
        {
            return true;
        }

        float minProgress = float.MaxValue;
        int itemCount = 0;

        // 대상 타일의 아이템 수 및 입구에서 가장 가까운 아이템의 진행도 확인
        do
        {
            if (ItemOwnershipLookup.HasComponent(itemEntity) &&
                ItemOwnershipLookup[itemEntity].IsWorldItem &&
                BeltMovementStateLookup.HasComponent(itemEntity) &&
                BeltMovementStateLookup.IsComponentEnabled(itemEntity))
            {
                float prog = BeltMovementStateLookup[itemEntity].Progress;
                minProgress = math.min(minProgress, prog);
                itemCount++;
            }
        } while (ItemMap.TryGetNextValue(out itemEntity, ref it));

        // 타일 수용량(GameConstants.MaxItemsPerBeltTile) 초과 시 진입 불가
        if (itemCount >= GameConstants.MaxItemsPerBeltTile)
        {
            return false;
        }

        // 입구로부터 첫 번째 아이템까지의 간격이 최소 간격(ItemSpacing) 미만이면 진입 불가
        if (itemCount > 0 && minProgress < GameConstants.ItemSpacing - GameConstants.AlignmentEpsilon)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// 탈락하거나 공간 부족으로 거부된 후보의 결정을 비활성화합니다.
    /// </summary>
    private void RejectCandidate(in DestinationCandidate cand)
    {
        switch (cand.Type)
        {
            case DestinationCandidateType.BuildingOutput:
                if (BuildingOutputDecisionLookup.HasComponent(cand.SourceEntity))
                {
                    var decision = BuildingOutputDecisionLookup[cand.SourceEntity];
                    decision.CanOutput = false;
                    BuildingOutputDecisionLookup[cand.SourceEntity] = decision;
                    BuildingOutputDecisionLookup.SetComponentEnabled(cand.SourceEntity, false);
                }
                break;

            case DestinationCandidateType.RoutingTransfer:
                if (RoutingTransferDecisionLookup.HasComponent(cand.SourceEntity))
                {
                    RoutingTransferDecisionLookup.SetComponentEnabled(cand.SourceEntity, false);
                }
                break;
        }
    }

    /// <summary>
    /// PlacementStamp(Tick, Order) 및 Entity.Index 순서로 후보 간 우선순위를 비교합니다.
    /// </summary>
    private static bool IsCandidateEarlier(in DestinationCandidate a, in DestinationCandidate b)
    {
        if (a.HasStamp && b.HasStamp)
        {
            if (a.Stamp.Tick != b.Stamp.Tick)
                return a.Stamp.Tick < b.Stamp.Tick;
            if (a.Stamp.Order != b.Stamp.Order)
                return a.Stamp.Order < b.Stamp.Order;
            return a.SourceEntity.Index < b.SourceEntity.Index;
        }
        if (a.HasStamp) return true;
        if (b.HasStamp) return false;
        return a.SourceEntity.Index < b.SourceEntity.Index;
    }
}
