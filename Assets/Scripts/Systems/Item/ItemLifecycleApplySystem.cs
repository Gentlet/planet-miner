using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// 아이템 엔티티의 탄생(생성) 및 죽음(파괴) 전담 시스템.
/// 
/// [책임]
/// - StateApplyGroup(Phase 5)에서 실행.
/// - ProductResult를 처리하여 생산 완료 결과를 실제 아이템 엔티티와 ProductItemElement로 반영.
/// - SpawnItemRequest를 처리하여 아이템 엔티티를 생성하고 기본 컴포넌트를 초기화.
/// - DestroyItemRequest가 활성화된 아이템 엔티티를 파괴.
/// - 단일 워커 Burst Job들을 순차 스케줄링하여 구조적 변경(Structural Change) 명령을
///   EndStateApplyEntityCommandBufferSystem에 비동기 기록.
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

    public void OnCreate(ref SystemState state)
    {
        // [임시 Fallback 아키타입]
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
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
    }

    public void OnUpdate(ref SystemState state)
    {
        var ecbSystem = state.World.GetExistingSystemManaged<EndStateApplyEntityCommandBufferSystem>();
        var ecb = ecbSystem.CreateCommandBuffer();

        _storedBufferLookup.Update(ref state);
        _productBufferLookup.Update(ref state);

        // 1. [생산 결과 적용 Job] ProductResult -> 실제 Item + ProductItemElement
        var productResultJob = new ProductResultApplyJob
        {
            ECB = ecb,
            FallbackItemArchetype = _fallbackItemArchetype
        };
        var productResultHandle = productResultJob.Schedule(_productResultQuery, state.Dependency);

        // 2. [생성 Job] SpawnItemRequest 처리
        var spawnJob = new SpawnItemApplyJob
        {
            ECB = ecb,
            FallbackItemArchetype = _fallbackItemArchetype,
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

            for (int countIndex = 0; countIndex < result.Count; countIndex++)
            {
                Entity newItem = ECB.CreateEntity(FallbackItemArchetype);

                ECB.SetComponent(newItem, new ItemIdentity(result.ItemType));
                ECB.SetComponent(newItem, new GridPosition(int2.zero));
                ECB.SetComponent(newItem, LocalTransform.FromPosition(float3.zero));
                ECB.SetComponent(newItem, ItemOwnership.Stored(producerEntity));

                ECB.AppendToBuffer(
                    producerEntity,
                    new ProductItemElement(newItem, result.ItemType, result.SlotIndex));

                // 생산 건물 내부에 보관된 상태로 생성되므로 1회성 Request / 이동 관련 상태는 비활성화.
                ECB.SetComponentEnabled<DestroyItemRequest>(newItem, false);
                ECB.SetComponentEnabled<TransferOwnershipRequest>(newItem, false);
                ECB.SetComponentEnabled<BeltMovementState>(newItem, false);
                ECB.SetComponentEnabled<BuildingItemInputDecision>(newItem, false);
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
            Entity newItem = ECB.CreateEntity(FallbackItemArchetype);

            ECB.SetComponent(newItem, new ItemIdentity(request.ItemType));
            ECB.SetComponent(newItem, new GridPosition(request.Position));
            ECB.SetComponent(newItem, LocalTransform.FromPosition(isWorld
                ? new float3(request.Position.x, request.Position.y, 0f)
                : float3.zero));
            ECB.SetComponent(newItem, ownership);

            if (request.Destination == ItemSpawnDestination.Product)
            {
                ECB.AppendToBuffer(request.TargetOwner, new ProductItemElement(newItem, request.ItemType, request.TargetSlotIndex));
            }
            else if (request.Destination == ItemSpawnDestination.Storage)
            {
                ECB.AppendToBuffer(request.TargetOwner, new StoredItemElement(newItem, request.ItemType, request.TargetSlotIndex));
            }

            // 1회성 Request 컴포넌트들을 비활성화 상태로 초기화
            ECB.SetComponentEnabled<DestroyItemRequest>(newItem, false);
            ECB.SetComponentEnabled<TransferOwnershipRequest>(newItem, false);

            // 상태 및 의사결정 컴포넌트 비활성화 초기화
            ECB.SetComponentEnabled<BeltMovementState>(newItem, false);
            ECB.SetComponentEnabled<BuildingItemInputDecision>(newItem, false);
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
