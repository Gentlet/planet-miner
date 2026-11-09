using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: 남은 현장의 바닥 정리 상태와 실제 도착 자재로 완공 여부를 판단한다. 취소는 Command에서 먼저 확정된다.
/// 처리 단계: BuildingStateApply 마지막. 지난 틱 드론 결과와 이번 건물 처리의 현재 Owner/GridPosition/활성 Destroy를 읽는다.
/// 출력·소유권: AwaitingItemClearance를 갱신하고 공통 SpawnBuilding으로 완공 건물을 직접 생성한다. 공급/예약 정산은 드론 관리 소유자에게 둔다.
/// 정리·가시화: 생성 성공분만 자재/현장 삭제를 EndBuilding에 기록한다. 생성 실패 시 보존하고 공간 인덱스는 Synchronization에서 갱신한다.
/// 현장 내부 새 World Spawn/드론 방출은 해당 생산 경계가 막으므로 예정 월드 위치를 별도 원본으로 보관하지 않는다.
/// </summary>
[UpdateInGroup(typeof(BuildingStateApplyGroup), OrderLast = true)]
public partial struct ConstructionLifecycleApplySystem : ISystem
{
    private EntityQuery _siteQuery;
    private EntityQuery _prefabDbQuery;
    private EntityQuery _buildingConfigQuery;
    private EntityQuery _worldItemQuery;
    private ComponentLookup<DestroyItemRequest> _destroyRequestLookup;

    private BufferLookup<StoredItemElement> _storedBufferLookup;
    private ComponentLookup<PlacementStamp> _stampLookup;
    private BufferLookup<BuildingPrefabElement> _prefabBufferLookup;
    private BufferLookup<BuildingConfigElement> _configBufferLookup;

    public void OnCreate(ref SystemState state)
    {
        _siteQuery = SystemAPI.QueryBuilder()
            .WithAllRW<ConstructionSite>()
            .WithAll<GridPosition, Direction, BuildingFootprint>()
            .WithAll<ConstructionMaterialRequirementElement>()
            .Build();

        _prefabDbQuery = SystemAPI.QueryBuilder()
            .WithAll<BuildingPrefabElement>()
            .Build();

        _buildingConfigQuery = SystemAPI.QueryBuilder()
            .WithAll<BuildingConfig, BuildingConfigElement>()
            .Build();

        _worldItemQuery = state.GetEntityQuery(ComponentType.ReadOnly<ItemIdentity>(),
            ComponentType.ReadOnly<ItemOwnership>(), ComponentType.ReadOnly<GridPosition>());
        _destroyRequestLookup = state.GetComponentLookup<DestroyItemRequest>(true);

        _storedBufferLookup = state.GetBufferLookup<StoredItemElement>(true);
        _stampLookup = state.GetComponentLookup<PlacementStamp>(true);
        _prefabBufferLookup = state.GetBufferLookup<BuildingPrefabElement>(true);
        _configBufferLookup = state.GetBufferLookup<BuildingConfigElement>(true);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
    }

    public void OnUpdate(ref SystemState state)
    {
        if (_siteQuery.IsEmptyIgnoreFilter)
        {
            return;
        }

        var ecbSystem = state.World.GetOrCreateSystemManaged<EndBuildingEntityCommandBufferSystem>();
        var ecb = ecbSystem.CreateCommandBuffer();

        _storedBufferLookup.Update(ref state);
        _stampLookup.Update(ref state);
        _prefabBufferLookup.Update(ref state);
        _configBufferLookup.Update(ref state);
        _destroyRequestLookup.Update(ref state);
        // 일반 Ownership과 지난 틱 드론 실물 인계가 반영된 원본을 모은다. 공간 인덱스로 완공을 판단하지 않는다.
        // Job에는 이번 검사 동안만 쓰는 위치 복사본을 전달하고 완료 후 Dispose한다.
        state.CompleteDependency();
        var worldItemPositions = CollectWorldItemPositions(ref state);

        bool hasPrefabDb = !_prefabDbQuery.IsEmptyIgnoreFilter;
        Entity prefabDbEntity = hasPrefabDb ? _prefabDbQuery.GetSingletonEntity() : Entity.Null;

        bool hasConfig = !_buildingConfigQuery.IsEmptyIgnoreFilter;
        Entity configEntity = hasConfig ? _buildingConfigQuery.GetSingletonEntity() : Entity.Null;

        var completionJob = new ConstructionCompletionApplyJob
        {
            EntityHandle = SystemAPI.GetEntityTypeHandle(),
            ConstructionSiteHandle = SystemAPI.GetComponentTypeHandle<ConstructionSite>(false),
            GridPositionHandle = SystemAPI.GetComponentTypeHandle<GridPosition>(true),
            DirectionHandle = SystemAPI.GetComponentTypeHandle<Direction>(true),
            BuildingFootprintHandle = SystemAPI.GetComponentTypeHandle<BuildingFootprint>(true),
            ConstructionMaterialRequirementElementHandle = SystemAPI.GetBufferTypeHandle<ConstructionMaterialRequirementElement>(true),
            ECB = ecb,
            HasPrefabDb = hasPrefabDb,
            PrefabDbEntity = prefabDbEntity,
            HasConfig = hasConfig,
            ConfigEntity = configEntity,
            StoredLookup = _storedBufferLookup,
            StampLookup = _stampLookup,
            PrefabBufferLookup = _prefabBufferLookup,
            ConfigBufferLookup = _configBufferLookup,
            PlannedSettings = SystemAPI.GetComponentLookup<PlannedBuildingSettings>(true),
            WorldItemPositions = worldItemPositions.AsArray()
        };
        var completionHandle = completionJob.Schedule(_siteQuery, state.Dependency);
        state.Dependency = worldItemPositions.Dispose(completionHandle);
        ecbSystem.AddJobHandleForProducer(state.Dependency);
    }

    private NativeList<int2> CollectWorldItemPositions(ref SystemState state)
    {
        var manager = state.EntityManager;
        var positions = new NativeList<int2>(Allocator.TempJob);
        using var items = _worldItemQuery.ToEntityArray(Allocator.Temp);
        for (int i = 0; i < items.Length; i++)
        {
            Entity item = items[i];
            // EndBuilding에서 삭제될 활성 Destroy 실물은 더 이상 바닥 정리를 막지 않는다.
            // 수납 아이템의 GridPosition은 월드에 보이는 실물이 아니므로 Owner=Null만 포함한다.
            if (IsPendingItemDestruction(item)) continue;
            if (!manager.GetComponentData<ItemOwnership>(item).IsWorldItem) continue;
            positions.Add(manager.GetComponentData<GridPosition>(item).Value);
        }

        return positions;
    }

    private bool IsPendingItemDestruction(Entity item)
    {
        return _destroyRequestLookup.HasComponent(item) && _destroyRequestLookup.IsComponentEnabled(item);
    }
}

/// <summary>
/// 완공 조건을 만족한 공사 현장을 감지하여 자재를 소비하고 완공 건물을 직접 생성하는 단일 워커 Burst Job.
/// </summary>
[BurstCompile]
public partial struct ConstructionCompletionApplyJob : IJobChunk
{
    [ReadOnly] public EntityTypeHandle EntityHandle;
    public ComponentTypeHandle<ConstructionSite> ConstructionSiteHandle;
    [ReadOnly] public ComponentTypeHandle<GridPosition> GridPositionHandle;
    [ReadOnly] public ComponentTypeHandle<Direction> DirectionHandle;
    [ReadOnly] public ComponentTypeHandle<BuildingFootprint> BuildingFootprintHandle;
    [ReadOnly] public BufferTypeHandle<ConstructionMaterialRequirementElement> ConstructionMaterialRequirementElementHandle;

    private struct RegistryReadContext
    {
        public BuildingLifecycleUtility.SpawnRegistryReadContext Spawn;
    }

    public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
    {
        var entities = chunk.GetNativeArray(EntityHandle);
        var constructionSiteValues = chunk.GetNativeArray(ref ConstructionSiteHandle);
        var gridPositionValues = chunk.GetNativeArray(ref GridPositionHandle);
        var directionValues = chunk.GetNativeArray(ref DirectionHandle);
        var buildingFootprintValues = chunk.GetNativeArray(ref BuildingFootprintHandle);
        var constructionMaterialRequirementElementValues = chunk.GetBufferAccessorRO(ref ConstructionMaterialRequirementElementHandle);
        var registryRead = new RegistryReadContext();
        var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
        while (enumerator.NextEntityIndex(out int index))
        {
            var siteRef = new RefRW<ConstructionSite>(constructionSiteValues, index);
            var posValue = gridPositionValues[index];
            var dirValue = directionValues[index];
            var footprintValue = buildingFootprintValues[index];
            var requirementsValue = constructionMaterialRequirementElementValues[index];
            ExecuteEntity(entities[index],
                ref siteRef.ValueRW,
                in posValue,
                in dirValue,
                in footprintValue,
                in requirementsValue, ref registryRead);
        }
    }

    [ReadOnly] public ComponentLookup<PlannedBuildingSettings> PlannedSettings;
    public EntityCommandBuffer ECB;
    public bool HasPrefabDb;
    public Entity PrefabDbEntity;
    public bool HasConfig;
    public Entity ConfigEntity;

    [ReadOnly]
    public NativeArray<int2> WorldItemPositions;

    [ReadOnly]
    public BufferLookup<StoredItemElement> StoredLookup;

    [ReadOnly]
    public ComponentLookup<PlacementStamp> StampLookup;

    [ReadOnly]
    public BufferLookup<BuildingPrefabElement> PrefabBufferLookup;

    [ReadOnly]
    public BufferLookup<BuildingConfigElement> ConfigBufferLookup;

    private void ExecuteEntity(
        Entity siteEntity,
        ref ConstructionSite site,
        in GridPosition pos,
        in Direction dir,
        in BuildingFootprint footprint,
        in DynamicBuffer<ConstructionMaterialRequirementElement> requirements, ref RegistryReadContext registryRead)
    {
        // 1. 현장 수명 동안 최신 월드 실물의 차단을 재검사한다.
        if ((site.Flags & ConstructionSiteFlags.Cancelled) != 0)
        {
            return;
        }

        int2 size = BuildingFootprintUtility.GetEffectiveSize(footprint.Size, dir.dir);
        bool hasWorldItem = ConstructionCompletionUtility.HasBlockingWorldItem(pos.Value, size, WorldItemPositions);

        if (hasWorldItem)
        {
            site.Flags |= ConstructionSiteFlags.AwaitingItemClearance;
            return;
        }

        // 최신 바닥 차단을 다시 검사한다. 뒤의 드론 단계가 마지막 실물을 회수하면 다음 틱 완공 검사에 반영한다.
        site.Flags &= ~ConstructionSiteFlags.AwaitingItemClearance;

        // 2. 무효하거나 공사 현장 타입인 경우 완공 불가
        if (site.TargetBuildingType == BuildingTypeEnum.None || site.TargetBuildingType == BuildingTypeEnum.ConstructionSite)
        {
            return;
        }

        // 예약량은 도착량이 아니다. 실제 인계 성공분으로 갱신된 DeliveredQuantity만 완공 조건으로 사용한다.
        if (!ConstructionCompletionUtility.AreMaterialsSatisfied(requirements))
        {
            return;
        }

        // 4. 완공 건물 엔티티 인스턴스화 및 컴포넌트 초기화 (BuildingLifecycleUtility 활용)
        StampLookup.TryGetComponent(siteEntity, out var stamp);

        Entity building = BuildingLifecycleUtility.SpawnBuilding(
            ref ECB,
            site.TargetBuildingType,
            site.TargetTier,
            pos.Value,
            dir.dir,
            footprint.Size,
            stamp,
            HasPrefabDb,
            PrefabDbEntity,
            HasConfig,
            ConfigEntity,
            in PrefabBufferLookup,
            in ConfigBufferLookup,
            ref registryRead.Spawn
        );

        if (building == Entity.Null)
        {
            // 건물 생성이 거부되면 자재와 현장을 보존한다.
            return;
        }

        if (PlannedSettings.TryGetComponent(siteEntity, out var settings))
        {
            if (settings.HasStorageFilter)
            {
                // 일반 저장소는 생성과 같은 공개 경계에서 필터를 갖추어 이번 틱 드론 입고도 검사받는다.
                ECB.SetComponent(building, settings.Filter);
            }
            if (settings.HasRecipe)
            {
                // 새 제작기는 다음 Command에서 기존 레시피 검증/입력 구성 경로를 사용한다.
                Entity request = ECB.CreateEntity();
                ECB.AddComponent(request, new ChangeCrafterRecipeRequest(building, settings.RecipeId));
            }
        }

        // 생성 명령이 확보된 뒤에만 보관 실물과 현장을 소비한다. 프리팹 실패가 자재 손실로 이어지지 않게 한다.
        if (StoredLookup.TryGetBuffer(siteEntity, out var storedItems))
        {
            for (int i = 0; i < storedItems.Length; i++)
            {
                Entity itemEntity = storedItems[i].ItemEntity;
                if (itemEntity != Entity.Null)
                {
                    ECB.DestroyEntity(itemEntity);
                }
            }
        }

        // 6. 공사 현장 엔티티 파괴 (동일 ECB Playback 틱에 발생하므로 점유 공백 0 달성)
        ECB.DestroyEntity(siteEntity);
    }
}
