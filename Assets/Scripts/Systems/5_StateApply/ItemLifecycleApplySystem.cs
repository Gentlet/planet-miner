using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

/// <summary>
/// 아이템 엔티티의 탄생(생성) 및 죽음(파괴) 전담 시스템.
/// 
/// [책임]
/// - StateApplyGroup(Phase 5)에서 실행.
/// - ProductResult를 처리하여 생산 완료 결과를 실제 아이템 엔티티와 ProductItemElement로 반영.
/// - SpawnItemRequest를 처리하여 아이템 엔티티를 생성하고 기본 컴포넌트를 초기화.
/// - DestroyItemRequest가 활성화된 아이템 엔티티를 파괴.
/// - 등록된 프리팹만 인스턴스화한다. DB/항목 누락은 시뮬레이션 중단 오류다.
/// - 수납 아이템에 대해 DisableRendering 컴포넌트를 부착하여 렌더 파이프라인에서 제외.
/// </summary>
[UpdateInGroup(typeof(StateApplyGroup))]
public partial struct ItemLifecycleApplySystem : ISystem
{
    private EntityQuery _productResultQuery;
    private EntityQuery _spawnQuery;
    private EntityQuery _destroyQuery;
    private BufferLookup<StoredItemElement> _storedBufferLookup;
    private BufferLookup<ProductItemElement> _productBufferLookup;
    private BufferLookup<ItemPrefabElement> _itemPrefabBufferLookup;
    private EntityQuery _prefabDbQuery;
    private EntityQuery _demolishQuery;

    public void OnCreate(ref SystemState state)
    {

        _storedBufferLookup = state.GetBufferLookup<StoredItemElement>(true);
        _productBufferLookup = state.GetBufferLookup<ProductItemElement>(true);
        _itemPrefabBufferLookup = state.GetBufferLookup<ItemPrefabElement>(true);

        _productResultQuery = SystemAPI.QueryBuilder()
            .WithAllRW<ProductResult>()
            .WithAll<ProductItemElement>()
            .Build();

        _spawnQuery = SystemAPI.QueryBuilder()
            .WithAll<SpawnItemRequest>()
            .Build();

        _destroyQuery = SystemAPI.QueryBuilder()
            .WithAll<DestroyItemRequest>()
            .Build();

        _prefabDbQuery = SystemAPI.QueryBuilder()
            .WithAll<ItemPrefabDatabase, ItemPrefabElement>()
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
        var ecbSystem = state.World.GetOrCreateSystemManaged<EndStateApplyEntityCommandBufferSystem>();
        var ecb = ecbSystem.CreateCommandBuffer();

        _storedBufferLookup.Update(ref state);
        _productBufferLookup.Update(ref state);
        _itemPrefabBufferLookup.Update(ref state);

        // EndCommand에서 거부/중복 요청이 제거되었으므로 남은 요청은 철거 대상이다.
        // 요청은 EndStateApply까지 생존하며, 이 임시 복사본은 생성 Job들이 사용한 뒤 해제한다.
        var demolitionRequests = _demolishQuery.ToComponentDataListAsync<DemolishBuildingRequest>(
            Allocator.TempJob, state.Dependency, out var demolitionRequestsHandle);

        bool hasPrefabDb = !_prefabDbQuery.IsEmptyIgnoreFilter;
        Entity prefabDbEntity = hasPrefabDb ? _prefabDbQuery.GetSingletonEntity() : Entity.Null;

        // 1. [생산 결과 적용 Job] ProductResult -> 실제 Item + ProductItemElement
        var productResultJob = new ProductResultApplyJob
        {
            ECB = ecb,
            HasPrefabDb = hasPrefabDb,
            PrefabDbEntity = prefabDbEntity,
            DemolitionRequests = demolitionRequests,
            ItemPrefabBufferLookup = _itemPrefabBufferLookup
        };
        var productResultHandle = productResultJob.Schedule(_productResultQuery, demolitionRequestsHandle);

        // 2. [생성 Job] SpawnItemRequest 처리
        var spawnJob = new SpawnItemApplyJob
        {
            ECB = ecb,
            HasPrefabDb = hasPrefabDb,
            PrefabDbEntity = prefabDbEntity,
            DemolitionRequests = demolitionRequests,
            ItemPrefabBufferLookup = _itemPrefabBufferLookup,
            StoredBufferLookup = _storedBufferLookup,
            ProductBufferLookup = _productBufferLookup
        };
        var spawnHandle = spawnJob.Schedule(_spawnQuery, productResultHandle);

        // 3. [파괴 Job] DestroyItemRequest 처리
        var destroyJob = new DestroyItemApplyJob
        {
            ECB = ecb
        };
        var destroyHandle = destroyJob.Schedule(_destroyQuery, spawnHandle);

        state.Dependency = demolitionRequests.Dispose(destroyHandle);
        ecbSystem.AddJobHandleForProducer(state.Dependency);
    }
}

internal static class DemolishBuildingRequestLookup
{
    public static bool ContainsTarget(
        Entity targetBuilding,
        in NativeList<DemolishBuildingRequest> requests)
    {
        if (targetBuilding == Entity.Null)
        {
            return false;
        }

        for (int i = 0; i < requests.Length; i++)
        {
            if (requests[i].TargetBuilding == targetBuilding)
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// 생산 건물의 ProductResult를 소비하여 실제 아이템 엔티티를 생성하고 ProductItemElement에 반영하는 단일 워커 Burst Job.
/// </summary>
[BurstCompile]
public partial struct ProductResultApplyJob : IJobEntity
{
    public EntityCommandBuffer ECB;
    [ReadOnly] public NativeList<DemolishBuildingRequest> DemolitionRequests;
    public bool HasPrefabDb;
    public Entity PrefabDbEntity;

    [ReadOnly]
    public BufferLookup<ItemPrefabElement> ItemPrefabBufferLookup;

    public void Execute(
        Entity producerEntity,
        ref DynamicBuffer<ProductResult> productResults)
    {
        if (productResults.Length == 0)
        {
            return;
        }

        if (DemolishBuildingRequestLookup.ContainsTarget(producerEntity, DemolitionRequests))
        {
            // 승인된 철거와 겹친 완료 생산물은 폐기한다. 선소비한 재료/자원은 보상하지 않는다.
            productResults.Clear();
            return;
        }

        for (int resultIndex = 0; resultIndex < productResults.Length; resultIndex++)
        {
            ProductResult result = productResults[resultIndex];
            if (result.ItemType == ItemTypeEnum.None || result.Count <= 0)
            {
                continue;
            }

            Entity prefabEntity = Entity.Null;
            if (HasPrefabDb && PrefabDbEntity != Entity.Null && ItemPrefabBufferLookup.HasBuffer(PrefabDbEntity))
            {
                var buffer = ItemPrefabBufferLookup[PrefabDbEntity];
                for (int i = 0; i < buffer.Length; i++)
                {
                    if (buffer[i].Type == result.ItemType)
                    {
                        prefabEntity = buffer[i].Prefab;
                        break;
                    }
                }
            }

            if (prefabEntity == Entity.Null)
            {
                // 프리팹 DB 환경에서 프리팹 미등록 시 Strict Fail: 스폰 중단하고 로깅 (Burst 호환 FixedString 사용)
                FixedString128Bytes msg = default;
                msg.Append((FixedString64Bytes)"[ItemLifecycleApplySystem] Missing prefab for item type '");
                msg.Append(result.ItemType.ToFixedString());
                msg.Append((FixedString64Bytes)"'. Product spawning skipped.");
                SimulationFailureUtility.Record(ref ECB, msg);
                continue;
            }

            for (int countIndex = 0; countIndex < result.Count; countIndex++)
            {
                Entity newItem = ItemLifecycleUtility.SpawnPrefabItem(
                    ref ECB, prefabEntity, result.ItemType, int2.zero, float3.zero,
                    ItemOwnership.Stored(producerEntity));

                ECB.AppendToBuffer(
                    producerEntity,
                    new ProductItemElement(newItem, result.ItemType, result.SlotIndex));
            }

        }

        // Consume-on-Apply: 논리적 생산 결과는 같은 StateApply 프레임에서 모두 소비.
        productResults.Clear();
    }
}

/// <summary>
/// SpawnItemRequest를 소비하여 새 아이템 엔티티를 생성 및 초기화하는 단일 워커 Burst Job.
/// </summary>
[BurstCompile]
public partial struct SpawnItemApplyJob : IJobEntity
{
    public EntityCommandBuffer ECB;
    [ReadOnly] public NativeList<DemolishBuildingRequest> DemolitionRequests;
    public bool HasPrefabDb;
    public Entity PrefabDbEntity;

    [ReadOnly]
    public BufferLookup<ItemPrefabElement> ItemPrefabBufferLookup;

    [ReadOnly]
    public BufferLookup<StoredItemElement> StoredBufferLookup;

    [ReadOnly]
    public BufferLookup<ProductItemElement> ProductBufferLookup;

    public void Execute(Entity requestEntity, in SpawnItemRequest request)
    {
        if (request.Destination == ItemSpawnDestination.Storage || request.Destination == ItemSpawnDestination.Product)
        {
            if (DemolishBuildingRequestLookup.ContainsTarget(request.TargetOwner, DemolitionRequests))
            {
                ECB.DestroyEntity(requestEntity);
                return;
            }
        }

        bool canSpawn = false;
        ItemOwnership ownership = default;
        bool isWorld = false;

        switch (request.Destination)
        {
            case ItemSpawnDestination.World:
                canSpawn = true;
                ownership = ItemOwnership.WorldItem;
                isWorld = true;
                break;

            case ItemSpawnDestination.Storage:
                if (request.TargetOwner != Entity.Null && StoredBufferLookup.HasBuffer(request.TargetOwner))
                {
                    canSpawn = true;
                    ownership = ItemOwnership.Stored(request.TargetOwner);
                }
                break;

            case ItemSpawnDestination.Product:
                if (request.TargetOwner != Entity.Null && ProductBufferLookup.HasBuffer(request.TargetOwner))
                {
                    canSpawn = true;
                    ownership = ItemOwnership.Stored(request.TargetOwner);
                }
                break;
        }

        if (canSpawn)
        {
            float3 spawnPos = isWorld
                ? new float3(request.Position.x, request.Position.y, 0f)
                : float3.zero;

            Entity prefabEntity = Entity.Null;
            if (HasPrefabDb && PrefabDbEntity != Entity.Null && ItemPrefabBufferLookup.HasBuffer(PrefabDbEntity))
            {
                var buffer = ItemPrefabBufferLookup[PrefabDbEntity];
                for (int i = 0; i < buffer.Length; i++)
                {
                    if (buffer[i].Type == request.ItemType)
                    {
                        prefabEntity = buffer[i].Prefab;
                        break;
                    }
                }
            }

            if (prefabEntity == Entity.Null)
            {
                // 프리팹 DB 환경에서 프리팹 미등록 시 Strict Fail: 스폰 중단하고 로깅 (Burst 호환 FixedString 사용)
                FixedString128Bytes msg = default;
                msg.Append((FixedString64Bytes)"[ItemLifecycleApplySystem] Missing prefab for item type '");
                msg.Append(request.ItemType.ToFixedString());
                msg.Append((FixedString64Bytes)"'. SpawnItemRequest rejected.");
                SimulationFailureUtility.Record(ref ECB, msg);
            }
            else
            {
                Entity newItem = ItemLifecycleUtility.SpawnPrefabItem(
                    ref ECB, prefabEntity, request.ItemType, request.Position, spawnPos, ownership);

                if (request.Destination == ItemSpawnDestination.Product)
                {
                    ECB.AppendToBuffer(request.TargetOwner, new ProductItemElement(newItem, request.ItemType, request.TargetSlotIndex));
                }
                else if (request.Destination == ItemSpawnDestination.Storage)
                {
                    ECB.AppendToBuffer(request.TargetOwner, new StoredItemElement(newItem, request.ItemType, request.TargetSlotIndex));
                }
            }

        }

        // Consume-on-Apply: 요청 엔티티 파괴
        ECB.DestroyEntity(requestEntity);
    }
}

/// <summary>
/// DestroyItemRequest가 켜진 엔티티를 파괴하는 단일 워커 Burst Job.
/// </summary>
[BurstCompile]
public partial struct DestroyItemApplyJob : IJobEntity
{
    public EntityCommandBuffer ECB;

    public void Execute(Entity entity, in DestroyItemRequest request)
    {
        ECB.DestroyEntity(entity);
    }
}

