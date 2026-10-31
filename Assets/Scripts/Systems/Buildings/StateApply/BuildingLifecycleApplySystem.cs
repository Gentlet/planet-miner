using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

/// <summary>
/// 역할/목적: 직접 생성 요청과 Command 승인/드론 확인이 공개된 일반 건물 철거를 최종 반영한다.
/// 처리 단계: Building StateApply. 입력은 SpawnBuildingRequest, Pending 상태/연결 root의 실행 요청과 설정/프리팹 DB다.
/// 출력·소유권: 보관/생산 실물의 월드 반환, 건축 비용 환급 Spawn, 철거 벨트 위 실물 정지, 건물 생성/삭제를 담당한다.
/// 철거 정책은 Command 승인/드론 확인을 유지하며 대상 소실/환급 실패를 root 결과로 반환한다. 현장 취소/완공은 공사 소유자가 처리한다.
/// 정리·가시화: 요청 소비와 건물/승인 상태 삭제 및 구조 변경은 EndBuilding에 확정한다. Job은 철거→벨트 정리→직접 생성 순서로 기록하며 전체 rollback은 보장하지 않는다.
/// </summary>
[UpdateInGroup(typeof(BuildingStateApplyGroup))]
[UpdateAfter(typeof(RoutingApplySystem))]
[UpdateAfter(typeof(BuildingItemStorageApplySystem))]
public partial struct BuildingLifecycleApplySystem : ISystem
{

    private EntityQuery _pendingDemolitionQuery;
    private EntityQuery _demolitionRequests;
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
        _pendingDemolitionQuery = SystemAPI.QueryBuilder()
            .WithAll<PendingBuildingDemolition>()
            .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
            .Build();

        _demolitionRequests = state.GetEntityQuery(typeof(BuildingDemolitionExecutionRequest), typeof(DroneLogisticsTask));
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
        bool hasPendingDemolition = !_demolitionRequests.IsEmptyIgnoreFilter;
        bool hasSpawnRequests = !_spawnQuery.IsEmptyIgnoreFilter;

        if (!hasPendingDemolition && !hasSpawnRequests)
        {
            return;
        }

        if (hasSpawnRequests)
        {
            state.CompleteDependency();
            BuildingPlacementRequestUtility.PrepareSpawnStamps(state.EntityManager, _spawnQuery);
        }

        var ecbSystem = state.World.GetOrCreateSystemManaged<EndBuildingEntityCommandBufferSystem>();
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

        // 외부 요청은 이미 EndCommand에 소비됐다. 여기서는 승인 상태만 읽어 실제 반환/삭제를 기록한다.
        if (hasPendingDemolition)
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
                DemolishedBeltPositions = demolishedBeltPositions,
                ExecutionRequests = state.GetComponentLookup<BuildingDemolitionExecutionRequest>(true),
                RecoveryMarks = state.GetComponentLookup<DroneRecoveryPending>(true)
            };
            currentDep = demolishJob.Schedule(_pendingDemolitionQuery, currentDep);

            // 벨트 철거 시 벨트 위 아이템의 최신 GridPosition을 대조하여 BeltMovementState 안전 비활성화 (공간 인덱스 의존 및 타이밍 오차 배제)
            var cleanupJob = new DemolishBeltItemCleanupJob
            {
                DemolishedBeltPositions = demolishedBeltPositions,
                DestroyRequestLookup = _destroyRequestLookup,
                RecoveryMarks = state.GetComponentLookup<DroneRecoveryPending>(true),
                ECB = ecb
            };
            currentDep = cleanupJob.Schedule(_beltItemQuery, currentDep);
            demolishedBeltPositions.Dispose(currentDep);
            currentDep = new MissingDemolitionTargetResultJob
            { Buildings = _buildingTypeLookup, ECB = ecb }.Schedule(_demolitionRequests, currentDep);
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
    [ReadOnly] public ComponentLookup<DroneRecoveryPending> RecoveryMarks;
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
                if (!RecoveryMarks.HasComponent(itemEntity))
                    ECB.AddComponent(itemEntity, new DroneRecoveryPending { Reason = DroneRecoveryReasonEnum.BuildingDemolition });
                break;
            }
        }
    }
}

/// <summary>
/// 드론 확인 요청이 공개된 PendingBuildingDemolition만 소비해 반환/환급/삭제와 root 결과를 기록하는 단일 Burst Job.
/// </summary>
[BurstCompile]
public partial struct DemolishBuildingApplyJob : IJobEntity
{
    [ReadOnly] public ComponentLookup<BuildingDemolitionExecutionRequest> ExecutionRequests;
    [ReadOnly] public ComponentLookup<DroneRecoveryPending> RecoveryMarks;
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

    public void Execute(Entity buildingEntity, in PendingBuildingDemolition pending)
    {
        if (!ExecutionRequests.HasComponent(pending.Task)) return;
        bool refundFailed = false;
        // 정책 검증과 중복 제거는 Command에서 끝난다. 여기서는 대상 소실만 방어한다.
        if (!BuildingTypeLookup.HasComponent(buildingEntity))
        {
            ECB.RemoveComponent<PendingBuildingDemolition>(buildingEntity);
            return;
        }

        var buildingDefinition = BuildingTypeLookup[buildingEntity];
        var buildingType = buildingDefinition.Type;

        // 3. 건물 좌표 확인
        int2 sitePos = int2.zero;
        if (GridPosLookup.HasComponent(buildingEntity))
        {
            sitePos = GridPosLookup[buildingEntity].Value;
        }
        float3 worldPos = new float3(sitePos.x, sitePos.y, 0f);

        // 기존 보관품은 새 환급품으로 대체하지 않는다. 같은 실물을 건물 위치에 반환하고
        // 활성 Destroy 실물은 반환에서 제외해 소비 예정 아이템을 되살리지 않는다.
        if (StoredBufferLookup.HasBuffer(buildingEntity))
        {
            var storedItems = StoredBufferLookup[buildingEntity];
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
                if (!RecoveryMarks.HasComponent(item))
                    ECB.AddComponent(item, new DroneRecoveryPending { Reason = DroneRecoveryReasonEnum.BuildingDemolition });
                ECB.RemoveComponent<DisableRendering>(item);
                ECB.SetComponent(item, new GridPosition(sitePos));
                ECB.SetComponent(item, LocalTransform.FromPosition(worldPos));
            }
        }

        // 5. 건물 내부 생산품(ProductItemElement) 전수 반환 (WorldItem 전환, DisableRendering 제거, 바닥 방출)
        if (ProductBufferLookup.HasBuffer(buildingEntity))
        {
            var productItems = ProductBufferLookup[buildingEntity];
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
                if (!RecoveryMarks.HasComponent(item))
                    ECB.AddComponent(item, new DroneRecoveryPending { Reason = DroneRecoveryReasonEnum.BuildingDemolition });
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

        // 기존 내용물 반환과 건축 비용 환급을 구분한다. 비용만 설정 수량대로 새 실물을 만들며
        // 이전 틱에 생산 과정에서 선소비한 재료까지 추가 보상하지 않는다.
        if (HasConfig && ConfigEntity != Entity.Null && MaterialBufferLookup.HasBuffer(ConfigEntity))
        {
            var materialDb = MaterialBufferLookup[ConfigEntity];
            for (int i = 0; i < materialDb.Length; i++)
            {
                if (materialDb[i].BuildingType == buildingType && materialDb[i].Tier == buildingDefinition.Tier)
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
                        refundFailed = true;
                        continue;
                    }

                    for (int q = 0; q < quantity; q++)
                    {
                        Entity refund = ItemLifecycleUtility.SpawnPrefabItem(ref ECB, prefabEntity, itemType, sitePos, worldPos, ItemOwnership.WorldItem);
                        ECB.AddComponent(refund, new DroneRecoveryPending { Reason = DroneRecoveryReasonEnum.BuildingDemolition });
                    }

                }
            }
        }

        // 8. 건물 엔티티 파괴 (동일 틱 Phase 6 공간 동기화 시 건물 공간 점유 자동 해제)
        ECB.AddComponent(pending.Task, new BuildingDemolitionResult
        { Status = refundFailed ? BuildingDemolitionResultStatus.Failed : BuildingDemolitionResultStatus.Completed });
        ECB.RemoveComponent<BuildingDemolitionExecutionRequest>(pending.Task);
        ECB.DestroyEntity(buildingEntity);


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
            request.TargetTier,
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

/// <summary>확인 뒤 대상이 소실된 root에 실패를 반환해 결과 대기가 남지 않게 한다.</summary>
[BurstCompile]
public partial struct MissingDemolitionTargetResultJob : IJobEntity
{
    [ReadOnly] public ComponentLookup<BuildingType> Buildings;
    public EntityCommandBuffer ECB;
    public void Execute(Entity taskEntity, in DroneLogisticsTask task, in BuildingDemolitionExecutionRequest request)
    {
        if (Buildings.HasComponent(task.Target)) return;
        ECB.AddComponent(taskEntity, new BuildingDemolitionResult { Status = BuildingDemolitionResultStatus.Failed });
        ECB.RemoveComponent<BuildingDemolitionExecutionRequest>(taskEntity);
    }
}
