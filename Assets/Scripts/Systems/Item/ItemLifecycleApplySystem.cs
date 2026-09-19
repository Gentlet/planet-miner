using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// 아이템 엔티티의 탄생(생성) 및 죽음(파괴) 전담 시스템.
/// 
/// [책임]
/// - SpawnItemRequest를 처리하여 아이템 엔티티를 생성하고 기본 컴포넌트를 초기화합니다.
/// - DestroyItemRequest가 활성화된 아이템 엔티티를 파괴합니다.
/// - StateApplyGroup 내에서 구조적 변경(Structural Change)을 한곳에 격리하여 EndStateApplyEntityCommandBufferSystem에 기록합니다.
/// - ISystem (struct) 기반으로 동작하여 불필요한 GC 및 클래스 오버헤드를 배제합니다.
/// </summary>
[UpdateInGroup(typeof(StateApplyGroup))]
public partial struct ItemLifecycleApplySystem : ISystem
{
    private EntityArchetype _fallbackItemArchetype;

    public void OnCreate(ref SystemState state)
    {
        // [임시 Fallback 아키타입]
        // 그래픽 베이커/프리팹 연동 전 단계 및 단위 테스트를 위한 순수 C# 엔티티 구성
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
    }

    public void OnUpdate(ref SystemState state)
    {
        var ecbSystem = state.World.GetExistingSystemManaged<EndStateApplyEntityCommandBufferSystem>();
        var ecb = ecbSystem.CreateCommandBuffer();

        // 1. [생성] SpawnItemRequest 처리
        foreach (var (request, requestEntity) in 
                 SystemAPI.Query<RefRO<SpawnItemRequest>>().WithEntityAccess())
        {
            var req = request.ValueRO;
            Entity newItem = CreateItemEntity(ref ecb, req.ItemType);

            // 초기 컴포넌트 값 세팅
            ecb.SetComponent(newItem, new ItemIdentity(req.ItemType));
            ecb.SetComponent(newItem, new GridPosition(req.Position));
            ecb.SetComponent(newItem, LocalTransform.FromPosition(new float3(req.Position.x, req.Position.y, 0f)));

            if (req.TargetOwner == Entity.Null)
            {
                ecb.SetComponent(newItem, ItemOwnership.WorldItem);
            }
            else
            {
                ecb.SetComponent(newItem, ItemOwnership.Stored(req.TargetOwner));
            }

            // 1회성 Request 컴포넌트들을 비활성화 상태로 초기화
            ecb.SetComponentEnabled<DestroyItemRequest>(newItem, false);
            ecb.SetComponentEnabled<TransferOwnershipRequest>(newItem, false);
            
            // 상태 컴포넌트를 비활성화 상태로 초기화
            ecb.SetComponentEnabled<BeltMovementState>(newItem, false);

            // 의사결정 컴포넌트를 비활성화 상태로 초기화
            ecb.SetComponentEnabled<BeltMovementDecision>(newItem, false);
            ecb.SetComponentEnabled<BuildingItemInputDecision>(newItem, false);

            // Consume-on-Apply: 요청 엔티티 파괴
            ecb.DestroyEntity(requestEntity);
        }

        // 2. [파괴] DestroyItemRequest가 켜진 아이템 처리
        foreach (var (_, entity) in 
                 SystemAPI.Query<RefRO<DestroyItemRequest>>().WithEntityAccess())
        {
            ecb.DestroyEntity(entity);
        }

        // EndStateApplyEntityCommandBufferSystem에서 다른 시스템들의 변경사항과 함께 일괄 Playback됨.
    }

    private Entity CreateItemEntity(ref EntityCommandBuffer ecb, ItemTypeEnum itemType)
    {
        // [1순위: 프리팹 카탈로그 연동 준비]
        // 향후 그래픽/스프라이트 베이커가 도입되면 이곳에서 프리팹 Entity를 복제합니다.
        if (TryGetItemPrefab(itemType, out Entity prefabEntity))
        {
            return ecb.Instantiate(prefabEntity);
        }

        // [2순위: 임시 Fallback 순수 아키타입 생성]
        // 향후 모든 아이템 프리팹이 등록되면 쉽게 제거하거나 경고(Warning)로 전환 가능합니다.
        return CreateFallbackArchetypeEntity(ref ecb);
    }

    private bool TryGetItemPrefab(ItemTypeEnum itemType, out Entity prefabEntity)
    {
        // 현재는 그래픽 프리팹 베이커 연동 전이므로 false 반환 (Fallback 아키타입으로 유도)
        prefabEntity = Entity.Null;
        return false;
    }

    private Entity CreateFallbackArchetypeEntity(ref EntityCommandBuffer ecb)
    {
        return ecb.CreateEntity(_fallbackItemArchetype);
    }
}
