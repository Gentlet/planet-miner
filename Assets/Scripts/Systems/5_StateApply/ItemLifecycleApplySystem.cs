using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

/// <summary>
/// 역할·목적: 생산 결과/직접 생성 요청을 실물 아이템으로 바꾸고 활성 삭제 요청의 실물을 제거한다.
/// 처리 단계: StateApply. 입력은 ProductResult, SpawnItemRequest, 활성 DestroyItemRequest 및 등록된 아이템 프리팹 DB다.
/// 출력·소유권: 아이템 초기화와 목적 보관/생산 버퍼 등록, 렌더 태그, 실물 삭제를 EndStateApply에 기록한다. 기존 실물의 드론 인계는 Ownership API가 담당한다.
/// World Spawn은 현재 현장 footprint로 다시 검사해 완공 검사 뒤 현장 내부에 새 실물이 생성되는 경우를 막는다.
/// 정리·가시화: 생산 결과는 Clear, 처리한 Spawn 요청/Destroy 실물은 EndStateApply에 삭제한다. DB 누락은 대체 생성 없이 중단 오류를 기록한다.
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

        // Admission 이후 생성된 현장과 Admission을 거치지 않은 요청도 현재 현장 기준으로 재검사한다.
        var constructionFootprints = ConstructionSiteWorldItemUtility.CaptureFootprints(state.EntityManager, Allocator.TempJob);

        bool hasPrefabDb = !_prefabDbQuery.IsEmptyIgnoreFilter;
        Entity prefabDbEntity = hasPrefabDb ? _prefabDbQuery.GetSingletonEntity() : Entity.Null;

        // 1. [생산 결과 적용 Job] ProductResult -> 실제 Item + ProductItemElement
        var productResultJob = new ProductResultApplyJob
        {
            ECB = ecb,
            HasPrefabDb = hasPrefabDb,
            PrefabDbEntity = prefabDbEntity,
            ItemPrefabBufferLookup = _itemPrefabBufferLookup
        };
        var productResultHandle = productResultJob.Schedule(_productResultQuery, state.Dependency);

        // 2. [생성 Job] SpawnItemRequest 처리
        var spawnJob = new SpawnItemApplyJob
        {
            ECB = ecb,
            HasPrefabDb = hasPrefabDb,
            PrefabDbEntity = prefabDbEntity,
            ItemPrefabBufferLookup = _itemPrefabBufferLookup,
            StoredBufferLookup = _storedBufferLookup,
            ProductBufferLookup = _productBufferLookup,
            ConstructionFootprints = constructionFootprints
        };
        var spawnHandle = spawnJob.Schedule(_spawnQuery, productResultHandle);

        // 3. [파괴 Job] DestroyItemRequest 처리
        var destroyJob = new DestroyItemApplyJob
        {
            ECB = ecb
        };
        var destroyHandle = destroyJob.Schedule(_destroyQuery, spawnHandle);

        state.Dependency = constructionFootprints.Dispose(destroyHandle);
        ecbSystem.AddJobHandleForProducer(state.Dependency);
    }
}

/// <summary>
/// 생산 건물의 ProductResult를 소비하여 실제 아이템 엔티티를 생성하고 ProductItemElement에 반영하는 단일 워커 Burst Job.
/// </summary>
[BurstCompile]
public partial struct ProductResultApplyJob : IJobEntity
{
    public EntityCommandBuffer ECB;
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
    public bool HasPrefabDb;
    public Entity PrefabDbEntity;
    [ReadOnly] public NativeArray<ConstructionSiteWorldItemUtility.Footprint> ConstructionFootprints;

    [ReadOnly]
    public BufferLookup<ItemPrefabElement> ItemPrefabBufferLookup;

    [ReadOnly]
    public BufferLookup<StoredItemElement> StoredBufferLookup;

    [ReadOnly]
    public BufferLookup<ProductItemElement> ProductBufferLookup;

    public void Execute(Entity requestEntity, in SpawnItemRequest request)
    {
        bool canSpawn = false;
        ItemOwnership ownership = default;
        bool isWorld = false;

        switch (request.Destination)
        {
            case ItemSpawnDestination.World:
                canSpawn = ConstructionSiteWorldItemUtility.IsWorldPositionAllowed(ConstructionFootprints, request.Position);
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
