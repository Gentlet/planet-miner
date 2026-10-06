using Unity.Entities;

/// <summary>
/// 역할·목적: 완공 전 현장 취소와 이미 도착한 실물 자재의 월드 반환을 요청한다. 완공 건물 철거와 분리한다.
/// 부착 엔티티: TargetSite를 참조하는 별도 일회성 요청 엔티티. 대상 현장에 직접 붙이지 않는다.
/// 생성: 외부 입력이 만드는 계약이며 현재 직접 Producer는 테스트다. 소비 시스템 이후에 실체화되면 다음 Command에서 처리한다.
/// 이용: ConstructionCancelCommandSystem(Command)이 대상과 중복을 검사하고 Cancelled를 표시해 추가 반환을 막는다.
/// 반환: 유효한 현장의 기존 보관 실물만 현장 위치에 반환하며 미도착 자재를 새로 생성하지 않는다. 활성 Destroy 실물은 제외한다.
/// 제거: 처리한 요청은 EndCommand에서 삭제한다. 유효 현장은 실물 반환과 함께 삭제하고 이미 완공/삭제된 대상이면 요청만 소비한다.
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
