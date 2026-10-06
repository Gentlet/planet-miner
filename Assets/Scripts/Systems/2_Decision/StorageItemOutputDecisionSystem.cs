using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: Decision에서 보관품의 첫 실물을 보낼 외향 벨트 후보를 찾는다.
/// 입력·생성자: 건물 생성/입고가 유지하는 StoredItemElement, 회전 footprint와 Synchronization의 벨트/월드 실물 인덱스.
/// 출력·소유권: 건물의 BuildingItemOutputDecision과 enable 상태만 쓴다. 보관품을 제거하거나 이동시키지 않는다.
/// 이용·정리: BeltDestinationReservationSystem이 목적지 경합을 중재하고 BuildingItemStorageApplySystem이 인계 후 결정을 소비한다.
/// 건물 둘레에서 처음 가능한 벨트를 선택하며 철거 승인 건물/벨트는 제외한다. ECB 구조 변경은 없다.
/// </summary>
[UpdateInGroup(typeof(DecisionGroup))]
[BurstCompile]
public partial struct StorageItemOutputDecisionSystem : ISystem
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
            .WithAll<StoredItemElement, BuildingFootprint, GridPosition, Direction>()
            .WithNone<ProductItemElement>()
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

        var job = new StorageItemOutputDecisionJob
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
/// 일반 창고의 외부 벨트 방출 의사결정을 병렬로 계산하는 Burst Job.
/// </summary>
[BurstCompile]
public partial struct StorageItemOutputDecisionJob : IJobEntity
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
        in DynamicBuffer<StoredItemElement> storedItems,
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

        // 1. 보관 버퍼에 아이템이 전혀 없으면 방출 불가 및 비활성화
        if (storedItems.Length == 0)
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

        // 후보 하나만 선택하는 고정 둘레 순서다. 여기서 공간을 확보하지 않고 Reservation이 다른 발신자와 중재한다.
        // 2. 건물 둘레 순회 (하단 -> 우측 -> 상단 -> 좌측)
        // 하단 변 (y = anchor.y - 1)
        for (int x = 0; x < effectiveSize.x && !foundBelt; x++)
        {
            int2 p = new int2(anchor.x + x, anchor.y - 1);
            CheckPerimeterCell(p, anchor, effectiveSize, ref foundBeltPos, ref foundBelt);
        }

        // 우측 변 (x = anchor.x + effectiveSize.x)
        for (int y = 0; y < effectiveSize.y && !foundBelt; y++)
        {
            int2 p = new int2(anchor.x + effectiveSize.x, anchor.y + y);
            CheckPerimeterCell(p, anchor, effectiveSize, ref foundBeltPos, ref foundBelt);
        }

        // 상단 변 (y = anchor.y + effectiveSize.y)
        for (int x = 0; x < effectiveSize.x && !foundBelt; x++)
        {
            int2 p = new int2(anchor.x + x, anchor.y + effectiveSize.y);
            CheckPerimeterCell(p, anchor, effectiveSize, ref foundBeltPos, ref foundBelt);
        }

        // 좌측 변 (x = anchor.x - 1)
        for (int y = 0; y < effectiveSize.y && !foundBelt; y++)
        {
            int2 p = new int2(anchor.x - 1, anchor.y + y);
            CheckPerimeterCell(p, anchor, effectiveSize, ref foundBeltPos, ref foundBelt);
        }

        // 3. 의사결정 기록
        if (foundBelt)
        {
            // 첫 보관 실물을 출고 후보로 지정한다. 실제 제거/이동은 Reservation 승인 뒤 Apply가 수행한다.
            outputDecision.CanOutput = true;
            outputDecision.ItemToOutput = storedItems[0].ItemEntity;
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

        // 외향 벨트 판정: 벨트의 이전 타일이 건물 내부인가?
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
