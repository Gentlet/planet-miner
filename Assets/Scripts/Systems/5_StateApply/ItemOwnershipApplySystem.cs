using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Rendering;

/// <summary>
/// 아이템 소유권 상태 적용 시스템 (State Owner).
/// 
/// [책임]
/// - StateApplyGroup(Phase 5)에서 실행.
/// - TransferOwnershipRequest를 처리하여 ItemOwnership의 단일 원본(Source of Truth)을 갱신.
/// - 소유권 전환(수납 <-> 방출)에 따라 DisableRendering 컴포넌트를 추가/제거하여 렌더링 표시 상태 동기화.
/// - 단일 워커 Burst Job(ItemOwnershipApplyJob)으로 소유권 변경 순차 적용.
/// - Consume-on-Apply 원칙에 따라 처리 즉시 TransferOwnershipRequest를 비활성화.
/// - Destroy 대상과 철거 소유 버퍼에 남은 실물은 요청만 소비하고 최종 변경을 해당 수명주기 경로에 맡긴다.
/// </summary>
[UpdateInGroup(typeof(StateApplyGroup))]
[BurstCompile]
public partial struct ItemOwnershipApplySystem : ISystem
{
    private EntityStorageInfoLookup _entityStorageInfoLookup;
    private ComponentLookup<DestroyItemRequest> _destroyRequestLookup;
    private BufferLookup<StoredItemElement> _storedBufferLookup;
    private BufferLookup<ProductItemElement> _productBufferLookup;
    private EntityQuery _requestQuery;
    private EntityQuery _demolishQuery;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        _entityStorageInfoLookup = state.GetEntityStorageInfoLookup();
        _destroyRequestLookup = state.GetComponentLookup<DestroyItemRequest>(true);
        _storedBufferLookup = state.GetBufferLookup<StoredItemElement>(true);
        _productBufferLookup = state.GetBufferLookup<ProductItemElement>(true);

        _requestQuery = SystemAPI.QueryBuilder()
            .WithAllRW<ItemOwnership>()
            .WithAllRW<TransferOwnershipRequest>()
            .Build();

        _demolishQuery = SystemAPI.QueryBuilder()
            .WithAll<DemolishBuildingRequest>()
            .Build();
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
    }

    public void OnUpdate(ref SystemState state)
    {
        _entityStorageInfoLookup.Update(ref state);
        _destroyRequestLookup.Update(ref state);
        _storedBufferLookup.Update(ref state);
        _productBufferLookup.Update(ref state);

        var ecbSystem = state.World.GetOrCreateSystemManaged<EndStateApplyEntityCommandBufferSystem>();
        var ecb = ecbSystem.CreateCommandBuffer();
        var demolitionRequests = _demolishQuery.ToComponentDataListAsync<DemolishBuildingRequest>(
            Allocator.TempJob, state.Dependency, out var demolitionRequestsHandle);

        var job = new ItemOwnershipApplyJob
        {
            EntityStorageInfoLookup = _entityStorageInfoLookup,
            DestroyRequestLookup = _destroyRequestLookup,
            StoredBufferLookup = _storedBufferLookup,
            ProductBufferLookup = _productBufferLookup,
            DemolitionRequests = demolitionRequests,
            ECB = ecb
        };

        var handle = job.Schedule(_requestQuery, demolitionRequestsHandle);
        state.Dependency = demolitionRequests.Dispose(handle);
        ecbSystem.AddJobHandleForProducer(state.Dependency);
    }
}

/// <summary>
/// TransferOwnershipRequest를 순차적으로 소비하여 ItemOwnership을 갱신하는 단일 워커 Burst Job.
/// </summary>
[BurstCompile]
public partial struct ItemOwnershipApplyJob : IJobEntity
{
    [ReadOnly]
    public EntityStorageInfoLookup EntityStorageInfoLookup;

    [ReadOnly] public ComponentLookup<DestroyItemRequest> DestroyRequestLookup;
    [ReadOnly] public BufferLookup<StoredItemElement> StoredBufferLookup;
    [ReadOnly] public BufferLookup<ProductItemElement> ProductBufferLookup;
    [ReadOnly] public NativeList<DemolishBuildingRequest> DemolitionRequests;

    public EntityCommandBuffer ECB;

    public void Execute(
        Entity entity,
        ref ItemOwnership ownership,
        RefRW<TransferOwnershipRequest> request,
        EnabledRefRW<TransferOwnershipRequest> requestEnabled)
    {
        if (DestroyRequestLookup.HasComponent(entity) && DestroyRequestLookup.IsComponentEnabled(entity))
        {
            requestEnabled.ValueRW = false;
            return;
        }

        // 입출고 적용 후에도 철거 대상 버퍼에 남은 실물은 철거 경로만 최종 반환한다.
        if (DemolishBuildingRequestLookup.ContainsBufferedItem(
                entity, DemolitionRequests, StoredBufferLookup, ProductBufferLookup))
        {
            requestEnabled.ValueRW = false;
            return;
        }

        Entity targetOwner = request.ValueRO.TargetOwner;

        if (targetOwner == Entity.Null)
        {
            // 수납 -> 월드로 방출: 렌더링 활성화
            if (ownership.IsStored)
            {
                ECB.RemoveComponent<DisableRendering>(entity);
            }
            ownership = ItemOwnership.WorldItem;
        }
        else if (EntityStorageInfoLookup.Exists(targetOwner))
        {
            // 월드 -> 시설 수납: 렌더링 비활성화
            if (ownership.IsWorldItem)
            {
                ECB.AddComponent<DisableRendering>(entity);
            }
            ownership = ItemOwnership.Stored(targetOwner);
        }
        // 수신자가 유효하지 않은(파괴된) 유령 엔티티인 경우 소유권 변경을 무시하고 Drop

        // Consume-on-Apply: 처리 완료 즉시 비활성화
        requestEnabled.ValueRW = false;
    }
}

