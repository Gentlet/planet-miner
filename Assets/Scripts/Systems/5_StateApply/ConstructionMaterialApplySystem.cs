using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;

/// <summary>
/// 공사 현장 자재 수령 및 소유권 이전 전담 시스템.
/// 
/// [책임]
/// - StateApplyGroup(Phase 5)에서 실행.
/// - SupplyConstructionMaterialRequest를 소비하여 공사 현장으로의 자재 전달을 원자적으로 처리.
/// - 현장 유효성, 요구 품목 일치, 잔여 요구량(RemainingRequired) 존재 여부를 엄격히 검증.
/// - 승인 시: DeliveredQuantity 증가, ReservedQuantity 차감, 아이템 소유권을 현장으로 이전(ItemOwnership.Stored),
///   DisableRendering 부착, 현장 StoredItemElement 버퍼에 보관 등록.
/// - 거부 시 (Strict Rejection): 잘못된 품목, 초과 전달, 무효 현장인 경우 소유권 변경 없이 요청만 소비.
/// - 요청 엔티티는 단일 프레임 내에 파괴 (Consume-on-Apply).
/// </summary>
[UpdateInGroup(typeof(StateApplyGroup))]
public partial struct ConstructionMaterialApplySystem : ISystem
{
    private EntityQuery _requestQuery;
    private ComponentLookup<ConstructionSite> _siteLookup;
    private BufferLookup<ConstructionMaterialRequirementElement> _reqBufferLookup;
    private BufferLookup<StoredItemElement> _storedBufferLookup;
    private ComponentLookup<ItemIdentity> _itemIdentityLookup;
    private ComponentLookup<ItemOwnership> _itemOwnershipLookup;

    public void OnCreate(ref SystemState state)
    {
        _requestQuery = SystemAPI.QueryBuilder()
            .WithAll<SupplyConstructionMaterialRequest>()
            .Build();

        _siteLookup = state.GetComponentLookup<ConstructionSite>(false);
        _reqBufferLookup = state.GetBufferLookup<ConstructionMaterialRequirementElement>(false);
        _storedBufferLookup = state.GetBufferLookup<StoredItemElement>(false);
        _itemIdentityLookup = state.GetComponentLookup<ItemIdentity>(true);
        _itemOwnershipLookup = state.GetComponentLookup<ItemOwnership>(true);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
    }

    public void OnUpdate(ref SystemState state)
    {
        if (_requestQuery.IsEmptyIgnoreFilter)
        {
            return;
        }

        var ecbSystem = state.World.GetOrCreateSystemManaged<EndStateApplyEntityCommandBufferSystem>();
        var ecb = ecbSystem.CreateCommandBuffer();

        _siteLookup.Update(ref state);
        _reqBufferLookup.Update(ref state);
        _storedBufferLookup.Update(ref state);
        _itemIdentityLookup.Update(ref state);
        _itemOwnershipLookup.Update(ref state);

        var job = new SupplyConstructionMaterialApplyJob
        {
            ECB = ecb,
            SiteLookup = _siteLookup,
            ReqBufferLookup = _reqBufferLookup,
            StoredBufferLookup = _storedBufferLookup,
            ItemIdentityLookup = _itemIdentityLookup,
            ItemOwnershipLookup = _itemOwnershipLookup
        };

        var handle = job.Schedule(_requestQuery, state.Dependency);
        ecbSystem.AddJobHandleForProducer(handle);
        state.Dependency = handle;
    }
}

/// <summary>
/// SupplyConstructionMaterialRequest를 소비하여 자재 수령 및 소유권 이전을 적용하는 단일 워커 Burst Job.
/// </summary>
[BurstCompile]
public partial struct SupplyConstructionMaterialApplyJob : IJobEntity
{
    public EntityCommandBuffer ECB;

    public ComponentLookup<ConstructionSite> SiteLookup;
    public BufferLookup<ConstructionMaterialRequirementElement> ReqBufferLookup;
    public BufferLookup<StoredItemElement> StoredBufferLookup;

    [ReadOnly]
    public ComponentLookup<ItemIdentity> ItemIdentityLookup;

    [ReadOnly]
    public ComponentLookup<ItemOwnership> ItemOwnershipLookup;

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

        // 4. 정상 수령(Accept) 적용
        req.DeliveredQuantity++;
        if (req.ReservedQuantity > 0)
        {
            req.ReservedQuantity--;
        }
        reqBuffer[matchedIndex] = req;

        // 아이템 소유권을 현장으로 이전 및 렌더링 비활성화
        ECB.SetComponent(request.ItemEntity, ItemOwnership.Stored(request.TargetSite));
        ECB.AddComponent<DisableRendering>(request.ItemEntity);

        // 현장 StoredItemElement 버퍼에 보관 등록
        if (!StoredBufferLookup.HasBuffer(request.TargetSite))
        {
            ECB.AddBuffer<StoredItemElement>(request.TargetSite);
        }
        ECB.AppendToBuffer(request.TargetSite, new StoredItemElement(request.ItemEntity, request.ItemType, 0));

        // 5. 현장 진행도(Progress) 비율 갱신 (0.0f ~ 1.0f)
        int totalRequired = 0;
        int totalDelivered = 0;
        for (int i = 0; i < reqBuffer.Length; i++)
        {
            totalRequired += reqBuffer[i].RequiredQuantity;
            totalDelivered += reqBuffer[i].DeliveredQuantity;
        }

        site = SiteLookup[request.TargetSite];
        if (totalRequired > 0)
        {
            site.Progress = math.clamp((float)totalDelivered / totalRequired, 0f, 1f);
        }
        SiteLookup[request.TargetSite] = site;

        // 6. 요청 엔티티 소비 (Consume-on-Apply)
        ECB.DestroyEntity(requestEntity);
    }
}
