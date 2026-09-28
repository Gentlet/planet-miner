using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 공사 완료 판정 및 완공 건물 전환 전담 시스템.
/// 
/// [책임]
/// - StateApplyGroup(Phase 5)에서 실행:
///   [UpdateAfter(typeof(ConstructionMaterialApplySystem))]
///   [UpdateBefore(typeof(BuildingLifecycleApplySystem))]
/// - 모든 자재가 충족되고(DeliveredQuantity >= RequiredQuantity), 바닥 아이템 청소 플래그(AwaitingItemClearance)가 꺼진 현장을 감지.
/// - 현장에 보관된 건설 자재(StoredItemElement)를 원자적으로 소비(DestroyEntity).
/// - BuildingLifecycleUtility를 통해 완공 건물을 단일 ECB 트랜잭션에서 인스턴스화하고 속성(타입, 위치, 방향, 크기, PlacementStamp)을 100% 승계.
/// - 공사 현장 엔티티(siteEntity)를 파괴(DestroyEntity).
/// - 현장 파괴와 완공 건물 생성이 동일한 ECB Playback 틱에 원자적으로 이루어지므로 점유 공백(Spatial Void) 0 보장.
/// </summary>
[UpdateInGroup(typeof(StateApplyGroup))]
[UpdateAfter(typeof(ConstructionMaterialApplySystem))]
[UpdateAfter(typeof(ConstructionCancelApplySystem))]
[UpdateBefore(typeof(BuildingLifecycleApplySystem))]
public partial struct ConstructionCompletionApplySystem : ISystem
{
    private EntityArchetype _fallbackBuildingArchetype;
    private EntityQuery _siteQuery;
    private EntityQuery _prefabDbQuery;
    private EntityQuery _buildingConfigQuery;
    private BufferLookup<StoredItemElement> _storedLookup;
    private ComponentLookup<PlacementStamp> _stampLookup;
    private BufferLookup<BuildingPrefabElement> _prefabBufferLookup;
    private BufferLookup<BuildingConfigElement> _configBufferLookup;

    public void OnCreate(ref SystemState state)
    {
        _fallbackBuildingArchetype = state.EntityManager.CreateArchetype(
            ComponentType.ReadWrite<BuildingType>(),
            ComponentType.ReadWrite<BuildingFootprint>(),
            ComponentType.ReadWrite<GridPosition>(),
            ComponentType.ReadWrite<Direction>(),
            ComponentType.ReadWrite<PlacementStamp>(),
            ComponentType.ReadWrite<Unity.Transforms.LocalTransform>()
        );

        _siteQuery = SystemAPI.QueryBuilder()
            .WithAll<ConstructionSite, GridPosition, Direction, BuildingFootprint>()
            .WithAll<ConstructionMaterialRequirementElement>()
            .Build();

        _prefabDbQuery = SystemAPI.QueryBuilder()
            .WithAll<BuildingPrefabElement>()
            .Build();

        _buildingConfigQuery = SystemAPI.QueryBuilder()
            .WithAll<BuildingConfig, BuildingConfigElement>()
            .Build();

        _storedLookup = state.GetBufferLookup<StoredItemElement>(true);
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

        var ecbSystem = state.World.GetOrCreateSystemManaged<EndStateApplyEntityCommandBufferSystem>();
        var ecb = ecbSystem.CreateCommandBuffer();

        _storedLookup.Update(ref state);
        _stampLookup.Update(ref state);
        _prefabBufferLookup.Update(ref state);
        _configBufferLookup.Update(ref state);

        bool hasPrefabDb = !_prefabDbQuery.IsEmptyIgnoreFilter;
        Entity prefabDbEntity = hasPrefabDb ? _prefabDbQuery.GetSingletonEntity() : Entity.Null;

        bool hasConfig = !_buildingConfigQuery.IsEmptyIgnoreFilter;
        Entity configEntity = hasConfig ? _buildingConfigQuery.GetSingletonEntity() : Entity.Null;

        var job = new ConstructionCompletionApplyJob
        {
            ECB = ecb,
            HasPrefabDb = hasPrefabDb,
            PrefabDbEntity = prefabDbEntity,
            HasConfig = hasConfig,
            ConfigEntity = configEntity,
            FallbackBuildingArchetype = _fallbackBuildingArchetype,
            StoredLookup = _storedLookup,
            StampLookup = _stampLookup,
            PrefabBufferLookup = _prefabBufferLookup,
            ConfigBufferLookup = _configBufferLookup
        };

        var handle = job.Schedule(_siteQuery, state.Dependency);
        ecbSystem.AddJobHandleForProducer(handle);
        state.Dependency = handle;
    }
}

/// <summary>
/// 완공 조건을 만족한 공사 현장을 감지하여 자재를 소비하고 완공 건물을 직접 생성하는 단일 워커 Burst Job.
/// </summary>
[BurstCompile]
public partial struct ConstructionCompletionApplyJob : IJobEntity
{
    public EntityCommandBuffer ECB;
    public bool HasPrefabDb;
    public Entity PrefabDbEntity;
    public bool HasConfig;
    public Entity ConfigEntity;
    public EntityArchetype FallbackBuildingArchetype;

    [ReadOnly]
    public BufferLookup<StoredItemElement> StoredLookup;

    [ReadOnly]
    public ComponentLookup<PlacementStamp> StampLookup;

    [ReadOnly]
    public BufferLookup<BuildingPrefabElement> PrefabBufferLookup;

    [ReadOnly]
    public BufferLookup<BuildingConfigElement> ConfigBufferLookup;

    public void Execute(
        Entity siteEntity,
        in ConstructionSite site,
        in GridPosition pos,
        in Direction dir,
        in BuildingFootprint footprint,
        in DynamicBuffer<ConstructionMaterialRequirementElement> requirements)
    {
        // 1. 공사 현장 상태 플래그 검사: 바닥 아이템 청소 대기 중이거나 취소된 현장이면 완공 보류
        if ((site.Flags & (ConstructionSiteFlags.AwaitingItemClearance | ConstructionSiteFlags.Cancelled)) != 0)
        {
            return;
        }

        // 2. 무효하거나 공사 현장 타입인 경우 완공 불가
        if (site.TargetBuildingType == BuildingTypeEnum.None || site.TargetBuildingType == BuildingTypeEnum.ConstructionSite)
        {
            return;
        }

        // 3. 자재 요구량 충족 검사: 모든 항목의 DeliveredQuantity >= RequiredQuantity (IsSatisfied) 여야 함
        for (int i = 0; i < requirements.Length; i++)
        {
            if (!requirements[i].IsSatisfied)
            {
                // 자재 미충족 현장 -> 완공 보류
                return;
            }
        }

        // 4. 보관된 자재 소비 (StoredItemElement 엔티티 파괴)
        if (StoredLookup.HasBuffer(siteEntity))
        {
            var storedItems = StoredLookup[siteEntity];
            for (int i = 0; i < storedItems.Length; i++)
            {
                Entity itemEntity = storedItems[i].ItemEntity;
                if (itemEntity != Entity.Null)
                {
                    ECB.DestroyEntity(itemEntity);
                }
            }
        }

        // 5. 완공 건물 직접 생성 및 컴포넌트 초기화 (속성 100% 승계, 동일 ECB 트랜잭션)
        PlacementStamp stamp = default;
        if (StampLookup.HasComponent(siteEntity))
        {
            stamp = StampLookup[siteEntity];
        }

        BuildingLifecycleUtility.SpawnBuilding(
            ref ECB,
            site.TargetBuildingType,
            pos.Value,
            dir.dir,
            footprint.Size,
            stamp,
            HasPrefabDb,
            PrefabDbEntity,
            HasConfig,
            ConfigEntity,
            FallbackBuildingArchetype,
            PrefabBufferLookup,
            ConfigBufferLookup
        );

        // 6. 공사 현장 엔티티 파괴 (Atomic Transition: 완공 건물 생성과 동시 실행)
        ECB.DestroyEntity(siteEntity);
    }
}
