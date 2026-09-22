using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 벨트 종단에 도달한 아이템의 건물 입고 적합성을 판정하는 시스템.
/// 
/// [책임]
/// - DecisionGroup(Phase 2)에서 실행됩니다.
/// - 벨트 끝(Progress >= 1.0f - AlignmentEpsilon)에 도달한 월드 아이템을 감지합니다.
/// - BuildingSpatialIndex를 통해 진행 방향 다음 타일의 건물 존재 및 수납 기능(Storage) 여부를 O(1)로 조회합니다.
/// - StorageFilter가 부착된 경우 아이템 허용 여부를 검사합니다.
/// - 적합 시 CanDeposit = true, TargetBuilding 지정, TargetSlotIndex = -1(Reservation 단계 확정용)을 기록하고 활성화합니다.
/// - 필터 차단 또는 부적합 시 CanDeposit = false를 기록하여 입고 불가 상태를 전달합니다.
/// - [엄격한 단일 책임 원칙 (SRP)]: 벨트 이동 컴포넌트(BeltMovementDecision)를 수정하지 않으며, 
///   오직 자신의 의사결정 컴포넌트(BuildingItemInputDecision)만 갱신하므로 쓰기 경합이 발생하지 않습니다.
/// </summary>
[UpdateInGroup(typeof(DecisionGroup))]
[BurstCompile]
public partial struct BuildingItemInputDecisionSystem : ISystem
{
    private ComponentLookup<Storage> _storageLookup;
    private ComponentLookup<StorageFilter> _storageFilterLookup;
    private ComponentLookup<CrafterState> _crafterStateLookup;
    private EntityQuery _itemQuery;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        _storageLookup = state.GetComponentLookup<Storage>(true);
        _storageFilterLookup = state.GetComponentLookup<StorageFilter>(true);
        _crafterStateLookup = state.GetComponentLookup<CrafterState>(true);

        _itemQuery = SystemAPI.QueryBuilder()
            .WithAllRW<BuildingItemInputDecision>()
            .WithAll<BeltMovementState, GridPosition, ItemIdentity, ItemOwnership>()
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
        if (!SystemAPI.HasSingleton<BuildingSpatialIndex>() ||
            !SystemAPI.HasSingleton<BuildingSpatialIndexFence>() ||
            !SystemAPI.HasSingleton<BeltSpatialIndex>() ||
            !SystemAPI.HasSingleton<BeltSpatialIndexFence>())
        {
            return;
        }

        var buildingIndex = SystemAPI.GetSingleton<BuildingSpatialIndex>();
        ref var buildingFence = ref SystemAPI.GetSingletonRW<BuildingSpatialIndexFence>().ValueRW;
        var beltIndex = SystemAPI.GetSingleton<BeltSpatialIndex>();
        ref var beltFence = ref SystemAPI.GetSingletonRW<BeltSpatialIndexFence>().ValueRW;

        _storageLookup.Update(ref state);
        _storageFilterLookup.Update(ref state);
        _crafterStateLookup.Update(ref state);

        var job = new BuildingItemInputDecisionJob
        {
            BuildingMap = buildingIndex.Map,
            BeltMap = beltIndex.Map,
            StorageLookup = _storageLookup,
            StorageFilterLookup = _storageFilterLookup,
            CrafterStateLookup = _crafterStateLookup
        };

        // Reader 의존성: BuildingSpatialIndex 및 BeltSpatialIndex의 마지막 Writer 완료 대기 및 Reader 등록
        var readersDep = Unity.Jobs.JobHandle.CombineDependencies(buildingFence.GetReaderDependency(), beltFence.GetReaderDependency());
        var jobDep = Unity.Jobs.JobHandle.CombineDependencies(state.Dependency, readersDep);
        var jobHandle = job.ScheduleParallel(_itemQuery, jobDep);

        buildingFence.AddReader(jobHandle);
        beltFence.AddReader(jobHandle);

        state.Dependency = jobHandle;
    }
}

/// <summary>
/// 각 아이템의 건물 입고 의도를 병렬로 판정하는 Burst Job.
/// </summary>
[BurstCompile]
public partial struct BuildingItemInputDecisionJob : IJobEntity
{
    [ReadOnly]
    public NativeParallelHashMap<int2, BuildingInfo> BuildingMap;

    [ReadOnly]
    public NativeParallelHashMap<int2, BeltInfo> BeltMap;

    [ReadOnly]
    public ComponentLookup<Storage> StorageLookup;

    [ReadOnly]
    public ComponentLookup<StorageFilter> StorageFilterLookup;

    [ReadOnly]
    public ComponentLookup<CrafterState> CrafterStateLookup;

    public void Execute(
        ref BuildingItemInputDecision inputDecision,
        EnabledRefRW<BuildingItemInputDecision> inputDecisionEnabled,
        in BeltMovementState beltState,
        in GridPosition gridPos,
        in ItemIdentity itemIdentity,
        in ItemOwnership ownership)
    {
        // 1. 월드 아이템이 아니면 판정 제외 및 비활성화
        if (!ownership.IsWorldItem)
        {
            inputDecisionEnabled.ValueRW = false;
            return;
        }

        // 2. 현재 타일에 벨트가 없으면 판정 제외 및 비활성화
        if (!BeltMap.TryGetValue(gridPos.Value, out BeltInfo currentBelt))
        {
            inputDecisionEnabled.ValueRW = false;
            return;
        }

        // 3. 벨트 끝(Progress >= 1.0f - Epsilon)에 도달하지 않았으면 비활성화
        if (beltState.Progress < 1.0f - GameConstants.AlignmentEpsilon)
        {
            inputDecisionEnabled.ValueRW = false;
            return;
        }

        // 4. 벨트 진행 방향의 다음 타일 건물 O(1) 탐색
        int2 nextPos = gridPos.Value + currentBelt.Direction.ToInt2();
        if (!BuildingMap.TryGetValue(nextPos, out BuildingInfo buildingInfo))
        {
            // 다음 타일에 건물이 없음
            inputDecision.TargetBuilding = Entity.Null;
            inputDecision.CanDeposit = false;
            inputDecision.TargetSlotIndex = -1;
            inputDecisionEnabled.ValueRW = false;
            return;
        }

        // 5. 대상 건물이 아이템을 보관할 수 있는 건물(Storage)인지 확인
        if (!StorageLookup.HasComponent(buildingInfo.Entity))
        {
            // 보관 기능이 없는 건물 (예: 전신주 등)
            inputDecision.TargetBuilding = buildingInfo.Entity;
            inputDecision.CanDeposit = false;
            inputDecision.TargetSlotIndex = -1;
            inputDecisionEnabled.ValueRW = true;
            return;
        }

        // 5.5 제작기(Crafter) 상태 검사: WaitingForPurgeOutput 상태인 경우 재료 입고 완전 차단
        if (CrafterStateLookup.HasComponent(buildingInfo.Entity))
        {
            var crafterState = CrafterStateLookup[buildingInfo.Entity];
            if (crafterState.Status == CrafterStatusEnum.WaitingForPurgeOutput)
            {
                inputDecision.TargetBuilding = buildingInfo.Entity;
                inputDecision.CanDeposit = false;
                inputDecision.TargetSlotIndex = -1;
                inputDecisionEnabled.ValueRW = true;
                return;
            }
        }

        // 6. StorageFilter 검사 (필터가 부착된 경우)
        if (StorageFilterLookup.HasComponent(buildingInfo.Entity))
        {
            var filter = StorageFilterLookup[buildingInfo.Entity];
            if (!filter.IsItemAllowed(itemIdentity.Type))
            {
                // 필터로 인해 입고 거부
                inputDecision.TargetBuilding = buildingInfo.Entity;
                inputDecision.CanDeposit = false;
                inputDecision.TargetSlotIndex = -1;
                inputDecisionEnabled.ValueRW = true;
                return;
            }
        }

        // 7. 입고 적합 판정: ReservationPhase로 의도 전달
        inputDecision.TargetBuilding = buildingInfo.Entity;
        inputDecision.CanDeposit = true;
        inputDecision.TargetSlotIndex = -1; // ReservationPhase에서 최종 확정
        inputDecisionEnabled.ValueRW = true;
    }
}
