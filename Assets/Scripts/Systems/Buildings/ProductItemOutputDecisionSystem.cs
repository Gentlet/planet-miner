using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 채굴기, 제작기 등 생산 건물 내부에 완성된 생산물(ProductItemElement)의 외부 벨트 방출 적합성을 판정하는 시스템.
/// 
/// [책임]
/// - DecisionGroup(Phase 2)에서 실행.
/// - 생산물 버퍼(ProductItemElement)에 아이템이 존재하는 생산 건물을 탐색.
/// - 건물 둘레 타일(Perimeter)을 순회하여 건물 외부로 향하는 외향 벨트(Outward Belt)를 감지.
/// - 외향 벨트 시작점(Progress = 0.0f)에 ItemSpacing 이상의 여유 공간이 확보되었는지 확인.
/// - 방출 조건 충족 시 주완성품(Slot 0) 우선 또는 FIFO 순서로 방출 아이템을 결정하고 BuildingItemOutputDecision을 활성화.
/// - [상태-의사결정 분리]: 버퍼에서 아이템을 제거하지 않으며, 순수 의사결정 컴포넌트만 갱신.
/// </summary>
[UpdateInGroup(typeof(DecisionGroup))]
[BurstCompile]
public partial struct ProductItemOutputDecisionSystem : ISystem
{
    private ComponentLookup<BeltMovementState> _beltMovementStateLookup;
    private ComponentLookup<ItemOwnership> _itemOwnershipLookup;
    private EntityQuery _buildingQuery;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        _beltMovementStateLookup = state.GetComponentLookup<BeltMovementState>(true);
        _itemOwnershipLookup = state.GetComponentLookup<ItemOwnership>(true);

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

        var job = new ProductItemOutputDecisionJob
        {
            BeltMap = beltIndex.Map,
            ItemMap = itemIndex.Map,
            BeltMovementStateLookup = _beltMovementStateLookup,
            ItemOwnershipLookup = _itemOwnershipLookup
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

    public void Execute(
        ref BuildingItemOutputDecision outputDecision,
        EnabledRefRW<BuildingItemOutputDecision> outputDecisionEnabled,
        in DynamicBuffer<ProductItemElement> productItems,
        in BuildingFootprint footprint,
        in GridPosition gridPos,
        in Direction dir)
    {
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

        int2 prevPos = cell - beltInfo.Direction.ToInt2();
        if (!IsInsideBuilding(prevPos, anchor, size))
        {
            return;
        }

        bool spaceAvailable = true;
        if (ItemMap.TryGetFirstValue(cell, out Entity itemEntity, out var it))
        {
            float minProgress = float.MaxValue;
            int cellItemCount = 0;
            do
            {
                if (ItemOwnershipLookup.HasComponent(itemEntity) &&
                    ItemOwnershipLookup[itemEntity].IsWorldItem &&
                    BeltMovementStateLookup.HasComponent(itemEntity) &&
                    BeltMovementStateLookup.IsComponentEnabled(itemEntity))
                {
                    float prog = BeltMovementStateLookup[itemEntity].Progress;
                    minProgress = math.min(minProgress, prog);
                    cellItemCount++;
                }
            } while (ItemMap.TryGetNextValue(out itemEntity, ref it));

            if (cellItemCount >= GameConstants.MaxItemsPerBeltTile || minProgress < GameConstants.ItemSpacing - GameConstants.AlignmentEpsilon)
            {
                spaceAvailable = false;
            }
        }

        if (spaceAvailable)
        {
            foundBeltPos = cell;
            foundBelt = true;
        }
    }

    private static bool IsInsideBuilding(int2 pos, int2 anchor, int2 size)
    {
        return pos.x >= anchor.x && pos.x < anchor.x + size.x &&
               pos.y >= anchor.y && pos.y < anchor.y + size.y;
    }
}
