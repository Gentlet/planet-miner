using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// [1. 역할]         : 공사 현장에 건설 자재 전달 및 수령 요청 (Command / Transient Request)
/// [2. Producer]     : 드론 운송 도착 시스템 (Phase 9), 플레이어/디버그 인터랙션, 테스트 러너
/// [3. Consumer]     : ConstructionLifecycleApplySystem (StateApplyGroup)
/// [4. Create Phase]: ExecutionGroup / StateApplyGroup
/// [5. Consume Phase]: StateApplyGroup (Phase 5)
/// [6. 수명주기]     : 단일 프레임 소비 (Consume-on-Apply). EndStateApplyEntityCommandBufferSystem에 의해 처리 및 요청 엔티티 파괴
/// [7. 결과 정책]    : 유효한 요구 자재일 경우 수령(DeliveredQuantity 증가, StoredItemElement 소유권 이전). 초과/오품/무효 현장 시 Strict Rejection (수령 거부, 아이템 소유권 유지, 요청 엔티티 소비)
///                    Destroy 대상 또는 입출고 후 철거 건물 버퍼에 남은 실물은 수령 전 거부. 기존 거부처럼 예약량은 보존하며 자동 재시도하지 않는다.
/// [8. 안전망]       : 요청 처리 후 즉시 파괴되어 고아 요청 누수 방지
/// </summary>
public struct SupplyConstructionMaterialRequest : IComponentData, IRequestComponent
{
    /// <summary>
    /// 자재를 수령할 대상 공사 현장 엔티티.
    /// </summary>
    public Entity TargetSite;

    /// <summary>
    /// 전달할 자재 아이템 엔티티.
    /// </summary>
    public Entity ItemEntity;

    /// <summary>
    /// 전달할 자재 아이템의 종류.
    /// </summary>
    public ItemTypeEnum ItemType;

    public SupplyConstructionMaterialRequest(Entity targetSite, Entity itemEntity, ItemTypeEnum itemType)
    {
        TargetSite = targetSite;
        ItemEntity = itemEntity;
        ItemType = itemType;
    }
}

/// <summary>
/// [1. 역할]         : 진행 중인 공사 현장 취소 및 자재 반환 요청 (Command / Transient Request)
/// [2. Producer]     : 플레이어/UI 철거 및 취소 액션, 디버그 인터랙션, 테스트 러너
/// [3. Consumer]     : ConstructionLifecycleApplySystem (StateApplyGroup)
/// [4. Create Phase]: CommandGroup / ExecutionGroup / StateApplyGroup
/// [5. Consume Phase]: StateApplyGroup (Phase 5)
/// [6. 수명주기]     : 단일 프레임 소비 (Consume-on-Apply). EndStateApplyEntityCommandBufferSystem에 의해 처리 및 요청 엔티티 파괴
/// [7. 결과 정책]    : 유효한 현장일 경우 취소 플래그 마킹, 도착 자재 월드 아이템 방출, 현장 엔티티 파괴. 이미 파괴/완공된 대상일 경우 Strict Rejection / Idempotent Drop (요청만 소비)
/// [8. 안전망]       : 요청 처리 후 즉시 파괴되어 고아 요청 누수 방지
/// </summary>
public struct CancelConstructionRequest : IComponentData, IRequestComponent
{
    /// <summary>
    /// 취소할 대상 공사 현장 엔티티.
    /// </summary>
    public Entity TargetSite;

    public CancelConstructionRequest(Entity targetSite)
    {
        TargetSite = targetSite;
    }
}
