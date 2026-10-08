using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: Decision에서 채굴 대상과 생산 버퍼 여유를 검사한다.
/// 입력·생성자: Synchronization의 자원 인덱스, ResourceNode, 기존 생산품/미소비 결과와 ItemRegistry의 스택 한도.
/// 출력·소유권: 채굴기의 MinerDecision과 enable 상태만 쓴다. 회전 footprint에서 처음 유효한 자원을 선택한다.
/// 이용: MinerExecutionSystem이 진행도·자원량·ProductResult를 반영하고 ItemLifecycleApplySystem이 실물을 생성한다.
/// 정리·가시화: 결정은 다음 Decision에서 갱신한다. 철거 승인/미소비 생산 결과/품목 불일치는 새 생산을 차단하며 ECB 기록은 없다.
/// </summary>
[UpdateInGroup(typeof(BuildingDecisionGroup))]
[BurstCompile]
public partial struct MinerDecisionSystem : ISystem
{
    private ComponentLookup<ResourceNode> _resourceNodeLookup;
    private ComponentLookup<PendingBuildingDemolition> _pendingDemolitionLookup;
    private BufferLookup<ItemConfigElement> _itemConfigLookup;
    private EntityQuery _minerQuery;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        _resourceNodeLookup = state.GetComponentLookup<ResourceNode>(true);
        _pendingDemolitionLookup = state.GetComponentLookup<PendingBuildingDemolition>(true);
        _itemConfigLookup = state.GetBufferLookup<ItemConfigElement>(true);

        _minerQuery = SystemAPI.QueryBuilder()
            .WithAllRW<MinerDecision>()
            .WithAll<MinerState, BuildingFootprint, GridPosition, Direction, ProductItemElement, ProductResult>()
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
        _pendingDemolitionLookup.Update(ref state);
        _itemConfigLookup.Update(ref state);

        Entity itemRegistryEntity = Entity.Null;
        if (SystemAPI.HasSingleton<ItemRegistry>())
        {
            itemRegistryEntity = SystemAPI.GetSingletonEntity<ItemRegistry>();
        }

        var job = new MinerDecisionJob
        {
            ResourceMap = resIndex.Map,
            ResourceNodeLookup = _resourceNodeLookup,
            PendingDemolitionLookup = _pendingDemolitionLookup,
            ItemRegistryEntity = itemRegistryEntity,
            ItemConfigLookup = _itemConfigLookup
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
    public ComponentLookup<PendingBuildingDemolition> PendingDemolitionLookup;

    public Entity ItemRegistryEntity;

    [ReadOnly]
    public BufferLookup<ItemConfigElement> ItemConfigLookup;

    public void Execute(
        Entity building,
        ref MinerDecision decision,
        EnabledRefRW<MinerDecision> decisionEnabled,
        in DynamicBuffer<ProductItemElement> productItems,
        in DynamicBuffer<ProductResult> productResults,
        in MinerState state,
        in BuildingFootprint footprint,
        in GridPosition gridPos,
        in Direction dir)
    {
        if (PendingDemolitionLookup.HasComponent(building))
        {
            decision.CanMine = false;
            decision.TargetResource = Entity.Null;
            decisionEnabled.ValueRW = false;
            return;
        }

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
        int maxStack = 0;
        if (resourceType != ItemTypeEnum.None && ItemConfigLookup.HasBuffer(ItemRegistryEntity))
        {
            maxStack = ItemRegistry.GetMaxStack(ItemConfigLookup[ItemRegistryEntity], resourceType);
        }

        bool sameItemType = true;
        for (int i = 0; i < productItems.Length; i++)
        {
            if (productItems[i].ItemType != resourceType)
            {
                sameItemType = false;
                break;
            }
        }

        // 결과가 실물로 바뀌기 전에는 출력 버퍼에 아직 보이지 않는다. 미소비 결과를 별도로 막아 중복 생산을 방지한다.
        bool hasPendingProduction = productResults.Length > 0;
        bool hasSpace = !hasPendingProduction && sameItemType && productItems.Length < maxStack;

        // 3. 의사결정 기록 (자원이 있고, 동일 타입 1스택에 여유가 있으며, 미소비 생산 결과가 없을 때만 채굴 가능)
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
