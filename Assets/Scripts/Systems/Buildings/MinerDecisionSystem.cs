using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 채굴기의 채굴 조건(하부 자원 실존 및 내부 버퍼 여유 공간)을 판정하는 시스템.
/// 
/// [책임]
/// - DecisionGroup(Phase 2)에서 실행됩니다.
/// - 채굴기의 Footprint 하부 타일을 순회하여 ResourceSpatialIndex에서 유효 자원(Amount > 0)을 탐색합니다.
///   (다중 타일 채굴기의 경우 좌하단 Anchor 기준 첫 번째 자원을 우선 선택합니다.)
/// - 채굴기 내부 생산물 버퍼(DynamicBuffer<ProductItemElement>)의 적재 수량과 해당 자원의 1스택 한도(ItemRegistry.MaxStack)를 비교하여 여유 공간을 검사합니다.
/// - 하부 자원이 존재하고 내부 버퍼에 공간이 확보되어 있으면 CanMine = true를 기록하고 활성화합니다.
/// - 자원이 없거나 내부 버퍼가 가득 찬 경우 CanMine = false로 설정하고 비활성화합니다.
/// - [상태-의사결정 분리]: 채굴 진행도를 누적하거나 아이템을 생성하지 않으며, 순수 의사결정 컴포넌트(MinerDecision)만 갱신합니다.
/// - 외부 벨트로의 방출은 채굴기에 부착된 BuildingItemOutputDecision과 출고 시스템(ProductItemOutputDecisionSystem)이 전담합니다.
/// </summary>
[UpdateInGroup(typeof(DecisionGroup))]
[BurstCompile]
public partial struct MinerDecisionSystem : ISystem
{
    private ComponentLookup<ResourceNode> _resourceNodeLookup;
    private EntityQuery _minerQuery;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        _resourceNodeLookup = state.GetComponentLookup<ResourceNode>(true);

        _minerQuery = SystemAPI.QueryBuilder()
            .WithAllRW<MinerDecision>()
            .WithAll<MinerState, BuildingFootprint, GridPosition, Direction, ProductItemElement>()
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
        if (!SystemAPI.HasSingleton<ResourceSpatialIndex>() ||
            !SystemAPI.HasSingleton<ResourceSpatialIndexFence>())
        {
            return;
        }

        var resIndex = SystemAPI.GetSingleton<ResourceSpatialIndex>();
        ref var resFence = ref SystemAPI.GetSingletonRW<ResourceSpatialIndexFence>().ValueRW;

        _resourceNodeLookup.Update(ref state);

        ItemRegistry itemRegistry = default;
        if (SystemAPI.HasSingleton<ItemRegistry>())
        {
            itemRegistry = SystemAPI.GetSingleton<ItemRegistry>();
        }

        var job = new MinerDecisionJob
        {
            ResourceMap = resIndex.Map,
            ResourceNodeLookup = _resourceNodeLookup,
            ItemRegistry = itemRegistry
        };

        var jobDep = Unity.Jobs.JobHandle.CombineDependencies(state.Dependency, resFence.GetReaderDependency());
        var jobHandle = job.ScheduleParallel(_minerQuery, jobDep);

        resFence.AddReader(jobHandle);

        state.Dependency = jobHandle;
    }
}

/// <summary>
/// 각 채굴기의 채굴 조건을 병렬로 판정하는 Burst Job.
/// </summary>
[BurstCompile]
public partial struct MinerDecisionJob : IJobEntity
{
    [ReadOnly]
    public NativeParallelHashMap<int2, Entity> ResourceMap;

    [ReadOnly]
    public ComponentLookup<ResourceNode> ResourceNodeLookup;

    [ReadOnly]
    public ItemRegistry ItemRegistry;

    public void Execute(
        ref MinerDecision decision,
        EnabledRefRW<MinerDecision> decisionEnabled,
        in DynamicBuffer<ProductItemElement> productItems,
        in MinerState state,
        in BuildingFootprint footprint,
        in GridPosition gridPos,
        in Direction dir)
    {
        int2 anchor = gridPos.Value;
        int2 effectiveSize = footprint.GetEffectiveSize(dir.dir);

        // 1. 하부 자원 탐색 (다중 타일 시 좌하단 (0,0)부터 순회하여 첫 번째 유효 자원 선택)
        Entity targetResource = Entity.Null;
        ItemTypeEnum resourceType = ItemTypeEnum.None;
        bool foundResource = false;

        for (int y = 0; y < effectiveSize.y && !foundResource; y++)
        {
            for (int x = 0; x < effectiveSize.x && !foundResource; x++)
            {
                int2 cell = anchor + new int2(x, y);
                if (ResourceMap.TryGetValue(cell, out Entity resEntity) && resEntity != Entity.Null)
                {
                    if (ResourceNodeLookup.HasComponent(resEntity) && ResourceNodeLookup[resEntity].Amount > 0)
                    {
                        targetResource = resEntity;
                        resourceType = ResourceNodeLookup[resEntity].ResourceType;
                        foundResource = true;
                    }
                }
            }
        }

        // 2. 내부 출력 버퍼(ProductItemElement) 여유 공간 확인 (1스택 한도)
        int maxStack = 50;
        if (ItemRegistry.Value.IsCreated && resourceType != ItemTypeEnum.None)
        {
            maxStack = ItemRegistry.Value.Value.GetMaxStack(resourceType);
        }

        bool hasSpace = productItems.Length < maxStack;

        // 3. 의사결정 기록 (자원이 있고 버퍼에 여유가 있을 때만 채굴 가능 및 활성화)
        if (foundResource && hasSpace)
        {
            decision.CanMine = true;
            decision.TargetResource = targetResource;
            decisionEnabled.ValueRW = true;
        }
        else
        {
            decision.CanMine = false;
            decision.TargetResource = foundResource ? targetResource : Entity.Null;
            decisionEnabled.ValueRW = false;
        }
    }
}
