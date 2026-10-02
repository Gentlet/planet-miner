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
///   2단계 (Material)   : 운송 취소/닫힌 현장 정산 -> 실물 경합 검사/등록 -> 이전 보관자 인수, 건별 정산
///   3단계 (Completion) : 완공 조건 검사 -> 건물 생성 성공 후 자재/현장 삭제 -> 닫힌 현장의 남은 운송 종료
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
    private EntityQuery _deliveryQuery;
    private EntityQuery _cancelDeliveryQuery;
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
    private ComponentLookup<ItemOwnership> _itemOwnershipLookup;
    private ComponentLookup<BeltMovementState> _beltMovementLookup;
    private ComponentLookup<TransferOwnershipRequest> _transferLookup;
    private ComponentLookup<BuildingType> _buildingTypeLookup;
    private ComponentLookup<Storage> _storageLookup;
    private ComponentLookup<ConstructionMaterialDelivery> _deliveryLookup;
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

        _deliveryQuery = SystemAPI.QueryBuilder()
            .WithAll<ConstructionMaterialDelivery>()
            .Build();
        _cancelDeliveryQuery = SystemAPI.QueryBuilder()
            .WithAll<CancelConstructionMaterialDeliveryRequest>()
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
        _itemOwnershipLookup = state.GetComponentLookup<ItemOwnership>(true);
        _beltMovementLookup = state.GetComponentLookup<BeltMovementState>(false);
        _transferLookup = state.GetComponentLookup<TransferOwnershipRequest>(true);
        _buildingTypeLookup = state.GetComponentLookup<BuildingType>(true);
        _storageLookup = state.GetComponentLookup<Storage>(true);
        _deliveryLookup = state.GetComponentLookup<ConstructionMaterialDelivery>(false);
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
        bool hasDeliveries = !_deliveryQuery.IsEmptyIgnoreFilter;
        bool hasCancelDeliveries = !_cancelDeliveryQuery.IsEmptyIgnoreFilter;

        if (!hasCancelRequests && !hasMaterialRequests && !hasSites && !hasDeliveries && !hasCancelDeliveries)
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
        _itemOwnershipLookup.Update(ref state);
        _beltMovementLookup.Update(ref state);
        _transferLookup.Update(ref state);
        _buildingTypeLookup.Update(ref state);
        _storageLookup.Update(ref state);
        _deliveryLookup.Update(ref state);
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

        var completedSites = new NativeParallelHashSet<Entity>(math.max(1, _siteQuery.CalculateEntityCount()), Allocator.TempJob);

        // 종료된 운송의 예약/실물 선점을 새 등록보다 먼저 해제한다.
        if (hasCancelDeliveries)
        {
            currentDep = new CancelConstructionMaterialDeliveryJob
            {
                ECB = ecb,
                DeliveryLookup = _deliveryLookup,
                RequirementsLookup = _reqBufferLookup
            }.Schedule(_cancelDeliveryQuery, currentDep);
        }
        if (hasDeliveries)
        {
            currentDep = new CloseConstructionMaterialDeliveryJob
            {
                ECB = ecb,
                SiteLookup = _siteLookup,
                CompletedSites = completedSites,
                RequirementsLookup = _reqBufferLookup
            }.Schedule(_deliveryQuery, currentDep);
        }

        // 운송 등록과 수령은 동일한 승인 철거 스냅샷을 사용한다.
        if (hasDeliveries || hasMaterialRequests)
        {
            var demolitionRequests = _demolishQuery.ToComponentDataListAsync<DemolishBuildingRequest>(
                Allocator.TempJob, currentDep, out var demolitionRequestsHandle);
            currentDep = demolitionRequestsHandle;
            if (hasDeliveries)
            {
                var claimedItems = new NativeParallelHashSet<Entity>(
                    math.max(1, _deliveryQuery.CalculateEntityCount()), Allocator.TempJob);
                currentDep = new CollectConstructionMaterialClaimsJob
                {
                    ClaimedItems = claimedItems
                }.Schedule(_deliveryQuery, currentDep);
                currentDep = new RegisterConstructionMaterialDeliveryJob
                {
                    ECB = ecb,
                    ClaimedItems = claimedItems,
                    RequirementsLookup = _reqBufferLookup,
                    SiteLookup = _siteLookup,
                    BuildingTypeLookup = _buildingTypeLookup,
                    StorageLookup = _storageLookup,
                    IdentityLookup = _itemIdentityLookup,
                    OwnershipLookup = _itemOwnershipLookup,
                    DestroyLookup = _destroyRequestLookup,
                    TransferLookup = _transferLookup,
                    StoredLookup = _storedBufferLookup,
                    ProductLookup = _productBufferLookup,
                    DemolitionRequests = demolitionRequests
                }.Schedule(_deliveryQuery, currentDep);
                currentDep = claimedItems.Dispose(currentDep);
            }
            if (hasMaterialRequests)
            {
                var materialJob = new ConstructionMaterialApplyJob
                {
                    ECB = ecb,
                    SiteLookup = _siteLookup,
                    ReqBufferLookup = _reqBufferLookup,
                    StoredBufferLookup = _storedBufferLookup,
                    ProductBufferLookup = _productBufferLookup,
                    DestroyRequestLookup = _destroyRequestLookup,
                    TransferLookup = _transferLookup,
                    DemolitionRequests = demolitionRequests,
                    ItemIdentityLookup = _itemIdentityLookup,
                    OwnershipLookup = _itemOwnershipLookup,
                    BuildingTypeLookup = _buildingTypeLookup,
                    BeltMovementLookup = _beltMovementLookup,
                    DeliveryLookup = _deliveryLookup
                };
                currentDep = materialJob.Schedule(_materialRequestQuery, currentDep);
            }
            currentDep = demolitionRequests.Dispose(currentDep);
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
                ConfigBufferLookup = _configBufferLookup,
                CompletedSites = completedSites
            };
            currentDep = completionJob.Schedule(_siteQuery, currentDep);
        }

        if (hasDeliveries)
        {
            currentDep = new CloseConstructionMaterialDeliveryJob
            {
                ECB = ecb,
                SiteLookup = _siteLookup,
                CompletedSites = completedSites,
                RequirementsLookup = _reqBufferLookup
            }.Schedule(_deliveryQuery, currentDep);
        }
        currentDep = completedSites.Dispose(currentDep);

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
/// 등록된 운송의 실물 인수와 예약 정산을 단일 워커에서 처리한다.
/// 검증을 모두 끝낸 후 이전 버퍼 제거/현장 등록/수량을 직접 적용하고,
/// Owner/태그/공개 결과/요청 삭제는 같은 EndStateApply ECB에 기록한다.
/// </summary>
[BurstCompile]
public partial struct ConstructionMaterialApplyJob : IJobEntity
{
    public EntityCommandBuffer ECB;
    public ComponentLookup<ConstructionSite> SiteLookup;
    public ComponentLookup<ConstructionMaterialDelivery> DeliveryLookup;
    public ComponentLookup<BeltMovementState> BeltMovementLookup;
    public BufferLookup<ConstructionMaterialRequirementElement> ReqBufferLookup;
    public BufferLookup<StoredItemElement> StoredBufferLookup;
    [ReadOnly] public ComponentLookup<ItemIdentity> ItemIdentityLookup;
    [ReadOnly] public ComponentLookup<ItemOwnership> OwnershipLookup;
    [ReadOnly] public ComponentLookup<BuildingType> BuildingTypeLookup;
    [ReadOnly] public ComponentLookup<DestroyItemRequest> DestroyRequestLookup;
    [ReadOnly] public ComponentLookup<TransferOwnershipRequest> TransferLookup;
    [ReadOnly] public BufferLookup<ProductItemElement> ProductBufferLookup;
    [ReadOnly] public NativeList<DemolishBuildingRequest> DemolitionRequests;

    public void Execute(Entity requestEntity, in SupplyConstructionMaterialRequest request)
    {
        ECB.DestroyEntity(requestEntity);
        if (request.Delivery == Entity.Null)
        {
            return;
        }
        if (!DeliveryLookup.HasComponent(request.Delivery))
        {
            return;
        }
        var delivery = DeliveryLookup[request.Delivery];
        if (delivery.State != ConstructionMaterialDeliveryStateEnum.Ready)
        {
            // 같은 운송의 재요청은 예약/실물/기존 결과를 다시 변경하지 않는다.
            return;
        }

        // F-004: 해당 실물 충돌은 일반 실패 정산과 구분하여 활성 예약을 그대로 둔다.
        if (DestroyRequestLookup.HasComponent(delivery.ItemEntity) &&
            DestroyRequestLookup.IsComponentEnabled(delivery.ItemEntity))
        {
            Reject(request.Delivery, ref delivery, ConstructionMaterialDeliveryOutcomeEnum.DestroyConflict, true);
            return;
        }
        if (DemolishBuildingRequestLookup.ContainsBufferedItem(
                delivery.ItemEntity, DemolitionRequests, StoredBufferLookup, ProductBufferLookup))
        {
            Reject(request.Delivery, ref delivery, ConstructionMaterialDeliveryOutcomeEnum.DemolitionConflict, true);
            return;
        }

        var outcome = Validate(delivery, request.ExpectedOwner, out int requirementIndex, out int sourceItemIndex);
        if (outcome != ConstructionMaterialDeliveryOutcomeEnum.Supplied)
        {
            Reject(request.Delivery, ref delivery, outcome, false);
            return;
        }

        if (request.ExpectedOwner != Entity.Null)
        {
            var sourceItems = StoredBufferLookup[request.ExpectedOwner];
            sourceItems.RemoveAt(sourceItemIndex);
        }
        // 동일 타입 버퍼 수정 이후 대상 버퍼를 새로 얻는다. 소유자가 현장인 입력은 검증에서 거부했다.
        var targetItems = StoredBufferLookup[delivery.TargetSite];
        targetItems.Add(new StoredItemElement(delivery.ItemEntity, delivery.ItemType, 0));
        if (BeltMovementLookup.HasComponent(delivery.ItemEntity))
        {
            BeltMovementLookup.SetComponentEnabled(delivery.ItemEntity, false);
        }

        ConstructionMaterialDeliveryOperations.ReleaseReservation(ref delivery, ReqBufferLookup);
        var requirements = ReqBufferLookup[delivery.TargetSite];
        var requirement = requirements[requirementIndex];
        requirement.DeliveredQuantity++;
        requirements[requirementIndex] = requirement;
        UpdateProgress(delivery.TargetSite, requirements);

        ECB.SetComponent(delivery.ItemEntity, ItemOwnership.Stored(delivery.TargetSite));
        ECB.AddComponent<DisableRendering>(delivery.ItemEntity);
        delivery.State = ConstructionMaterialDeliveryStateEnum.Supplied;
        DeliveryLookup[request.Delivery] = delivery;
        ConstructionMaterialDeliveryOperations.PublishResult(
            ref ECB, request.Delivery, delivery, ConstructionMaterialDeliveryOutcomeEnum.Supplied);
    }

    private ConstructionMaterialDeliveryOutcomeEnum Validate(
        in ConstructionMaterialDelivery delivery, Entity expectedOwner, out int requirementIndex, out int sourceItemIndex)
    {
        requirementIndex = -1;
        sourceItemIndex = -1;
        if (!SiteLookup.HasComponent(delivery.TargetSite) ||
            !ReqBufferLookup.HasBuffer(delivery.TargetSite) || !StoredBufferLookup.HasBuffer(delivery.TargetSite))
        {
            return ConstructionMaterialDeliveryOutcomeEnum.InvalidSite;
        }
        if ((SiteLookup[delivery.TargetSite].Flags & ConstructionSiteFlags.Cancelled) != 0)
        {
            return ConstructionMaterialDeliveryOutcomeEnum.SiteClosed;
        }
        if (!ItemIdentityLookup.HasComponent(delivery.ItemEntity) || !OwnershipLookup.HasComponent(delivery.ItemEntity))
        {
            return ConstructionMaterialDeliveryOutcomeEnum.InvalidItem;
        }
        if (delivery.ItemType == ItemTypeEnum.None || ItemIdentityLookup[delivery.ItemEntity].Type != delivery.ItemType)
        {
            return ConstructionMaterialDeliveryOutcomeEnum.InvalidItem;
        }
        if (ConstructionMaterialDeliveryOperations.ContainsStoredItem(StoredBufferLookup[delivery.TargetSite], delivery.ItemEntity))
        {
            return ConstructionMaterialDeliveryOutcomeEnum.AlreadySupplied;
        }
        if (ConstructionMaterialDeliveryOperations.HasTransferConflict(delivery.ItemEntity, TransferLookup))
        {
            return ConstructionMaterialDeliveryOutcomeEnum.ConflictingTransfer;
        }
        if (OwnershipLookup[delivery.ItemEntity].Owner != expectedOwner)
        {
            return ConstructionMaterialDeliveryOutcomeEnum.OwnershipMismatch;
        }
        if (expectedOwner != Entity.Null)
        {
            if (SiteLookup.HasComponent(expectedOwner))
            {
                // 다른 현장의 도착 자재 재배정은 허용하지 않는다.
                return ConstructionMaterialDeliveryOutcomeEnum.InvalidSource;
            }
            if (BuildingTypeLookup.HasComponent(expectedOwner) &&
                !ConstructionMaterialDeliveryOperations.IsSupplyBuilding(BuildingTypeLookup[expectedOwner].Type))
            {
                return ConstructionMaterialDeliveryOutcomeEnum.InvalidSource;
            }
            if (!StoredBufferLookup.HasBuffer(expectedOwner))
            {
                return ConstructionMaterialDeliveryOutcomeEnum.OwnershipMismatch;
            }
            if (!ConstructionMaterialDeliveryOperations.TryFindUniqueStoredItem(
                    StoredBufferLookup[expectedOwner], delivery.ItemEntity, delivery.ItemType, out sourceItemIndex) ||
                ConstructionMaterialDeliveryOperations.ContainsProductItem(expectedOwner, delivery.ItemEntity, ProductBufferLookup))
            {
                return ConstructionMaterialDeliveryOutcomeEnum.OwnershipMismatch;
            }
        }
        if (expectedOwner != delivery.SourceBuilding)
        {
            // 운반자 또는 월드로 정상 인계되었다면 원래 공급원 버퍼에는 실물이 없어야 한다.
            if (StoredBufferLookup.HasBuffer(delivery.SourceBuilding) &&
                ConstructionMaterialDeliveryOperations.ContainsStoredItem(StoredBufferLookup[delivery.SourceBuilding], delivery.ItemEntity))
            {
                return ConstructionMaterialDeliveryOutcomeEnum.OwnershipMismatch;
            }
            if (ConstructionMaterialDeliveryOperations.ContainsProductItem(delivery.SourceBuilding, delivery.ItemEntity, ProductBufferLookup))
            {
                return ConstructionMaterialDeliveryOutcomeEnum.OwnershipMismatch;
            }
        }
        var requirements = ReqBufferLookup[delivery.TargetSite];
        requirementIndex = ConstructionMaterialDeliveryOperations.FindRequirement(requirements, delivery.ItemType);
        if (requirementIndex < 0)
        {
            return ConstructionMaterialDeliveryOutcomeEnum.MaterialNotRequired;
        }
        if (requirements[requirementIndex].RemainingRequired <= 0)
        {
            return ConstructionMaterialDeliveryOutcomeEnum.RequirementSatisfied;
        }
        return ConstructionMaterialDeliveryOutcomeEnum.Supplied;
    }

    private void Reject(Entity entity, ref ConstructionMaterialDelivery delivery,
        ConstructionMaterialDeliveryOutcomeEnum outcome, bool preserveReservation)
    {
        if (!preserveReservation)
        {
            ConstructionMaterialDeliveryOperations.ReleaseReservation(ref delivery, ReqBufferLookup);
        }
        delivery.State = ConstructionMaterialDeliveryStateEnum.Rejected;
        DeliveryLookup[entity] = delivery;
        ConstructionMaterialDeliveryOperations.PublishResult(ref ECB, entity, delivery, outcome);
    }

    private void UpdateProgress(Entity siteEntity, in DynamicBuffer<ConstructionMaterialRequirementElement> requirements)
    {
        int required = 0;
        int delivered = 0;
        for (int i = 0; i < requirements.Length; i++)
        {
            required += requirements[i].RequiredQuantity;
            delivered += requirements[i].DeliveredQuantity;
        }
        if (required <= 0)
        {
            return;
        }
        var site = SiteLookup[siteEntity];
        site.Progress = math.clamp((float)delivered / required, 0f, 1f);
        SiteLookup[siteEntity] = site;
    }
}

/// <summary>
/// 완공 조건을 만족한 공사 현장을 감지하여 자재를 소비하고 완공 건물을 직접 생성하는 단일 워커 Burst Job.
/// </summary>
[BurstCompile]
public partial struct ConstructionCompletionApplyJob : IJobEntity
{
    public EntityCommandBuffer ECB;
    public NativeParallelHashSet<Entity> CompletedSites;
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
        CompletedSites.Add(siteEntity);
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

/// <summary>
/// 공사 수명주기 Job들이 공유하는 운송 기록 정산. 새 상태 소유자나 범용 인계 API가 아니다.
/// </summary>
internal static class ConstructionMaterialDeliveryOperations
{
    public static bool HasTransferConflict(Entity item, in ComponentLookup<TransferOwnershipRequest> transferLookup)
    {
        if (!transferLookup.HasComponent(item))
        {
            return false;
        }
        return transferLookup.IsComponentEnabled(item) || transferLookup[item].ProcessedInStateApply;
    }

    public static bool IsSupplyBuilding(BuildingTypeEnum type)
    {
        return type == BuildingTypeEnum.Storage ||
            type == BuildingTypeEnum.DroneStation ||
            type == BuildingTypeEnum.MainFacility;
    }

    public static int FindRequirement(in DynamicBuffer<ConstructionMaterialRequirementElement> requirements, ItemTypeEnum type)
    {
        for (int i = 0; i < requirements.Length; i++)
        {
            if (requirements[i].ItemType == type)
            {
                return i;
            }
        }
        return -1;
    }

    public static bool TryFindUniqueStoredItem(
        in DynamicBuffer<StoredItemElement> items, Entity item, ItemTypeEnum type, out int index)
    {
        index = -1;
        for (int i = 0; i < items.Length; i++)
        {
            if (items[i].ItemEntity != item)
            {
                continue;
            }
            if (index >= 0 || items[i].ItemType != type)
            {
                return false;
            }
            index = i;
        }
        return index >= 0;
    }

    public static bool ContainsStoredItem(in DynamicBuffer<StoredItemElement> items, Entity item)
    {
        for (int i = 0; i < items.Length; i++)
        {
            if (items[i].ItemEntity == item)
            {
                return true;
            }
        }
        return false;
    }

    public static bool ContainsProductItem(
        Entity owner, Entity item, in BufferLookup<ProductItemElement> productLookup)
    {
        if (!productLookup.HasBuffer(owner))
        {
            return false;
        }
        var products = productLookup[owner];
        for (int i = 0; i < products.Length; i++)
        {
            if (products[i].ItemEntity == item)
            {
                return true;
            }
        }
        return false;
    }

    public static void ReleaseReservation(
        ref ConstructionMaterialDelivery delivery,
        in BufferLookup<ConstructionMaterialRequirementElement> requirementsLookup)
    {
        if (!delivery.ReservationActive)
        {
            return;
        }
        delivery.ReservationActive = false;
        if (!requirementsLookup.HasBuffer(delivery.TargetSite))
        {
            return;
        }
        var requirements = requirementsLookup[delivery.TargetSite];
        int index = FindRequirement(requirements, delivery.ItemType);
        if (index < 0)
        {
            return;
        }
        var requirement = requirements[index];
        if (requirement.ReservedQuantity > 0)
        {
            requirement.ReservedQuantity--;
            requirements[index] = requirement;
        }
    }

    public static void PublishResult(
        ref EntityCommandBuffer ecb, Entity entity, in ConstructionMaterialDelivery delivery,
        ConstructionMaterialDeliveryOutcomeEnum outcome)
    {
        ecb.SetComponent(entity, new ConstructionMaterialDeliveryResult(delivery, outcome));
    }
}

/// <summary>
/// 기존 운송 기록에서 실물 선점을 재구성한다. F-004로 거부됐어도 활성 예약을 보존한 건은 포함한다.
/// </summary>
[BurstCompile]
public partial struct CollectConstructionMaterialClaimsJob : IJobEntity
{
    public NativeParallelHashSet<Entity> ClaimedItems;

    public void Execute(in ConstructionMaterialDelivery delivery)
    {
        if (delivery.ItemEntity == Entity.Null)
        {
            return;
        }
        if (delivery.State == ConstructionMaterialDeliveryStateEnum.Ready || delivery.ReservationActive)
        {
            ClaimedItems.Add(delivery.ItemEntity);
        }
    }
}

/// <summary>
/// 명시된 운송 건의 예약을 검증·등록하는 StateApply. 공급원 탐색이나 운송 출발은 수행하지 않는다.
/// 같은 기록의 재등록은 State로 차단하고 외부 Producer가 예약 숫자만 직접 쓰지 않게 한다.
/// </summary>
[BurstCompile]
public partial struct RegisterConstructionMaterialDeliveryJob : IJobEntity
{
    public EntityCommandBuffer ECB;
    public NativeParallelHashSet<Entity> ClaimedItems;
    public BufferLookup<ConstructionMaterialRequirementElement> RequirementsLookup;
    [ReadOnly] public ComponentLookup<ConstructionSite> SiteLookup;
    [ReadOnly] public ComponentLookup<BuildingType> BuildingTypeLookup;
    [ReadOnly] public ComponentLookup<Storage> StorageLookup;
    [ReadOnly] public ComponentLookup<ItemIdentity> IdentityLookup;
    [ReadOnly] public ComponentLookup<ItemOwnership> OwnershipLookup;
    [ReadOnly] public ComponentLookup<DestroyItemRequest> DestroyLookup;
    [ReadOnly] public ComponentLookup<TransferOwnershipRequest> TransferLookup;
    [ReadOnly] public BufferLookup<StoredItemElement> StoredLookup;
    [ReadOnly] public BufferLookup<ProductItemElement> ProductLookup;
    [ReadOnly] public NativeList<DemolishBuildingRequest> DemolitionRequests;

    public void Execute(Entity entity, ref ConstructionMaterialDelivery delivery)
    {
        if (delivery.State != ConstructionMaterialDeliveryStateEnum.PendingRegistration)
        {
            return;
        }

        // 생성자가 State/ReservationActive를 선행 승인하지 않는 공개 계약.
        delivery.ReservationActive = false;
        ConstructionMaterialDeliveryOutcomeEnum outcome = Validate(delivery, out int requirementIndex);
        if (outcome == ConstructionMaterialDeliveryOutcomeEnum.Registered)
        {
            ClaimedItems.Add(delivery.ItemEntity);
            if (delivery.ReserveMaterial)
            {
                var requirements = RequirementsLookup[delivery.TargetSite];
                var requirement = requirements[requirementIndex];
                requirement.ReservedQuantity++;
                requirements[requirementIndex] = requirement;
                delivery.ReservationActive = true;
            }
            delivery.State = ConstructionMaterialDeliveryStateEnum.Ready;
        }
        else
        {
            delivery.State = ConstructionMaterialDeliveryStateEnum.Rejected;
        }
        ECB.AddComponent(entity, new ConstructionMaterialDeliveryResult(delivery, outcome));
    }

    private ConstructionMaterialDeliveryOutcomeEnum Validate(
        in ConstructionMaterialDelivery delivery, out int requirementIndex)
    {
        requirementIndex = -1;
        if (delivery.TargetSite == Entity.Null)
        {
            return ConstructionMaterialDeliveryOutcomeEnum.InvalidSite;
        }
        if (!SiteLookup.HasComponent(delivery.TargetSite) ||
            !RequirementsLookup.HasBuffer(delivery.TargetSite) || !StoredLookup.HasBuffer(delivery.TargetSite))
        {
            return ConstructionMaterialDeliveryOutcomeEnum.InvalidSite;
        }
        if ((SiteLookup[delivery.TargetSite].Flags & ConstructionSiteFlags.Cancelled) != 0)
        {
            return ConstructionMaterialDeliveryOutcomeEnum.SiteClosed;
        }
        if (delivery.SourceBuilding == Entity.Null)
        {
            return ConstructionMaterialDeliveryOutcomeEnum.InvalidSource;
        }
        if (!BuildingTypeLookup.HasComponent(delivery.SourceBuilding) ||
            !StorageLookup.HasComponent(delivery.SourceBuilding) || !StoredLookup.HasBuffer(delivery.SourceBuilding))
        {
            return ConstructionMaterialDeliveryOutcomeEnum.InvalidSource;
        }
        if (!ConstructionMaterialDeliveryOperations.IsSupplyBuilding(BuildingTypeLookup[delivery.SourceBuilding].Type))
        {
            return ConstructionMaterialDeliveryOutcomeEnum.InvalidSource;
        }
        if (delivery.ItemEntity == Entity.Null)
        {
            return ConstructionMaterialDeliveryOutcomeEnum.InvalidItem;
        }
        if (!IdentityLookup.HasComponent(delivery.ItemEntity) || !OwnershipLookup.HasComponent(delivery.ItemEntity))
        {
            return ConstructionMaterialDeliveryOutcomeEnum.InvalidItem;
        }
        if (delivery.ItemType == ItemTypeEnum.None || IdentityLookup[delivery.ItemEntity].Type != delivery.ItemType)
        {
            return ConstructionMaterialDeliveryOutcomeEnum.InvalidItem;
        }
        if (DestroyLookup.HasComponent(delivery.ItemEntity) && DestroyLookup.IsComponentEnabled(delivery.ItemEntity))
        {
            return ConstructionMaterialDeliveryOutcomeEnum.DestroyConflict;
        }
        if (DemolishBuildingRequestLookup.ContainsBufferedItem(delivery.ItemEntity, DemolitionRequests, StoredLookup, ProductLookup))
        {
            return ConstructionMaterialDeliveryOutcomeEnum.DemolitionConflict;
        }
        if (ConstructionMaterialDeliveryOperations.HasTransferConflict(delivery.ItemEntity, TransferLookup))
        {
            return ConstructionMaterialDeliveryOutcomeEnum.ConflictingTransfer;
        }
        if (OwnershipLookup[delivery.ItemEntity].Owner != delivery.SourceBuilding)
        {
            return ConstructionMaterialDeliveryOutcomeEnum.OwnershipMismatch;
        }
        if (!ConstructionMaterialDeliveryOperations.TryFindUniqueStoredItem(
                StoredLookup[delivery.SourceBuilding], delivery.ItemEntity, delivery.ItemType, out _) ||
            ConstructionMaterialDeliveryOperations.ContainsProductItem(delivery.SourceBuilding, delivery.ItemEntity, ProductLookup))
        {
            return ConstructionMaterialDeliveryOutcomeEnum.OwnershipMismatch;
        }
        if (ClaimedItems.Contains(delivery.ItemEntity))
        {
            return ConstructionMaterialDeliveryOutcomeEnum.ItemAlreadyClaimed;
        }
        var requirements = RequirementsLookup[delivery.TargetSite];
        requirementIndex = ConstructionMaterialDeliveryOperations.FindRequirement(requirements, delivery.ItemType);
        if (requirementIndex < 0)
        {
            return ConstructionMaterialDeliveryOutcomeEnum.MaterialNotRequired;
        }
        if (requirements[requirementIndex].RemainingRequired <= 0)
        {
            return ConstructionMaterialDeliveryOutcomeEnum.RequirementSatisfied;
        }
        if (delivery.ReserveMaterial && requirements[requirementIndex].RemainingToReserve <= 0)
        {
            return ConstructionMaterialDeliveryOutcomeEnum.ReservationUnavailable;
        }
        return ConstructionMaterialDeliveryOutcomeEnum.Registered;
    }
}

[BurstCompile]
public partial struct CancelConstructionMaterialDeliveryJob : IJobEntity
{
    public EntityCommandBuffer ECB;
    public ComponentLookup<ConstructionMaterialDelivery> DeliveryLookup;
    public BufferLookup<ConstructionMaterialRequirementElement> RequirementsLookup;

    public void Execute(Entity entity, in CancelConstructionMaterialDeliveryRequest request)
    {
        ECB.DestroyEntity(entity);
        if (request.Delivery == Entity.Null)
        {
            return;
        }
        if (!DeliveryLookup.HasComponent(request.Delivery))
        {
            return;
        }
        var delivery = DeliveryLookup[request.Delivery];
        if (delivery.State == ConstructionMaterialDeliveryStateEnum.Supplied ||
            delivery.State == ConstructionMaterialDeliveryStateEnum.Cancelled)
        {
            return;
        }
        bool wasPendingRegistration = delivery.State == ConstructionMaterialDeliveryStateEnum.PendingRegistration;
        ConstructionMaterialDeliveryOperations.ReleaseReservation(ref delivery, RequirementsLookup);
        delivery.State = ConstructionMaterialDeliveryStateEnum.Cancelled;
        DeliveryLookup[request.Delivery] = delivery;
        if (wasPendingRegistration)
        {
            ECB.AddComponent(request.Delivery, new ConstructionMaterialDeliveryResult(
                delivery, ConstructionMaterialDeliveryOutcomeEnum.Cancelled));
        }
        else
        {
            ConstructionMaterialDeliveryOperations.PublishResult(
                ref ECB, request.Delivery, delivery, ConstructionMaterialDeliveryOutcomeEnum.Cancelled);
        }
    }
}

/// <summary>
/// 현장 취소/완공/외부 삭제로 더 이상 도착할 수 없는 운송을 종료한다.
/// 실물의 소멸/철거 충돌만으로는 실행하지 않아 F-004의 예약 보존을 유지한다.
/// </summary>
[BurstCompile]
public partial struct CloseConstructionMaterialDeliveryJob : IJobEntity
{
    public EntityCommandBuffer ECB;
    [ReadOnly] public ComponentLookup<ConstructionSite> SiteLookup;
    [ReadOnly] public NativeParallelHashSet<Entity> CompletedSites;
    public BufferLookup<ConstructionMaterialRequirementElement> RequirementsLookup;

    public void Execute(Entity entity, ref ConstructionMaterialDelivery delivery)
    {
        if (!delivery.ReservationActive && delivery.State != ConstructionMaterialDeliveryStateEnum.Ready)
        {
            return;
        }
        if (SiteLookup.HasComponent(delivery.TargetSite) &&
            (SiteLookup[delivery.TargetSite].Flags & ConstructionSiteFlags.Cancelled) == 0 &&
            !CompletedSites.Contains(delivery.TargetSite))
        {
            return;
        }
        ConstructionMaterialDeliveryOperations.ReleaseReservation(ref delivery, RequirementsLookup);
        delivery.State = ConstructionMaterialDeliveryStateEnum.Cancelled;
        ConstructionMaterialDeliveryOperations.PublishResult(
            ref ECB, entity, delivery, ConstructionMaterialDeliveryOutcomeEnum.SiteClosed);
    }
}
