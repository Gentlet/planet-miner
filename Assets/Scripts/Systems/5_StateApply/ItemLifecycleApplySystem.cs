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
/// - 프리팹 데이터베이스(ItemPrefabElement)에 등록된 프리팹을 우선 인스턴스화하고, 미등록 시 Safe Fallback 아키타입으로 안전 대체.
/// - 수납 아이템에 대해 DisableRendering 컴포넌트를 부착하여 렌더 파이프라인에서 제외.
/// </summary>
[UpdateInGroup(typeof(StateApplyGroup))]
public partial struct ItemLifecycleApplySystem : ISystem
{
    private EntityArchetype _fallbackItemArchetype;
    private EntityQuery _productResultQuery;
    private EntityQuery _spawnQuery;
    private EntityQuery _destroyQuery;
    private BufferLookup<StoredItemElement> _storedBufferLookup;
    private BufferLookup<ProductItemElement> _productBufferLookup;
    private BufferLookup<ItemPrefabElement> _itemPrefabBufferLookup;
    private EntityQuery _prefabDbQuery;

    public void OnCreate(ref SystemState state)
    {
        // 프리팹 누락 시 사용할 Fallback 시뮬레이션 아키타입
        _fallbackItemArchetype = state.EntityManager.CreateArchetype(
            ComponentType.ReadWrite<ItemIdentity>(),
            ComponentType.ReadWrite<ItemOwnership>(),
            ComponentType.ReadWrite<GridPosition>(),
            ComponentType.ReadWrite<LocalTransform>(),
            ComponentType.ReadWrite<DestroyItemRequest>(),
            ComponentType.ReadWrite<TransferOwnershipRequest>(),
            ComponentType.ReadWrite<BeltMovementState>(),
            ComponentType.ReadWrite<BeltMovementDecision>(),
            ComponentType.ReadWrite<BuildingItemInputDecision>()
        );

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

        Entity prefabDbEntity = _prefabDbQuery.IsEmptyIgnoreFilter
            ? Entity.Null
            : _prefabDbQuery.GetSingletonEntity();

        // 1. [생산 결과 적용 Job] ProductResult -> 실제 Item + ProductItemElement
        var productResultJob = new ProductResultApplyJob
        {
            ECB = ecb,
            FallbackItemArchetype = _fallbackItemArchetype,
            ItemPrefabBufferLookup = _itemPrefabBufferLookup,
            PrefabDbEntity = prefabDbEntity
        };
        var productResultHandle = productResultJob.Schedule(_productResultQuery, state.Dependency);

        // 2. [생성 Job] SpawnItemRequest 처리
        var spawnJob = new SpawnItemApplyJob
        {
            ECB = ecb,
            FallbackItemArchetype = _fallbackItemArchetype,
            ItemPrefabBufferLookup = _itemPrefabBufferLookup,
            PrefabDbEntity = prefabDbEntity,
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

        ecbSystem.AddJobHandleForProducer(destroyHandle);
        state.Dependency = destroyHandle;
    }
}

/// <summary>
/// 생산 건물의 ProductResult를 소비하여 실제 아이템 엔티티를 생성하고 ProductItemElement에 반영하는 단일 워커 Burst Job.
/// </summary>
[BurstCompile]
public partial struct ProductResultApplyJob : IJobEntity
{
    public EntityCommandBuffer ECB;
    public EntityArchetype FallbackItemArchetype;

    [ReadOnly]
    public BufferLookup<ItemPrefabElement> ItemPrefabBufferLookup;

    public Entity PrefabDbEntity;

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
            if (PrefabDbEntity != Entity.Null && ItemPrefabBufferLookup.HasBuffer(PrefabDbEntity))
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

            for (int countIndex = 0; countIndex < result.Count; countIndex++)
            {
                Entity newItem;
                if (prefabEntity != Entity.Null)
                {
                    newItem = ECB.Instantiate(prefabEntity);
                    ECB.SetComponent(newItem, new ItemIdentity(result.ItemType));
                    ECB.AddComponent(newItem, new GridPosition(int2.zero));
                    ECB.SetComponent(newItem, LocalTransform.FromPosition(float3.zero));
                    ECB.AddComponent(newItem, ItemOwnership.Stored(producerEntity));

                    ECB.AddComponent<DestroyItemRequest>(newItem);
                    ECB.SetComponentEnabled<DestroyItemRequest>(newItem, false);
                    ECB.AddComponent<TransferOwnershipRequest>(newItem);
                    ECB.SetComponentEnabled<TransferOwnershipRequest>(newItem, false);
                    ECB.AddComponent<BeltMovementState>(newItem);
                    ECB.SetComponentEnabled<BeltMovementState>(newItem, false);
                    ECB.AddComponent<BeltMovementDecision>(newItem);
                    ECB.AddComponent<BuildingItemInputDecision>(newItem);
                    ECB.SetComponentEnabled<BuildingItemInputDecision>(newItem, false);
                }
                else
                {
                    newItem = ECB.CreateEntity(FallbackItemArchetype);
                    ECB.SetComponent(newItem, new ItemIdentity(result.ItemType));
                    ECB.SetComponent(newItem, new GridPosition(int2.zero));
                    ECB.SetComponent(newItem, LocalTransform.FromPosition(float3.zero));
                    ECB.SetComponent(newItem, ItemOwnership.Stored(producerEntity));

                    ECB.SetComponentEnabled<DestroyItemRequest>(newItem, false);
                    ECB.SetComponentEnabled<TransferOwnershipRequest>(newItem, false);
                    ECB.SetComponentEnabled<BeltMovementState>(newItem, false);
                    ECB.SetComponentEnabled<BuildingItemInputDecision>(newItem, false);
                }

                // 생산 시설 내부 보관 아이템이므로 렌더링 비활성화
                ECB.AddComponent<DisableRendering>(newItem);

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
    public EntityArchetype FallbackItemArchetype;

    [ReadOnly]
    public BufferLookup<ItemPrefabElement> ItemPrefabBufferLookup;

    public Entity PrefabDbEntity;

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
            Entity prefabEntity = Entity.Null;
            if (PrefabDbEntity != Entity.Null && ItemPrefabBufferLookup.HasBuffer(PrefabDbEntity))
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

            float3 spawnPos = isWorld
                ? new float3(request.Position.x, request.Position.y, 0f)
                : float3.zero;

            Entity newItem;
            if (prefabEntity != Entity.Null)
            {
                newItem = ECB.Instantiate(prefabEntity);
                ECB.SetComponent(newItem, new ItemIdentity(request.ItemType));
                ECB.AddComponent(newItem, new GridPosition(request.Position));
                ECB.SetComponent(newItem, LocalTransform.FromPosition(spawnPos));
                ECB.AddComponent(newItem, ownership);

                ECB.AddComponent<DestroyItemRequest>(newItem);
                ECB.SetComponentEnabled<DestroyItemRequest>(newItem, false);
                ECB.AddComponent<TransferOwnershipRequest>(newItem);
                ECB.SetComponentEnabled<TransferOwnershipRequest>(newItem, false);
                ECB.AddComponent<BeltMovementState>(newItem);
                ECB.SetComponentEnabled<BeltMovementState>(newItem, false);
                ECB.AddComponent<BeltMovementDecision>(newItem);
                ECB.AddComponent<BuildingItemInputDecision>(newItem);
                ECB.SetComponentEnabled<BuildingItemInputDecision>(newItem, false);
            }
            else
            {
                newItem = ECB.CreateEntity(FallbackItemArchetype);
                ECB.SetComponent(newItem, new ItemIdentity(request.ItemType));
                ECB.SetComponent(newItem, new GridPosition(request.Position));
                ECB.SetComponent(newItem, LocalTransform.FromPosition(spawnPos));
                ECB.SetComponent(newItem, ownership);

                ECB.SetComponentEnabled<DestroyItemRequest>(newItem, false);
                ECB.SetComponentEnabled<TransferOwnershipRequest>(newItem, false);
                ECB.SetComponentEnabled<BeltMovementState>(newItem, false);
                ECB.SetComponentEnabled<BuildingItemInputDecision>(newItem, false);
            }

            // 수납 아이템의 경우 렌더링 비활성화
            if (!isWorld)
            {
                ECB.AddComponent<DisableRendering>(newItem);
            }

            if (request.Destination == ItemSpawnDestination.Product)
            {
                ECB.AppendToBuffer(request.TargetOwner, new ProductItemElement(newItem, request.ItemType, request.TargetSlotIndex));
            }
            else if (request.Destination == ItemSpawnDestination.Storage)
            {
                ECB.AppendToBuffer(request.TargetOwner, new StoredItemElement(newItem, request.ItemType, request.TargetSlotIndex));
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

