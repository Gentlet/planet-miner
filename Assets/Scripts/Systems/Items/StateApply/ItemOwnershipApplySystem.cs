using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Rendering;
using Unity.Transforms;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: 아이템 Owner 원본과 소유권에 따른 렌더 상태를 반영하는 공통 실물 소유자.
/// 처리 단계: StateApply. 일반 TransferOwnershipRequest는 단일 워커 Job으로 적용하며 선행 저장/라우팅 경계가 버퍼·위치를 정한다.
/// 출력·소유권: 일반 요청은 Owner/렌더를 반영하고, TryTransferItem은 성공 실물의 보관 버퍼·Owner·위치·벨트 정지·렌더를 함께 반영한다.
/// DroneTaskLifecycleApplySystem은 일반 Ownership 이후 이 API를 호출하며 품목·수량·대상·슬롯 선택과 드론 정산은 호출자가 담당한다.
/// 정리·가시화: Transfer는 처리 후 비활성화한다. 렌더의 구조 변경은 EndBuilding, 일반 DestroyItemRequest 실물 삭제는 ItemLifecycleApplySystem이 담당한다.
/// 활성 Destroy 실물은 이전하지 않으며 철거 건물의 이전 Transfer 차단은 Command가 담당한다.
/// </summary>
[UpdateInGroup(typeof(BuildingStateApplyGroup))]
[BurstCompile]
public partial struct ItemOwnershipApplySystem : ISystem
{
    private EntityStorageInfoLookup _entityStorageInfoLookup;
    private ComponentLookup<DestroyItemRequest> _destroyRequestLookup;
    private EntityQuery _requestQuery;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        _entityStorageInfoLookup = state.GetEntityStorageInfoLookup();
        _destroyRequestLookup = state.GetComponentLookup<DestroyItemRequest>(true);

        _requestQuery = SystemAPI.QueryBuilder()
            .WithAllRW<ItemOwnership>()
            .WithAllRW<TransferOwnershipRequest>()
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

        var ecbSystem = state.World.GetOrCreateSystemManaged<EndBuildingEntityCommandBufferSystem>();
        var ecb = ecbSystem.CreateCommandBuffer();
        var job = new ItemOwnershipApplyJob
        {
            EntityStorageInfoLookup = _entityStorageInfoLookup,
            DestroyRequestLookup = _destroyRequestLookup,
            ECB = ecb
        };

        var handle = job.Schedule(_requestQuery, state.Dependency);
        state.Dependency = handle;
        ecbSystem.AddJobHandleForProducer(state.Dependency);
    }

    /// <summary>
    /// 활성 일반 Transfer의 예정 소유자를 포함한 조회 값. ItemOwnership의 원본을 여기서 변경하지 않는다.
    /// TryTransferItem은 별도로 실제 Owner 일치를 검사하므로 예정 소유자만 보고 실물을 중복 이전하지 않는다.
    /// </summary>
    public static Entity EffectiveOwner(EntityManager manager, Entity item)
    {
        if (!manager.Exists(item)) return Entity.Null;
        if (!manager.HasComponent<ItemOwnership>(item)) return Entity.Null;
        if (manager.HasComponent<TransferOwnershipRequest>(item) &&
            manager.IsComponentEnabled<TransferOwnershipRequest>(item))
        {
            return manager.GetComponentData<TransferOwnershipRequest>(item).TargetOwner;
        }
        return manager.GetComponentData<ItemOwnership>(item).Owner;
    }

    /// <summary>계획의 실물이 아직 존재하며 예상 소유자에게 있는지 확인한다.</summary>
    public static bool CanTransferItem(EntityManager manager, Entity item, ItemTypeEnum type, Entity expectedOwner)
    {
        if (!IsActiveTransferEntity(manager, item)) return false;
        if (!manager.HasComponent<ItemIdentity>(item)) return false;
        if (!manager.HasComponent<ItemOwnership>(item)) return false;
        if (!manager.HasComponent<GridPosition>(item)) return false;
        if (manager.HasComponent<DestroyItemRequest>(item) &&
            manager.IsComponentEnabled<DestroyItemRequest>(item)) return false;
        return manager.GetComponentData<ItemIdentity>(item).Type == type &&
               EffectiveOwner(manager, item) == expectedOwner;
    }

    /// <summary>
    /// 일반 이전 요청 적용 이후 호출하는 공통 실물 반영 경계.
    /// 수량·대상·슬롯 선택은 호출자가 소유하며, 성공한 한 실물의 버퍼·소유권·위치를 함께 반영한다.
    /// 렌더 구조 변경은 호출자가 전달한 종료 ECB에 기록한다. 새 Transfer 요청을 다시 발행하지 않는다.
    /// </summary>
    public static bool TryTransferItem(EntityManager manager, Entity item, ItemTypeEnum type,
        Entity sourceOwner, Entity targetOwner, int slotIndex, int2 position, EntityCommandBuffer ecb)
    {
        if (sourceOwner == targetOwner) return false;
        if (!CanTransferItem(manager, item, type, sourceOwner)) return false;
        var ownership = manager.GetComponentData<ItemOwnership>(item);
        // 조회용 예정 소유자만 일치하는 상태는 아직 공통 반영 경계를 지난 실물이 아니다.
        if (ownership.Owner != sourceOwner) return false;
        int sourceIndex = -1;
        if (sourceOwner != Entity.Null)
        {
            if (!IsActiveTransferEntity(manager, sourceOwner)) return false;
            if (!manager.HasBuffer<StoredItemElement>(sourceOwner)) return false;
            sourceIndex = FindStoredItem(manager.GetBuffer<StoredItemElement>(sourceOwner, true), item, type);
            if (sourceIndex < 0) return false;
        }
        // 버퍼를 쓰기 전에 출발 참조의 유일성과 도착 버퍼/슬롯/중복을 모두 확인한다.
        // 실패 시 출발 버퍼만 제거된 실물이 남지 않도록 검사를 실제 변경보다 먼저 끝낸다.
        if (targetOwner != Entity.Null)
        {
            if (!IsActiveTransferEntity(manager, targetOwner)) return false;
            if (!manager.HasBuffer<StoredItemElement>(targetOwner)) return false;
            if (slotIndex < 0) return false;
            if (ContainsStoredItem(manager.GetBuffer<StoredItemElement>(targetOwner, true), item)) return false;
        }

        // 아이템 엔티티를 재생성하지 않고 같은 실물의 출발/도착 참조와 Owner를 바꾼다.
        // 인계 수량은 이 API의 성공 횟수로 호출자가 정산하며 새 Transfer 요청을 중복 발행하지 않는다.
        if (sourceOwner != Entity.Null)
            manager.GetBuffer<StoredItemElement>(sourceOwner).RemoveAt(sourceIndex);
        if (targetOwner != Entity.Null)
        {
            manager.GetBuffer<StoredItemElement>(targetOwner).Add(new StoredItemElement
            {
                ItemEntity = item, ItemType = type, SlotIndex = slotIndex
            });
        }
        manager.SetComponentData(item, new GridPosition(position));
        if (manager.HasComponent<LocalTransform>(item))
        {
            var transform = manager.GetComponentData<LocalTransform>(item);
            transform.Position = new float3(position.x, position.y, transform.Position.z);
            manager.SetComponentData(item, transform);
        }
        if (manager.HasComponent<BeltMovementState>(item))
            manager.SetComponentEnabled<BeltMovementState>(item, false);

        // 실물 값은 즉시 반영하고 렌더 태그의 구조 변경만 호출자의 종료 ECB에 기록한다.
        // 월드 회수는 숨기고 월드 방출은 표시한다. 수납 장소 간 이동은 기존 숨김을 유지한다.
        if (sourceOwner == Entity.Null) ecb.AddComponent<DisableRendering>(item);
        else if (targetOwner == Entity.Null) ecb.RemoveComponent<DisableRendering>(item);
        manager.SetComponentData(item, targetOwner == Entity.Null
            ? ItemOwnership.WorldItem : ItemOwnership.Stored(targetOwner));
        if (manager.HasComponent<TransferOwnershipRequest>(item))
            manager.SetComponentEnabled<TransferOwnershipRequest>(item, false);
        return true;
    }

    private static bool IsActiveTransferEntity(EntityManager manager, Entity entity)
    {
        if (entity == Entity.Null) return false;
        if (!manager.Exists(entity)) return false;
        return !manager.HasComponent<Disabled>(entity) && !manager.HasComponent<Prefab>(entity);
    }

    private static int FindStoredItem(DynamicBuffer<StoredItemElement> items, Entity item, ItemTypeEnum type)
    {
        int index = -1;
        for (int i = 0; i < items.Length; i++)
        {
            if (items[i].ItemEntity != item) continue;
            if (index >= 0 || items[i].ItemType != type) return -1;
            index = i;
        }
        return index;
    }

    private static bool ContainsStoredItem(DynamicBuffer<StoredItemElement> items, Entity item)
    {
        for (int i = 0; i < items.Length; i++)
        {
            if (items[i].ItemEntity == item) return true;
        }
        return false;
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

        requestEnabled.ValueRW = false;
    }
}
