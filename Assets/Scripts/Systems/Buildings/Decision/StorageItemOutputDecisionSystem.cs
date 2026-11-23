using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: Decision에서 보관품의 첫 실물을 보낼 외향 벨트 후보를 찾는다.
/// 입력·생성자: 건물 생성/입고가 유지하는 StorageSlotElement, 회전 footprint와 Synchronization의 벨트/월드 실물 인덱스.
/// 출력·소유권: 생산품 버퍼가 없는 건물의 BuildingItemOutputDecision만 작성한다. 보관품 수량은 변경하지 않는다.
/// 이용·정리: BeltDestinationReservationSystem이 목적지 경합을 중재하고 BuildingItemStorageApplySystem이 인계 후 결정을 소비한다.
/// 건물 둘레에서 처음 가능한 벨트를 선택하며 철거 승인 건물/벨트는 제외한다. ECB 구조 변경은 없다.
/// </summary>
[UpdateInGroup(typeof(BuildingDecisionGroup))]
[BurstCompile]
public partial struct StorageItemOutputDecisionSystem : ISystem
{
    private ComponentLookup<BeltMovementState> _beltMovementStateLookup;
    private ComponentLookup<DestroyItemRequest> _destroyRequestLookup;
    private ComponentLookup<PendingBuildingDemolition> _pendingDemolitionLookup;
    private ComponentLookup<DroneStationStorage> _stationStorageLookup;
    private EntityQuery _buildingQuery;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        _beltMovementStateLookup = state.GetComponentLookup<BeltMovementState>(true);
        _destroyRequestLookup = state.GetComponentLookup<DestroyItemRequest>(true);
        _pendingDemolitionLookup = state.GetComponentLookup<PendingBuildingDemolition>(true);
        _stationStorageLookup = state.GetComponentLookup<DroneStationStorage>(true);

        _buildingQuery = SystemAPI.QueryBuilder()
            .WithAllRW<BuildingItemOutputDecision>()
            .WithAll<StorageSlotElement, BuildingFootprint, GridPosition, Direction>()
            .WithNone<ProductSlotElement, ResearchCycleState>()
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
        _pendingDemolitionLookup.Update(ref state);
        _stationStorageLookup.Update(ref state);

        var job = new StorageItemOutputDecisionJob
        {
            Directions = SystemAPI.GetComponentLookup<Direction>(true),
            BeltMap = beltIndex.Map,
            ItemMap = itemIndex.Map,
            BeltMovementStateLookup = _beltMovementStateLookup,
            DestroyItemRequestLookup = _destroyRequestLookup,
            PendingDemolitionLookup = _pendingDemolitionLookup,
            PreparingDemolitionLookup = SystemAPI.GetComponentLookup<PreparingStationDemolition>(true),
            FuelSlots = SystemAPI.GetComponentLookup<FuelStorageSlots>(true),
            StationStorageLookup = _stationStorageLookup
        };

        var readDep = Unity.Jobs.JobHandle.CombineDependencies(beltFence.State.GetReaderDependency(), itemFence.State.GetReaderDependency());
        var jobDep = Unity.Jobs.JobHandle.CombineDependencies(state.Dependency, readDep);

        var jobHandle = job.ScheduleParallel(_buildingQuery, jobDep);

        beltFence.State.AddReader(jobHandle);
        itemFence.State.AddReader(jobHandle);

        state.Dependency = jobHandle;
    }
}

/// <summary>
/// 일반 창고의 외부 벨트 방출 의사결정을 병렬로 계산하는 Burst Job.
/// </summary>
[BurstCompile]
public partial struct StorageItemOutputDecisionJob : IJobEntity
{
    [ReadOnly]
    public NativeParallelHashMap<int2, BeltInfo> BeltMap;
    [ReadOnly] public ComponentLookup<Direction> Directions;

    [ReadOnly]
    public NativeParallelMultiHashMap<int2, Entity> ItemMap;

    [ReadOnly]
    public ComponentLookup<BeltMovementState> BeltMovementStateLookup;

    [ReadOnly]
    public ComponentLookup<DestroyItemRequest> DestroyItemRequestLookup;

    [ReadOnly]
    public ComponentLookup<PendingBuildingDemolition> PendingDemolitionLookup;
    [ReadOnly] public ComponentLookup<PreparingStationDemolition> PreparingDemolitionLookup;
    [ReadOnly] public ComponentLookup<DroneStationStorage> StationStorageLookup;
    [ReadOnly] public ComponentLookup<FuelStorageSlots> FuelSlots;

    public void Execute(
        Entity building,
        ref BuildingItemOutputDecision outputDecision,
        EnabledRefRW<BuildingItemOutputDecision> outputDecisionEnabled,
        in DynamicBuffer<StorageSlotElement> storedItems,
        in BuildingFootprint footprint,
        in GridPosition gridPos,
        in Direction dir)
    {
        outputDecision = default;
        outputDecisionEnabled.ValueRW = false;
        if (PendingDemolitionLookup.HasComponent(building) || PreparingDemolitionLookup.HasComponent(building))
        {
            return;
        }

        FuelSlots.TryGetComponent(building, out var fuel);
        int outputIndex = BeltEntryUtility.FindStorageOutputSlot(storedItems, StationStorageLookup.HasComponent(building), fuel);
        if (outputIndex < 0)
        {
            return;
        }

        if (BeltEntryUtility.TryFindBuildingOutputBelt(gridPos.Value, footprint.GetEffectiveSize(dir.dir),
                BeltMap, ItemMap, Directions, PendingDemolitionLookup, DestroyItemRequestLookup, BeltMovementStateLookup,
                out int2 foundBeltPos))
        {
            outputDecision = new BuildingItemOutputDecision(true, storedItems[outputIndex].ItemType, foundBeltPos)
            {
                SourceSlotIndex = outputIndex
            };
            outputDecisionEnabled.ValueRW = true;
        }
    }
}
