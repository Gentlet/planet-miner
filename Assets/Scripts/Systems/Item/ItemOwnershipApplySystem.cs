using Unity.Burst;
using Unity.Entities;

/// <summary>
/// 아이템 소유권 상태 적용 시스템 (State Owner).
/// 
/// [책임]
/// - TransferOwnershipRequest를 처리하여 ItemOwnership의 단일 원본(Source of Truth)을 갱신합니다.
/// - 단순 컴포넌트 값 변경만 수행하므로 Structural Change(ECB) 없이 즉시 고속 처리됩니다.
/// - Consume-on-Apply 원칙에 따라 처리 즉시 TransferOwnershipRequest를 비활성화합니다.
/// - ISystem 및 [BurstCompile] 기반으로 완전히 Unmanaged/고성능으로 동작합니다.
/// </summary>
[UpdateInGroup(typeof(StateApplyGroup))]
[BurstCompile]
public partial struct ItemOwnershipApplySystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (request, ownership, entity) in 
                 SystemAPI.Query<RefRO<TransferOwnershipRequest>, RefRW<ItemOwnership>>()
                          .WithEntityAccess())
        {
            Entity targetOwner = request.ValueRO.TargetOwner;

            if (targetOwner == Entity.Null)
            {
                // 월드로 방출 (월드 아이템 전환)
                ownership.ValueRW = ItemOwnership.WorldItem;
            }
            else if (SystemAPI.Exists(targetOwner))
            {
                // 특정 건물/창고 보관 아이템으로 전환
                ownership.ValueRW = ItemOwnership.Stored(targetOwner);
            }
            // 수신자가 유효하지 않은(파괴된) 유령 엔티티인 경우 소유권 변경을 무시하고 Drop

            // Consume-on-Apply: 처리 완료 즉시 비활성화
            SystemAPI.SetComponentEnabled<TransferOwnershipRequest>(entity, false);
        }
    }
}
