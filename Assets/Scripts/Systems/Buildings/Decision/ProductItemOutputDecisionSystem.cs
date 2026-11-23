using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: Decision에서 생산 건물이 외향 벨트로 내보낼 실물 하나를 선택한다.
/// 입력·생성자: 생산품 버퍼, 복합 채굴기의 보관 버퍼, 회전 footprint와 최종 공간 인덱스.
/// 출력·소유권: 생산품 버퍼가 있는 건물의 BuildingItemOutputDecision을 단독 작성한다. 생산품이 없으면 채굴기의 보관 구간을 확인한다.
/// 이용·정리: BeltDestinationReservationSystem이 목적지 경합을 중재하고 BuildingItemStorageApplySystem이 인계/결정 소비를 수행한다.
/// 철거 승인 건물/벨트는 제외한다. 생산 버퍼·소유권 원본 및 ECB는 변경하지 않는다.
/// </summary>
[UpdateInGroup(typeof(BuildingDecisionGroup))]
[BurstCompile]
public partial struct ProductItemOutputDecisionSystem : ISystem
{
    private ComponentLookup<BeltMovementState> _beltMovementStateLookup;
    private ComponentLookup<DestroyItemRequest> _destroyRequestLookup;
    private ComponentLookup<PendingBuildingDemolition> _pendingDemolitionLookup;
    private EntityQuery _buildingQuery;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        _beltMovementStateLookup = state.GetComponentLookup<BeltMovementState>(true);
        _destroyRequestLookup = state.GetComponentLookup<DestroyItemRequest>(true);
        _pendingDemolitionLookup = state.GetComponentLookup<PendingBuildingDemolition>(true);

        _buildingQuery = SystemAPI.QueryBuilder()
            .WithAllRW<BuildingItemOutputDecision>()
            .WithAll<ProductSlotElement, BuildingFootprint, GridPosition, Direction>()
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

        var job = new ProductItemOutputDecisionJob
        {
            Directions = SystemAPI.GetComponentLookup<Direction>(true),
            BeltMap = beltIndex.Map,
            ItemMap = itemIndex.Map,
            BeltMovementStateLookup = _beltMovementStateLookup,
            DestroyItemRequestLookup = _destroyRequestLookup,
            PendingDemolitionLookup = _pendingDemolitionLookup,
            PreparingDemolition = SystemAPI.GetComponentLookup<PreparingStationDemolition>(true),
            Miners = SystemAPI.GetComponentLookup<MinerState>(true),
            StationStorage = SystemAPI.GetComponentLookup<DroneStationStorage>(true),
            FuelSlots = SystemAPI.GetComponentLookup<FuelStorageSlots>(true),
            StoredItems = SystemAPI.GetBufferLookup<StorageSlotElement>(true)
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
/// 생산 건물의 외부 벨트 방출 의사결정을 병렬로 계산하는 Burst Job.
/// </summary>
[BurstCompile]
public partial struct ProductItemOutputDecisionJob : IJobEntity
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
    [ReadOnly] public ComponentLookup<PreparingStationDemolition> PreparingDemolition;
    [ReadOnly] public ComponentLookup<MinerState> Miners;
    [ReadOnly] public ComponentLookup<DroneStationStorage> StationStorage;
    [ReadOnly] public ComponentLookup<FuelStorageSlots> FuelSlots;
    [ReadOnly] public BufferLookup<StorageSlotElement> StoredItems;

    public void Execute(
        Entity building,
        ref BuildingItemOutputDecision outputDecision,
        EnabledRefRW<BuildingItemOutputDecision> outputDecisionEnabled,
        in DynamicBuffer<ProductSlotElement> productItems,
        in BuildingFootprint footprint,
        in GridPosition gridPos,
        in Direction dir)
    {
        outputDecision = default;
        outputDecisionEnabled.ValueRW = false;
        if (PendingDemolitionLookup.HasComponent(building) || PreparingDemolition.HasComponent(building))
        {
            return;
        }

        bool fromProduct = !productItems.IsEmpty;
        ItemTypeEnum itemType;
        int slotIndex;
        if (fromProduct)
        {
            var product = productItems[0];
            itemType = product.ItemType;
            slotIndex = product.SlotIndex;
        }
        else
        {
            // 제작기의 보관 버퍼는 레시피 입력이다. 채굴기의 별도 보관 구간만 출고 후보로 쓴다.
            if (!Miners.HasComponent(building) || !StoredItems.TryGetBuffer(building, out var storedItems))
            {
                return;
            }

            FuelSlots.TryGetComponent(building, out var fuel);
            slotIndex = BeltEntryUtility.FindStorageOutputSlot(storedItems, StationStorage.HasComponent(building), fuel);
            if (slotIndex < 0)
            {
                return;
            }
            itemType = storedItems[slotIndex].ItemType;
        }

        if (BeltEntryUtility.TryFindBuildingOutputBelt(gridPos.Value, footprint.GetEffectiveSize(dir.dir),
                BeltMap, ItemMap, Directions, PendingDemolitionLookup, DestroyItemRequestLookup, BeltMovementStateLookup,
                out int2 foundBeltPos))
        {
            outputDecision = new BuildingItemOutputDecision(true, itemType, foundBeltPos)
            {
                SourceSlotIndex = slotIndex,
                FromProduct = fromProduct
            };
            outputDecisionEnabled.ValueRW = true;
        }
    }
}
