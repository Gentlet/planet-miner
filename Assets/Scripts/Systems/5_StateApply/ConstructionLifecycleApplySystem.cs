using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

/// <summary>
/// 공사 현장(ConstructionSite)의 수명주기 전이(취소, 자재 공급, 완공)를 총괄하는 단일 통합 시스템.
/// 
/// [책임 및 파이프라인 순서]
/// - StateApplyGroup(Phase 5)에서 단일 시스템으로 실행되며, 코드 레벨에서 결정론적 순서를 보장:
///   1단계 (Cancel)     : CancelConstructionRequest 소비 -> 취소 플래그 마킹, 도착 자재 월드 방출, 현장 파괴 (Cancel Wins)
///   2단계 (Material)   : SupplyConstructionMaterialRequest 소비 -> 취소된 현장 거부, 자재 요구량 충족, 자재 소유권 이전
///   3단계 (Completion) : 완공 조건 검사 -> 취소된 현장 보류, 보관 자재 소비, 완공 건물 인스턴스화, 현장 파괴
/// - 입출고 적용 후 소유 버퍼를 읽어 철거 반환 대상의 공급을 거부한다. 철거 적용 결과를 기다리지 않는다.
/// - EndStateApplyEntityCommandBufferSystem을 통해 ECB로 변경을 반영한다. 전체 rollback을 보장하지 않는다.
/// </summary>
[UpdateInGroup(typeof(StateApplyGroup))]
[UpdateAfter(typeof(BuildingItemStorageApplySystem))]
[UpdateBefore(typeof(BuildingLifecycleApplySystem))]
public partial struct ConstructionLifecycleApplySystem : ISystem
{
    private EntityQuery _cancelRequestQuery;
    private EntityQuery _materialRequestQuery;
    private EntityQuery _siteQuery;
    private EntityQuery _prefabDbQuery;
    private EntityQuery _buildingConfigQuery;
    private EntityQuery _demolishQuery;

    private ComponentLookup<ConstructionSite> _siteLookup;
    private BufferLookup<ConstructionMaterialRequirementElement> _reqBufferLookup;
    private BufferLookup<StoredItemElement> _storedBufferLookup;
    private BufferLookup<ProductItemElement> _productBufferLookup;
    private ComponentLookup<DestroyItemRequest> _destroyRequestLookup;
    private ComponentLookup<GridPosition> _gridPosLookup;
    private ComponentLookup<ItemIdentity> _itemIdentityLookup;
    private ComponentLookup<PlacementStamp> _stampLookup;
    private BufferLookup<BuildingPrefabElement> _prefabBufferLookup;
    private BufferLookup<BuildingConfigElement> _configBufferLookup;

    public void OnCreate(ref SystemState state)
    {
        _cancelRequestQuery = SystemAPI.QueryBuilder()
            .WithAll<CancelConstructionRequest>()
            .Build();

        _materialRequestQuery = SystemAPI.QueryBuilder()
            .WithAll<SupplyConstructionMaterialRequest>()
            .Build();

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

        _demolishQuery = SystemAPI.QueryBuilder()
            .WithAll<DemolishBuildingRequest>()
            .Build();

        _siteLookup = state.GetComponentLookup<ConstructionSite>(false);
        _reqBufferLookup = state.GetBufferLookup<ConstructionMaterialRequirementElement>(false);
        _storedBufferLookup = state.GetBufferLookup<StoredItemElement>(false);
        _productBufferLookup = state.GetBufferLookup<ProductItemElement>(true);
        _destroyRequestLookup = state.GetComponentLookup<DestroyItemRequest>(true);
        _gridPosLookup = state.GetComponentLookup<GridPosition>(true);
        _itemIdentityLookup = state.GetComponentLookup<ItemIdentity>(true);
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
        bool hasCancelRequests = !_cancelRequestQuery.IsEmptyIgnoreFilter;
        bool hasMaterialRequests = !_materialRequestQuery.IsEmptyIgnoreFilter;
        bool hasSites = !_siteQuery.IsEmptyIgnoreFilter;

        if (!hasCancelRequests && !hasMaterialRequests && !hasSites)
        {
            return;
        }

        var ecbSystem = state.World.GetOrCreateSystemManaged<EndStateApplyEntityCommandBufferSystem>();
        var ecb = ecbSystem.CreateCommandBuffer();

        _siteLookup.Update(ref state);
        _reqBufferLookup.Update(ref state);
        _storedBufferLookup.Update(ref state);
        _productBufferLookup.Update(ref state);
        _destroyRequestLookup.Update(ref state);
        _gridPosLookup.Update(ref state);
        _itemIdentityLookup.Update(ref state);
        _stampLookup.Update(ref state);
        _prefabBufferLookup.Update(ref state);
        _configBufferLookup.Update(ref state);

        var currentDep = state.Dependency;

        // 1단계: 취소 요청 일괄 처리 (Cancel Wins 보장)
        if (hasCancelRequests)
        {
            var cancelJob = new CancelConstructionApplyJob
            {
                ECB = ecb,
                SiteLookup = _siteLookup,
                GridPosLookup = _gridPosLookup,
                StoredBufferLookup = _storedBufferLookup,
                DestroyRequestLookup = _destroyRequestLookup
            };
            currentDep = cancelJob.Schedule(_cancelRequestQuery, currentDep);
        }

        // 2단계: 자재 공급 요청 일괄 처리 (취소된 현장은 엄격 거부)
        if (hasMaterialRequests)
        {
            var demolitionRequests = _demolishQuery.ToComponentDataListAsync<DemolishBuildingRequest>(
                Allocator.TempJob, currentDep, out var demolitionRequestsHandle);
            var materialJob = new ConstructionMaterialApplyJob
            {
                ECB = ecb,
                SiteLookup = _siteLookup,
                ReqBufferLookup = _reqBufferLookup,
                StoredBufferLookup = _storedBufferLookup,
                ProductBufferLookup = _productBufferLookup,
                DestroyRequestLookup = _destroyRequestLookup,
                DemolitionRequests = demolitionRequests,
                ItemIdentityLookup = _itemIdentityLookup
            };
            var materialHandle = materialJob.Schedule(_materialRequestQuery, demolitionRequestsHandle);
            currentDep = demolitionRequests.Dispose(materialHandle);
        }

        // 3단계: 완공 판정 및 완공 건물 전환 (취소 및 아이템 정리 대기 현장 보류)
        if (hasSites)
        {
            bool hasPrefabDb = !_prefabDbQuery.IsEmptyIgnoreFilter;
            Entity prefabDbEntity = hasPrefabDb ? _prefabDbQuery.GetSingletonEntity() : Entity.Null;

            bool hasConfig = !_buildingConfigQuery.IsEmptyIgnoreFilter;
            Entity configEntity = hasConfig ? _buildingConfigQuery.GetSingletonEntity() : Entity.Null;

            var completionJob = new ConstructionCompletionApplyJob
            {
                ECB = ecb,
                HasPrefabDb = hasPrefabDb,
                PrefabDbEntity = prefabDbEntity,
                HasConfig = hasConfig,
                ConfigEntity = configEntity,
                StoredLookup = _storedBufferLookup,
                StampLookup = _stampLookup,
                PrefabBufferLookup = _prefabBufferLookup,
                ConfigBufferLookup = _configBufferLookup
            };
            currentDep = completionJob.Schedule(_siteQuery, currentDep);
        }

        ecbSystem.AddJobHandleForProducer(currentDep);
        state.Dependency = currentDep;
    }
}

/// <summary>
/// CancelConstructionRequest를 소비하여 현장을 취소하고 자재를 반환하는 단일 워커 Burst Job.
/// </summary>
[BurstCompile]
public partial struct CancelConstructionApplyJob : IJobEntity
{
    public EntityCommandBuffer ECB;
    [ReadOnly] public ComponentLookup<DestroyItemRequest> DestroyRequestLookup;

    public ComponentLookup<ConstructionSite> SiteLookup;

    [ReadOnly]
    public ComponentLookup<GridPosition> GridPosLookup;

    [ReadOnly]
    public BufferLookup<StoredItemElement> StoredBufferLookup;

    public void Execute(Entity requestEntity, in CancelConstructionRequest request)
    {
        // 1. 대상 공사 현장 엔티티 검증 (무효, 기파괴, 완공 건물 등)
        if (request.TargetSite == Entity.Null || !SiteLookup.HasComponent(request.TargetSite))
        {
            // 대상이 없거나 이미 완공된 건물이거나 파괴된 경우 요청만 안전하게 소비 (Idempotent Drop)
            ECB.DestroyEntity(requestEntity);
            return;
        }

        var site = SiteLookup[request.TargetSite];
        if ((site.Flags & ConstructionSiteFlags.Cancelled) != 0)
        {
            // 이미 동일 프레임 앞선 요청에 의해 취소 처리됨 (중복 취소 방지)
            ECB.DestroyEntity(requestEntity);
            return;
        }

        // 2. 취소 플래그 즉시 마킹 (동일 프레임 뒤따르는 자재 공급 및 완공 전환 차단)
        site.Flags |= ConstructionSiteFlags.Cancelled;
        SiteLookup[request.TargetSite] = site;

        // 3. 현장 위치 확인
        int2 sitePos = int2.zero;
        if (GridPosLookup.HasComponent(request.TargetSite))
        {
            sitePos = GridPosLookup[request.TargetSite].Value;
        }
        float3 worldPos = new float3(sitePos.x, sitePos.y, 0f);

        // 4. 도착 자재 전수 반환 (WorldItem 전환, DisableRendering 제거, 위치 주입)
        if (StoredBufferLookup.HasBuffer(request.TargetSite))
        {
            var storedItems = StoredBufferLookup[request.TargetSite];
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

        // 5. 공사 현장 엔티티 파괴 (동일 틱 Phase 6에서 공간 점유 자동 해제)
        ECB.DestroyEntity(request.TargetSite);

        // 6. 요청 엔티티 소비 (Consume-on-Apply)
        ECB.DestroyEntity(requestEntity);
    }
}

/// <summary>
/// SupplyConstructionMaterialRequest를 소비하여 자재를 현장에 수령하는 단일 워커 Burst Job.
/// </summary>
[BurstCompile]
public partial struct ConstructionMaterialApplyJob : IJobEntity
{
    public EntityCommandBuffer ECB;

    public ComponentLookup<ConstructionSite> SiteLookup;
    public BufferLookup<ConstructionMaterialRequirementElement> ReqBufferLookup;
    public BufferLookup<StoredItemElement> StoredBufferLookup;

    [ReadOnly]
    public ComponentLookup<ItemIdentity> ItemIdentityLookup;

    [ReadOnly] public ComponentLookup<DestroyItemRequest> DestroyRequestLookup;
    [ReadOnly] public BufferLookup<ProductItemElement> ProductBufferLookup;
    [ReadOnly] public NativeList<DemolishBuildingRequest> DemolitionRequests;

    public void Execute(Entity requestEntity, in SupplyConstructionMaterialRequest request)
    {
        // 1. 대상 공사 현장 엔티티 검증
        if (request.TargetSite == Entity.Null ||
            !SiteLookup.HasComponent(request.TargetSite) ||
            !ReqBufferLookup.HasBuffer(request.TargetSite))
        {
            // 현장이 존재하지 않거나 이미 파괴됨 -> Strict Rejection
            ECB.DestroyEntity(requestEntity);
            return;
        }

        var site = SiteLookup[request.TargetSite];
        if ((site.Flags & ConstructionSiteFlags.Cancelled) != 0)
        {
            // 현장이 취소되어 파괴 진행 중임 -> Strict Rejection (자재 소유권 유지, 요청만 소비)
            ECB.DestroyEntity(requestEntity);
            return;
        }

        // 2. 전달할 아이템 엔티티 유효성 검증
        if (request.ItemEntity == Entity.Null ||
            !ItemIdentityLookup.HasComponent(request.ItemEntity))
        {
            // 무효 아이템 -> Strict Rejection
            ECB.DestroyEntity(requestEntity);
            return;
        }

        var identity = ItemIdentityLookup[request.ItemEntity];
        if (identity.Type != request.ItemType || request.ItemType == ItemTypeEnum.None)
        {
            // 아이템 종류 불일치 -> Strict Rejection
            ECB.DestroyEntity(requestEntity);
            return;
        }

        // 수령 승인 전에 소멸/철거 반환과의 충돌을 거부한다. 예약량은 기존 거부 정책대로 보존한다.
        if (DestroyRequestLookup.HasComponent(request.ItemEntity) &&
            DestroyRequestLookup.IsComponentEnabled(request.ItemEntity))
        {
            ECB.DestroyEntity(requestEntity);
            return;
        }

        if (DemolishBuildingRequestLookup.ContainsBufferedItem(
                request.ItemEntity, DemolitionRequests, StoredBufferLookup, ProductBufferLookup))
        {
            ECB.DestroyEntity(requestEntity);
            return;
        }

        // 3. 현장 자재 요구 버퍼 매칭 및 잔여량 검증
        var reqBuffer = ReqBufferLookup[request.TargetSite];
        int matchedIndex = -1;
        for (int i = 0; i < reqBuffer.Length; i++)
        {
            if (reqBuffer[i].ItemType == request.ItemType)
            {
                matchedIndex = i;
                break;
            }
        }

        if (matchedIndex < 0)
        {
            // 현장 요구 목록에 없는 잘못된 품목 -> Strict Rejection
            ECB.DestroyEntity(requestEntity);
            return;
        }

        var req = reqBuffer[matchedIndex];
        if (req.RemainingRequired <= 0)
        {
            // 이미 요구 수량이 100% 충족된 품목 초과 전달 -> Strict Rejection
            ECB.DestroyEntity(requestEntity);
            return;
        }

        // 4. 자재 수령 적용 (DeliveredQuantity 증가, ReservedQuantity 차감)
        req.DeliveredQuantity++;
        if (req.ReservedQuantity > 0)
        {
            req.ReservedQuantity--;
        }
        reqBuffer[matchedIndex] = req;

        // 5. 공사 진행률 갱신 (모든 요구 품목의 총 인도 수량 / 총 요구 수량)
        int totalRequired = 0;
        int totalDelivered = 0;
        for (int i = 0; i < reqBuffer.Length; i++)
        {
            totalRequired += reqBuffer[i].RequiredQuantity;
            totalDelivered += reqBuffer[i].DeliveredQuantity;
        }

        if (totalRequired > 0)
        {
            site.Progress = math.clamp((float)totalDelivered / totalRequired, 0f, 1f);
            SiteLookup[request.TargetSite] = site;
        }

        // 6. 아이템 소유권 이전 및 렌더링 비활성화, 현장 보관 버퍼 등록
        ECB.SetComponent(request.ItemEntity, ItemOwnership.Stored(request.TargetSite));
        ECB.AddComponent<DisableRendering>(request.ItemEntity);

        if (StoredBufferLookup.HasBuffer(request.TargetSite))
        {
            var storedBuffer = StoredBufferLookup[request.TargetSite];
            storedBuffer.Add(new StoredItemElement(request.ItemEntity, request.ItemType, 0));
        }

        // 7. 요청 엔티티 소비 (Consume-on-Apply)
        ECB.DestroyEntity(requestEntity);
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

        // 5. 완공 건물 엔티티 인스턴스화 및 컴포넌트 초기화 (BuildingLifecycleUtility 활용)
        PlacementStamp stamp = default;
        if (StampLookup.HasComponent(siteEntity))
        {
            stamp = StampLookup[siteEntity];
        }

        Entity building = BuildingLifecycleUtility.SpawnBuilding(
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
            in PrefabBufferLookup,
            in ConfigBufferLookup
        );

        if (building == Entity.Null)
        {
            // 정상 수령 결과는 유지하고 자재/현장을 보존한다.
            return;
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

        // 6. 공사 현장 엔티티 파괴 (동일 ECB Playback 틱에 발생하므로 점유 공백 0 달성)
        ECB.DestroyEntity(siteEntity);
    }
}
