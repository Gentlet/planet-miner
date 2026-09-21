using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// 아이템 엔티티의 탄생(생성) 및 죽음(파괴) 전담 시스템.
/// 
/// [책임]
/// - StateApplyGroup(Phase 5)에서 실행됩니다.
/// - SpawnItemRequest를 처리하여 아이템 엔티티를 생성하고 기본 컴포넌트를 초기화합니다.
/// - DestroyItemRequest가 활성화된 아이템 엔티티를 파괴합니다.
/// - 단일 워커 Burst Job(SpawnItemApplyJob, DestroyItemApplyJob)을 스케줄링하여
///   구조적 변경(Structural Change) 명령을 EndStateApplyEntityCommandBufferSystem에 비동기 기록합니다.
/// </summary>
[UpdateInGroup(typeof(StateApplyGroup))]
public partial struct ItemLifecycleApplySystem : ISystem
{
    private EntityArchetype _fallbackItemArchetype;
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

        // 1. [생성 Job] SpawnItemRequest 처리
        var spawnJob = new SpawnItemApplyJob
        {
            ECB = ecb,
            FallbackItemArchetype = _fallbackItemArchetype,
            StoredBufferLookup = _storedBufferLookup,
            ProductBufferLookup = _productBufferLookup
        };
        var spawnHandle = spawnJob.Schedule(_spawnQuery, state.Dependency);

        // 2. [파괴 Job] DestroyItemRequest 처리
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
        Entity newItem = ECB.CreateEntity(FallbackItemArchetype);

        ECB.SetComponent(newItem, new ItemIdentity(request.ItemType));
        ECB.SetComponent(newItem, new GridPosition(request.Position));
        ECB.SetComponent(newItem, LocalTransform.FromPosition(new float3(request.Position.x, request.Position.y, 0f)));

        if (request.TargetOwner == Entity.Null)
        {
            ECB.SetComponent(newItem, ItemOwnership.WorldItem);
        }
        else
        {
            ECB.SetComponent(newItem, ItemOwnership.Stored(request.TargetOwner));
            if (ProductBufferLookup.HasBuffer(request.TargetOwner))
            {
                ECB.AppendToBuffer(request.TargetOwner, new ProductItemElement(newItem, request.ItemType, 0));
            }
            else if (StoredBufferLookup.HasBuffer(request.TargetOwner))
            {
                ECB.AppendToBuffer(request.TargetOwner, new StoredItemElement(newItem, request.ItemType, 0));
            }
        }

        // 1회성 Request 컴포넌트들을 비활성화 상태로 초기화
        ECB.SetComponentEnabled<DestroyItemRequest>(newItem, false);
        ECB.SetComponentEnabled<TransferOwnershipRequest>(newItem, false);

        // 상태 및 의사결정 컴포넌트 비활성화 초기화
        ECB.SetComponentEnabled<BeltMovementState>(newItem, false);
        ECB.SetComponentEnabled<BeltMovementDecision>(newItem, false);
        ECB.SetComponentEnabled<BuildingItemInputDecision>(newItem, false);

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
