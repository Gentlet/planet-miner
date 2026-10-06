using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: Decision에서 생산품을 외향 벨트로 내보낼 후보를 찾는다.
/// 입력·생성자: ItemLifecycleApplySystem의 ProductItemElement, 회전 footprint와 Synchronization의 벨트/월드 실물 인덱스.
/// 출력·소유권: 건물의 BuildingItemOutputDecision만 갱신하며 슬롯 0 주생산품을 우선, 없으면 첫 생산품을 고른다.
/// 이용·정리: BeltDestinationReservationSystem이 목적지 경합을 중재하고 BuildingItemStorageApplySystem이 인계/결정 소비를 수행한다.
/// 철거 승인 건물/벨트는 제외한다. 생산 버퍼·소유권 원본 및 ECB는 변경하지 않는다.
/// </summary>
[UpdateInGroup(typeof(BuildingDecisionGroup))]
[BurstCompile]
public partial struct ProductItemOutputDecisionSystem : ISystem
{
    private ComponentLookup<BeltMovementState> _beltMovementStateLookup;
    private ComponentLookup<ItemOwnership> _itemOwnershipLookup;
    private ComponentLookup<PendingBuildingDemolition> _pendingDemolitionLookup;
    private EntityQuery _buildingQuery;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        _beltMovementStateLookup = state.GetComponentLookup<BeltMovementState>(true);
        _itemOwnershipLookup = state.GetComponentLookup<ItemOwnership>(true);
        _pendingDemolitionLookup = state.GetComponentLookup<PendingBuildingDemolition>(true);

        _buildingQuery = SystemAPI.QueryBuilder()
            .WithAllRW<BuildingItemOutputDecision>()
            .WithAll<ProductItemElement, BuildingFootprint, GridPosition, Direction>()
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
        _pendingDemolitionLookup.Update(ref state);

        var job = new ProductItemOutputDecisionJob
        {
            BeltMap = beltIndex.Map,
            ItemMap = itemIndex.Map,
            BeltMovementStateLookup = _beltMovementStateLookup,
            ItemOwnershipLookup = _itemOwnershipLookup,
            PendingDemolitionLookup = _pendingDemolitionLookup
        };

        var readDep = Unity.Jobs.JobHandle.CombineDependencies(beltFence.GetReaderDependency(), itemFence.GetReaderDependency());
        var jobDep = Unity.Jobs.JobHandle.CombineDependencies(state.Dependency, readDep);

        var jobHandle = job.ScheduleParallel(_buildingQuery, jobDep);

        beltFence.AddReader(jobHandle);
        itemFence.AddReader(jobHandle);

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

    [ReadOnly]
    public NativeParallelMultiHashMap<int2, Entity> ItemMap;

    [ReadOnly]
    public ComponentLookup<BeltMovementState> BeltMovementStateLookup;

    [ReadOnly]
    public ComponentLookup<ItemOwnership> ItemOwnershipLookup;

    [ReadOnly]
    public ComponentLookup<PendingBuildingDemolition> PendingDemolitionLookup;

    public void Execute(
        Entity building,
        ref BuildingItemOutputDecision outputDecision,
        EnabledRefRW<BuildingItemOutputDecision> outputDecisionEnabled,
        in DynamicBuffer<ProductItemElement> productItems,
        in BuildingFootprint footprint,
        in GridPosition gridPos,
        in Direction dir)
    {
        if (PendingDemolitionLookup.HasComponent(building))
        {
            outputDecision.CanOutput = false;
            outputDecision.ItemToOutput = Entity.Null;
            outputDecision.TargetBeltPosition = int2.zero;
            outputDecisionEnabled.ValueRW = false;
            return;
        }

        // 1. 생산물 버퍼에 아이템이 전혀 없으면 방출 불가 및 비활성화
        if (productItems.Length == 0)
        {
            outputDecision.CanOutput = false;
            outputDecision.ItemToOutput = Entity.Null;
            outputDecision.TargetBeltPosition = int2.zero;
            outputDecisionEnabled.ValueRW = false;
            return;
        }

        int2 anchor = gridPos.Value;
        int2 effectiveSize = footprint.GetEffectiveSize(dir.dir);

        int2 foundBeltPos = int2.zero;
        bool foundBelt = false;

        // 생산품 선택과 목적지 경합 승인은 별개다. 첫 가용 벨트 후보만 남기고 수용 공간 경합은 Reservation에 넘긴다.
        // 2. 건물 둘레 순회 (하단 -> 우측 -> 상단 -> 좌측)
        for (int x = 0; x < effectiveSize.x && !foundBelt; x++)
        {
            int2 p = new int2(anchor.x + x, anchor.y - 1);
            CheckPerimeterCell(p, anchor, effectiveSize, ref foundBeltPos, ref foundBelt);
        }

        for (int y = 0; y < effectiveSize.y && !foundBelt; y++)
        {
            int2 p = new int2(anchor.x + effectiveSize.x, anchor.y + y);
            CheckPerimeterCell(p, anchor, effectiveSize, ref foundBeltPos, ref foundBelt);
        }

        for (int x = 0; x < effectiveSize.x && !foundBelt; x++)
        {
            int2 p = new int2(anchor.x + x, anchor.y + effectiveSize.y);
            CheckPerimeterCell(p, anchor, effectiveSize, ref foundBeltPos, ref foundBelt);
        }

        for (int y = 0; y < effectiveSize.y && !foundBelt; y++)
        {
            int2 p = new int2(anchor.x - 1, anchor.y + y);
            CheckPerimeterCell(p, anchor, effectiveSize, ref foundBeltPos, ref foundBelt);
        }

        // 3. 의사결정 기록
        if (foundBelt)
        {
            // 방출 대상 선정: 주 완성품(Slot 0) 우선 탐색 후 없으면 FIFO 0번 선택
            int selectedIndex = 0;
            for (int i = 0; i < productItems.Length; i++)
            {
                if (productItems[i].SlotIndex == 0)
                {
                    selectedIndex = i;
                    break;
                }
            }

            outputDecision.CanOutput = true;
            outputDecision.ItemToOutput = productItems[selectedIndex].ItemEntity;
            outputDecision.TargetBeltPosition = foundBeltPos;
            outputDecisionEnabled.ValueRW = true;
        }
        else
        {
            // 외향 벨트가 없거나 여유 공간 미확보: 방출 대기
            outputDecision.CanOutput = false;
            outputDecision.ItemToOutput = Entity.Null;
            outputDecision.TargetBeltPosition = int2.zero;
            outputDecisionEnabled.ValueRW = false;
        }
    }

    private void CheckPerimeterCell(
        int2 cell,
        int2 anchor,
        int2 size,
        ref int2 foundBeltPos,
        ref bool foundBelt)
    {
        if (!BeltMap.TryGetValue(cell, out BeltInfo beltInfo))
        {
            return;
        }

        if (PendingDemolitionLookup.HasComponent(beltInfo.Entity))
        {
            return;
        }

        int2 prevPos = cell - beltInfo.Direction.ToInt2();
        if (!IsInsideBuilding(prevPos, anchor, size))
        {
            return;
        }

        if (!BeltEntryUtility.HasEntrySpace(
                cell, ItemMap, ItemOwnershipLookup, BeltMovementStateLookup))
        {
            return;
        }

        foundBeltPos = cell;
        foundBelt = true;
    }

    private static bool IsInsideBuilding(int2 pos, int2 anchor, int2 size)
    {
        return pos.x >= anchor.x && pos.x < anchor.x + size.x &&
               pos.y >= anchor.y && pos.y < anchor.y + size.y;
    }
}
