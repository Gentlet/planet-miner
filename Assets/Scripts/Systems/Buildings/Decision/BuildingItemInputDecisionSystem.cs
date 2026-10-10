using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: Decision에서 벨트 종단 실물의 건물 입고 적합성을 판단한다.
/// 입력·생성자: 월드 실물의 진행도/품목, Synchronization의 건물/벨트 인덱스와 저장·필터·제작 상태.
/// 출력·소유권: 실물의 BuildingItemInputDecision과 enable 상태만 쓴다. 허용 후보의 슬롯은 -1로 남긴다.
/// 이용·정리: BuildingStorageInputReservationSystem이 슬롯을 확정하고 BuildingItemStorageApplySystem이 인계 후 비활성화한다.
/// 철거 승인 대상은 후보부터 차단한다. 실물·소유 버퍼 변경이나 ECB 기록은 하지 않는다.
/// </summary>
[UpdateInGroup(typeof(BuildingDecisionGroup))]
[BurstCompile]
public partial struct BuildingItemInputDecisionSystem : ISystem
{
    private ComponentLookup<Storage> _storageLookup;
    private ComponentLookup<StorageFilter> _storageFilterLookup;
    private ComponentLookup<CrafterState> _crafterStateLookup;
    private ComponentLookup<PendingBuildingDemolition> _pendingDemolitionLookup;
    private EntityQuery _itemQuery;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        _storageLookup = state.GetComponentLookup<Storage>(true);
        _storageFilterLookup = state.GetComponentLookup<StorageFilter>(true);
        _crafterStateLookup = state.GetComponentLookup<CrafterState>(true);
        _pendingDemolitionLookup = state.GetComponentLookup<PendingBuildingDemolition>(true);

        // 지난 틱 비활성 결정을 포함해 다시 판단한다. 이동 활성 여부는 Job에서 별도로 검사한다.
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
        _pendingDemolitionLookup.Update(ref state);

        var job = new BuildingItemInputDecisionJob
        {
            BuildingMap = buildingIndex.Map,
            BeltMap = beltIndex.Map,
            StorageLookup = _storageLookup,
            StorageFilterLookup = _storageFilterLookup,
            CrafterStateLookup = _crafterStateLookup,
            PendingDemolitionLookup = _pendingDemolitionLookup
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

    [ReadOnly]
    public ComponentLookup<PendingBuildingDemolition> PendingDemolitionLookup;

    public void Execute(
        ref BuildingItemInputDecision inputDecision,
        EnabledRefRW<BuildingItemInputDecision> inputDecisionEnabled,
        in BeltMovementState beltState,
        EnabledRefRO<BeltMovementState> beltMovementEnabled,
        in GridPosition gridPos,
        in ItemIdentity itemIdentity,
        in ItemOwnership ownership)
    {
        // 비활성 결정도 매 틱 재판단하되 이전 대상과 예약 슬롯은 남기지 않는다.
        inputDecision = new BuildingItemInputDecision(Entity.Null);
        inputDecisionEnabled.ValueRW = false;

        // 바닥 아이템의 잔여 진행도는 입고 자격이 아니다. 실제 이동 참여 상태를 확인한다.
        if (!beltMovementEnabled.ValueRO)
        {
            return;
        }

        // 1. 월드 아이템이 아니면 판정 제외 및 비활성화
        if (!ownership.IsWorldItem)
        {
            return;
        }

        // 2. 현재 타일에 벨트가 없으면 판정 제외 및 비활성화
        if (!BeltMap.TryGetValue(gridPos.Value, out BeltInfo currentBelt))
        {
            return;
        }

        // 철거 승인된 현재 벨트는 종단에 도달한 아이템도 인계하지 않는다.
        if (PendingDemolitionLookup.HasComponent(currentBelt.Entity))
        {
            return;
        }

        // 3. 벨트 끝(Progress >= 1.0f - Epsilon)에 도달하지 않았으면 비활성화
        if (beltState.Progress < 1.0f - GameConstants.AlignmentEpsilon)
        {
            return;
        }

        // 4. 벨트 진행 방향의 다음 타일 건물 O(1) 탐색
        int2 nextPos = gridPos.Value + currentBelt.Direction.ToInt2();
        if (!BuildingMap.TryGetValue(nextPos, out BuildingInfo buildingInfo))
        {
            // 다음 타일에 건물이 없음
            return;
        }

        // Command에서 철거를 승인한 건물에는 입고 후보를 생성하지 않는다.
        if (PendingDemolitionLookup.HasComponent(buildingInfo.Entity))
        {
            return;
        }

        // 5. 대상 건물이 아이템을 보관할 수 있는 건물(Storage)인지 확인
        if (!StorageLookup.HasComponent(buildingInfo.Entity))
        {
            // 보관 기능이 없는 건물 (예: 전신주 등)
            return;
        }

        if (StorageLookup[buildingInfo.Entity].SlotCount <= 0)
        {
            return;
        }

        // 5.5 제작기(Crafter) 상태 검사: WaitingForByproductOutput 상태인 경우 재료 입고 완전 차단
        if (CrafterStateLookup.HasComponent(buildingInfo.Entity))
        {
            var crafterState = CrafterStateLookup[buildingInfo.Entity];
            if (crafterState.Status == CrafterStatusEnum.WaitingForByproductOutput)
            {
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
