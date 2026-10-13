using Unity.Entities;

/// <summary>
/// 이번 틱 연결 집합과 유지할 공개 망의 계획. Reservation이 초기화/작성하고 후속 공급 계획과 StateApply가 읽는다.
/// ExpectedGrid는 최종 적용 시 대조할 이전 공개 값이며 RetainedGrid의 Null은 새 망 생성 계획이다.
/// 예약 망 Entity는 포함하지 않는다. 실제 망 변경은 Apply가 같은 EndBuilding ECB 안에서 기록한다.
/// </summary>
public struct PowerGridTopologyDecision : IComponentData
{
    public Entity RepresentativePole;
    public Entity ExpectedGrid;
    public Entity RetainedGrid;
}
