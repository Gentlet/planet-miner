using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

/// <summary>
/// 완공 건물의 수명주기(생성 및 철거)를 전담하는 시스템.
/// 
/// [책임 및 파이프라인 순서]
/// - StateApplyGroup(Phase 5)에서 실행:
///   1단계 (Demolish) : Command 검증을 통과한 DemolishBuildingRequest 소비 -> 보관/생산 내용물 월드 방출, 건축 비용 자재 100% 환급 스폰, 벨트 위 아이템 정적 보존, 건물 파괴
///   2단계 (Spawn)    : SpawnBuildingRequest 소비 -> 프리팹 DB 인스턴스화, 컴포넌트 초기화
/// - 동일 프레임 [철거 -> 생성]이 대칭적으로 처리되며, 요청 엔티티는 단일 프레임 내에 소비(Consume-on-Apply).
/// - EndStateApplyEntityCommandBufferSystem을 통해 ECB로 변경을 반영한다. 전체 rollback을 보장하지 않는다.
/// </summary>
[UpdateInGroup(typeof(StateApplyGroup))]
[UpdateAfter(typeof(RoutingApplySystem))]
[UpdateAfter(typeof(BuildingItemStorageApplySystem))]
public partial struct BuildingLifecycleApplySystem : ISystem
{

    private EntityQuery _demolishQuery;
    private EntityQuery _spawnQuery;
    private EntityQuery _beltItemQuery;
    private EntityQuery _buildingPrefabDbQuery;
    private EntityQuery _itemPrefabDbQuery;
    private EntityQuery _buildingConfigQuery;

    private ComponentLookup<BuildingType> _buildingTypeLookup;
    private ComponentLookup<GridPosition> _gridPosLookup;
    private ComponentLookup<DestroyItemRequest> _destroyRequestLookup;
    private BufferLookup<StoredItemElement> _storedBufferLookup;
    private BufferLookup<ProductItemElement> _productBufferLookup;
    private BufferLookup<BuildingConstructionMaterialElement> _materialBufferLookup;
    private BufferLookup<BuildingPrefabElement> _buildingPrefabBufferLookup;
    private BufferLookup<ItemPrefabElement> _itemPrefabBufferLookup;
    private BufferLookup<BuildingConfigElement> _configBufferLookup;

    public void OnCreate(ref SystemState state)
    {

        _demolishQuery = SystemAPI.QueryBuilder()
            .WithAll<DemolishBuildingRequest>()
            .Build();

        _spawnQuery = SystemAPI.QueryBuilder()
            .WithAll<SpawnBuildingRequest>()
            .Build();

        _beltItemQuery = SystemAPI.QueryBuilder()
            .WithAll<BeltMovementState, GridPosition>()
            .Build();

        _buildingPrefabDbQuery = SystemAPI.QueryBuilder()
            .WithAll<BuildingPrefabElement>()
            .Build();

        _itemPrefabDbQuery = SystemAPI.QueryBuilder()
            .WithAll<ItemPrefabDatabase, ItemPrefabElement>()
            .Build();

        _buildingConfigQuery = SystemAPI.QueryBuilder()
            .WithAll<BuildingConfig, BuildingConfigElement>()
            .Build();

        _buildingTypeLookup = state.GetComponentLookup<BuildingType>(true);
        _gridPosLookup = state.GetComponentLookup<GridPosition>(true);
        _destroyRequestLookup = state.GetComponentLookup<DestroyItemRequest>(true);
        _storedBufferLookup = state.GetBufferLookup<StoredItemElement>(true);
        _productBufferLookup = state.GetBufferLookup<ProductItemElement>(true);
        _materialBufferLookup = state.GetBufferLookup<BuildingConstructionMaterialElement>(true);
        _buildingPrefabBufferLookup = state.GetBufferLookup<BuildingPrefabElement>(true);
        _itemPrefabBufferLookup = state.GetBufferLookup<ItemPrefabElement>(true);
        _configBufferLookup = state.GetBufferLookup<BuildingConfigElement>(true);
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    public void OnUpdate(ref SystemState state)
    {
        bool hasDemolishRequests = !_demolishQuery.IsEmptyIgnoreFilter;
        bool hasSpawnRequests = !_spawnQuery.IsEmptyIgnoreFilter;

        if (!hasDemolishRequests && !hasSpawnRequests)
        {
            return;
        }

        var ecbSystem = state.World.GetOrCreateSystemManaged<EndStateApplyEntityCommandBufferSystem>();
        var ecb = ecbSystem.CreateCommandBuffer();

        _buildingTypeLookup.Update(ref state);
        _gridPosLookup.Update(ref state);
        _destroyRequestLookup.Update(ref state);
        _storedBufferLookup.Update(ref state);
        _productBufferLookup.Update(ref state);
        _materialBufferLookup.Update(ref state);
        _buildingPrefabBufferLookup.Update(ref state);
        _itemPrefabBufferLookup.Update(ref state);
        _configBufferLookup.Update(ref state);

        bool hasBuildingPrefabDb = !_buildingPrefabDbQuery.IsEmptyIgnoreFilter;
        Entity buildingPrefabDbEntity = hasBuildingPrefabDb ? _buildingPrefabDbQuery.GetSingletonEntity() : Entity.Null;

        bool hasItemPrefabDb = !_itemPrefabDbQuery.IsEmptyIgnoreFilter;
        Entity itemPrefabDbEntity = hasItemPrefabDb ? _itemPrefabDbQuery.GetSingletonEntity() : Entity.Null;

        bool hasConfig = !_buildingConfigQuery.IsEmptyIgnoreFilter;
        Entity configEntity = hasConfig ? _buildingConfigQuery.GetSingletonEntity() : Entity.Null;

        var currentDep = state.Dependency;

        // 1단계: 철거 요청 일괄 처리
        if (hasDemolishRequests)
        {
            var demolishedBeltPositions = new NativeList<int2>(Allocator.TempJob);

            var demolishJob = new DemolishBuildingApplyJob
            {
                ECB = ecb,
                HasConfig = hasConfig,
                ConfigEntity = configEntity,
                HasItemPrefabDb = hasItemPrefabDb,
                ItemPrefabDbEntity = itemPrefabDbEntity,
                BuildingTypeLookup = _buildingTypeLookup,
                GridPosLookup = _gridPosLookup,
                DestroyRequestLookup = _destroyRequestLookup,
                StoredBufferLookup = _storedBufferLookup,
                ProductBufferLookup = _productBufferLookup,
                MaterialBufferLookup = _materialBufferLookup,
                ItemPrefabBufferLookup = _itemPrefabBufferLookup,
                DemolishedBeltPositions = demolishedBeltPositions
            };
            currentDep = demolishJob.Schedule(_demolishQuery, currentDep);

            // 벨트 철거 시 벨트 위 아이템의 최신 GridPosition을 대조하여 BeltMovementState 안전 비활성화 (공간 인덱스 의존 및 타이밍 오차 배제)
            var cleanupJob = new DemolishBeltItemCleanupJob
            {
                DemolishedBeltPositions = demolishedBeltPositions,
                DestroyRequestLookup = _destroyRequestLookup,
                ECB = ecb
            };
            currentDep = cleanupJob.Schedule(_beltItemQuery, currentDep);
            demolishedBeltPositions.Dispose(currentDep);
        }

        // 2단계: 스폰 요청 일괄 처리
        if (hasSpawnRequests)
        {
            var spawnJob = new SpawnBuildingApplyJob
            {
                ECB = ecb,
                HasPrefabDb = hasBuildingPrefabDb,
                PrefabDbEntity = buildingPrefabDbEntity,
                HasConfig = hasConfig,
                ConfigEntity = configEntity,
                PrefabBufferLookup = _buildingPrefabBufferLookup,
                ConfigBufferLookup = _configBufferLookup
            };
            currentDep = spawnJob.Schedule(_spawnQuery, currentDep);
        }

        ecbSystem.AddJobHandleForProducer(currentDep);
        state.Dependency = currentDep;
    }
}

/// <summary>
/// 철거된 벨트 좌표에 위치한 아이템의 BeltMovementState를 비활성화하여 정적 바닥 아이템으로 보존하는 Burst Job.
/// ItemSpatialIndex 대신 아이템의 최신 GridPosition을 직접 대조하여 타이밍 불일치(Phase 4 이동 vs Phase 6 동기화)를 원천 차단.
/// </summary>
[BurstCompile]
public partial struct DemolishBeltItemCleanupJob : IJobEntity
{
    [ReadOnly] public ComponentLookup<DestroyItemRequest> DestroyRequestLookup;
    [ReadOnly]
    public NativeList<int2> DemolishedBeltPositions;

    public EntityCommandBuffer ECB;

    public void Execute(Entity itemEntity, in GridPosition gridPos)
    {
        if (DestroyRequestLookup.HasComponent(itemEntity) && DestroyRequestLookup.IsComponentEnabled(itemEntity))
        {
            return;
        }

        for (int i = 0; i < DemolishedBeltPositions.Length; i++)
        {
            if (gridPos.Value.Equals(DemolishedBeltPositions[i]))
            {
                ECB.SetComponentEnabled<BeltMovementState>(itemEntity, false);
                break;
            }
        }
    }
}

/// <summary>
/// DemolishBuildingRequest를 소비하여 완공 건물을 철거하고 내용물 및 건설 재료를 반환하는 단일 워커 Burst Job.
/// </summary>
[BurstCompile]
public partial struct DemolishBuildingApplyJob : IJobEntity
{
    [ReadOnly] public ComponentLookup<DestroyItemRequest> DestroyRequestLookup;
    public EntityCommandBuffer ECB;
    public bool HasConfig;
    public Entity ConfigEntity;
    public bool HasItemPrefabDb;
    public Entity ItemPrefabDbEntity;

    [ReadOnly]
    public ComponentLookup<BuildingType> BuildingTypeLookup;

    [ReadOnly]
    public ComponentLookup<GridPosition> GridPosLookup;

    [ReadOnly]
    public BufferLookup<StoredItemElement> StoredBufferLookup;

    [ReadOnly]
    public BufferLookup<ProductItemElement> ProductBufferLookup;

    [ReadOnly]
    public BufferLookup<BuildingConstructionMaterialElement> MaterialBufferLookup;

    [ReadOnly]
    public BufferLookup<ItemPrefabElement> ItemPrefabBufferLookup;

    public NativeList<int2> DemolishedBeltPositions;

    public void Execute(Entity requestEntity, in DemolishBuildingRequest request)
    {
        // 정책 검증과 중복 제거는 Command에서 끝난다. 여기서는 대상 소실만 방어한다.
        if (request.TargetBuilding == Entity.Null)
        {
            ECB.DestroyEntity(requestEntity);
            return;
        }

        if (!BuildingTypeLookup.HasComponent(request.TargetBuilding))
        {
            ECB.DestroyEntity(requestEntity);
            return;
        }

        var buildingType = BuildingTypeLookup[request.TargetBuilding].Type;

        // 3. 건물 좌표 확인
        int2 sitePos = int2.zero;
        if (GridPosLookup.HasComponent(request.TargetBuilding))
        {
            sitePos = GridPosLookup[request.TargetBuilding].Value;
        }
        float3 worldPos = new float3(sitePos.x, sitePos.y, 0f);

        // 4. 건물 내부 보관 자재(StoredItemElement) 전수 반환 (WorldItem 전환, DisableRendering 제거, 바닥 방출)
        if (StoredBufferLookup.HasBuffer(request.TargetBuilding))
        {
            var storedItems = StoredBufferLookup[request.TargetBuilding];
            for (int i = 0; i < storedItems.Length; i++)
            {
                Entity item = storedItems[i].ItemEntity;
                if (item == Entity.Null)
                {
                    continue;
                }

                if (DestroyRequestLookup.HasComponent(item) && DestroyRequestLookup.IsComponentEnabled(item))
                {
                    continue;
                }

                ECB.SetComponent(item, ItemOwnership.WorldItem);
                ECB.RemoveComponent<DisableRendering>(item);
                ECB.SetComponent(item, new GridPosition(sitePos));
                ECB.SetComponent(item, LocalTransform.FromPosition(worldPos));
            }
        }

        // 5. 건물 내부 생산품(ProductItemElement) 전수 반환 (WorldItem 전환, DisableRendering 제거, 바닥 방출)
        if (ProductBufferLookup.HasBuffer(request.TargetBuilding))
        {
            var productItems = ProductBufferLookup[request.TargetBuilding];
            for (int i = 0; i < productItems.Length; i++)
            {
                Entity item = productItems[i].ItemEntity;
                if (item == Entity.Null)
                {
                    continue;
                }

                if (DestroyRequestLookup.HasComponent(item) && DestroyRequestLookup.IsComponentEnabled(item))
                {
                    continue;
                }

                ECB.SetComponent(item, ItemOwnership.WorldItem);
                ECB.RemoveComponent<DisableRendering>(item);
                ECB.SetComponent(item, new GridPosition(sitePos));
                ECB.SetComponent(item, LocalTransform.FromPosition(worldPos));
            }
        }

        // 6. 벨트 철거 시 좌표 수집 (Phase 5 내 DemolishBeltItemCleanupJob을 통해 최신 GridPosition 아이템의 BeltMovementState 비활성화)
        if (buildingType == BuildingTypeEnum.Belt)
        {
            DemolishedBeltPositions.Add(sitePos);
        }

        // 7. 건설 재료(건축 비용) 100% 신규 스폰 환급
        if (HasConfig && ConfigEntity != Entity.Null && MaterialBufferLookup.HasBuffer(ConfigEntity))
        {
            var materialDb = MaterialBufferLookup[ConfigEntity];
            for (int i = 0; i < materialDb.Length; i++)
            {
                if (materialDb[i].BuildingType == buildingType)
                {
                    ItemTypeEnum itemType = materialDb[i].ItemType;
                    int quantity = materialDb[i].Quantity;

                    Entity prefabEntity = Entity.Null;
                    if (HasItemPrefabDb && ItemPrefabDbEntity != Entity.Null && ItemPrefabBufferLookup.HasBuffer(ItemPrefabDbEntity))
                    {
                        PrefabLookupUtility.TryGetItemPrefab(ItemPrefabBufferLookup[ItemPrefabDbEntity], itemType, out prefabEntity);
                    }
                    if (prefabEntity == Entity.Null)
                    {
                        // Strict Fail Policy: 프리팹 DB 활성화 환경에서 프리팹 누락 시 스폰 거부 및 에러 로깅
                        FixedString128Bytes msg = default;
                        msg.Append((FixedString128Bytes)"[BuildingLifecycleApplySystem] Missing prefab for refund item type '");
                        msg.Append(itemType.ToFixedString());
                        msg.Append((FixedString128Bytes)"'. Refund spawning skipped.");
                        SimulationFailureUtility.Record(ref ECB, msg);
                        continue;
                    }

                    for (int q = 0; q < quantity; q++)
                    {
                        ItemLifecycleUtility.SpawnPrefabItem(ref ECB, prefabEntity, itemType, sitePos, worldPos, ItemOwnership.WorldItem);
                    }

                }
            }
        }

        // 8. 건물 엔티티 파괴 (동일 틱 Phase 6 공간 동기화 시 건물 공간 점유 자동 해제)
        ECB.DestroyEntity(request.TargetBuilding);

        // 9. 요청 엔티티 소비 (Consume-on-Apply)
        ECB.DestroyEntity(requestEntity);
    }



}

/// <summary>
/// SpawnBuildingRequest를 소비하여 완공 건물 엔티티를 생성하고 컴포넌트를 초기화하는 단일 워커 Burst Job.
/// </summary>
[BurstCompile]
public partial struct SpawnBuildingApplyJob : IJobEntity
{
    public EntityCommandBuffer ECB;
    public bool HasPrefabDb;
    public Entity PrefabDbEntity;
    public bool HasConfig;
    public Entity ConfigEntity;

    [ReadOnly]
    public BufferLookup<BuildingPrefabElement> PrefabBufferLookup;

    [ReadOnly]
    public BufferLookup<BuildingConfigElement> ConfigBufferLookup;

    public void Execute(Entity requestEntity, in SpawnBuildingRequest request)
    {
        BuildingLifecycleUtility.SpawnBuilding(
            ref ECB,
            request.TargetType,
            request.Position,
            request.Direction,
            request.FootprintSize,
            request.Stamp,
            HasPrefabDb,
            PrefabDbEntity,
            HasConfig,
            ConfigEntity,
            in PrefabBufferLookup,
            in ConfigBufferLookup
        );

        // 요청 엔티티 소비 (Consume-on-Apply)
        ECB.DestroyEntity(requestEntity);
    }
}
