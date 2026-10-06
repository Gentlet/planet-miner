using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: Reservation에서 건물 출고와 분배/합류 전달의 같은 벨트 셀 진입 경합을 중재한다.
/// 입력·생성자: 출고/라우팅 Decision의 활성 후보, Synchronization 인덱스와 발신 건물 PlacementStamp.
/// 출력·소유권: 목적 셀별 후보 하나만 유지하고 탈락 결정은 비활성화한다. 일반 벨트 이동·원본 실물은 쓰지 않는다.
/// 이용: BuildingItemStorageApplySystem과 RoutingApplySystem이 승인된 인계를 반영한다. 인덱스 Reader는 Fence로 동기화한다.
/// 정리·가시화: 후보 집계는 Job 안에서만 유지 후 해제한다. 별도 영속 예약/ECB 생성 없이 이번 틱 결정에 승인을 남긴다.
/// </summary>
[UpdateInGroup(typeof(BuildingReservationGroup))]
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

/// <summary>같은 목적 벨트 셀을 중재할 후보의 발신 경계. 영속 요청 종류나 실물 종류가 아니다.</summary>
public enum DestinationCandidateType : byte
{
    BuildingOutput = 0,
    RoutingTransfer = 1
}

/// <summary>Reservation Job 안에서만 이용하는 발신 결정/실물/PlacementStamp의 후보 기록.</summary>
public struct DestinationCandidate
{
    public DestinationCandidateType Type;
    public Entity SourceEntity;      // 건물 엔티티 또는 라우팅 엔티티
    public Entity ItemEntity;        // 전송 대상 아이템
    public PlacementStamp Stamp;
    public bool HasStamp;
}

/// <summary>목적 셀별 후보를 모아 하나만 유지하는 단일 Job. 인덱스를 읽고 탈락 결정만 쓰며 로컬 후보 컨테이너는 처리 뒤 Dispose한다.</summary>
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

            // 한 셀에 이번 틱 외부 진입 하나만 유지한다. 일반 벨트 전진은 이 후보 집계에 포함하지 않는다.
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
        return BeltEntryUtility.HasEntrySpace(
            targetPos, ItemMap, ItemOwnershipLookup, BeltMovementStateLookup);
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
