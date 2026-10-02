using Unity.Entities;

/// <summary>
/// [1. 역할]         : 공사 현장에 건설 자재 전달 및 수령 요청 (Command / Transient Request)
/// [2. Producer]     : 드론 운송 도착 시스템 (Phase 9), 플레이어/디버그 인터랙션, 테스트 러너
/// [3. Consumer]     : ConstructionLifecycleApplySystem (StateApplyGroup)
/// [4. Create Phase]: ExecutionGroup / StateApplyGroup
/// [5. Consume Phase]: StateApplyGroup (Phase 5)
/// [6. 수명주기]     : 단일 프레임 소비 (Consume-on-Apply). EndStateApplyEntityCommandBufferSystem에 의해 처리 및 요청 엔티티 파괴
/// [7. 결과 정책]    : Delivery의 실물 한 개를 승인하면 이전 보관 버퍼 제거/현장 등록/해당 예약 정산을 함께 적용.
///                    일반 최종 거부는 실물을 보존하고 해당 운송의 예약만 해제. 중복은 추가 효과 없이 소비.
///                    Destroy/철거 반환 충돌은 요청만 거부하고 예약을 보존. 자동 재시도하지 않는다.
/// [8. 결과 수명]    : 요청 삭제와 별개로 Delivery의 Result가 EndStateApply 이후 결과를 보관한다.
///                    ExpectedOwner는 도착 시 보관자이며 등록 당시 SourceBuilding과 다를 수 있다.
/// </summary>
public struct SupplyConstructionMaterialRequest : IComponentData, IRequestComponent
{
    public Entity Delivery;
    public Entity ExpectedOwner;

    public SupplyConstructionMaterialRequest(Entity delivery, Entity expectedOwner)
    {
        Delivery = delivery;
        ExpectedOwner = expectedOwner;
    }
}

/// <summary>
/// 운송 취소 요청. Producer가 생성하고 ConstructionLifecycleApplySystem이 수령보다 먼저 소비한다.
/// 활성 예약만 한 번 해제하며 실물/보관 버퍼는 변경하지 않는다. 이미 수령한 건은 취소하지 않는다.
/// 결과는 해당 Delivery에 EndStateApply 후 남으며 요청은 같은 ECB에서 삭제된다.
/// </summary>
public struct CancelConstructionMaterialDeliveryRequest : IComponentData, IRequestComponent
{
    public Entity Delivery;

    public CancelConstructionMaterialDeliveryRequest(Entity delivery)
    {
        Delivery = delivery;
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
