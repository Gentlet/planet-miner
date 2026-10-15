using Unity.Entities;

/// <summary>
/// 이번 틱 연결 집합과 유지할 공개 망의 계획. Reservation이 초기화/작성하고 후속 공급 계획과 StateApply가 읽는다.
/// RetainedGrid의 Null은 새 망 생성 계획이다. 공개 연결은 EndPower까지 변경하지 않는다.
/// 예약 망 Entity는 포함하지 않는다. 실제 망 변경은 Apply가 같은 EndPower ECB 안에서 기록한다.
/// </summary>
public struct PowerGridTopologyDecision : IComponentData
{
    public Entity RepresentativePole;
    public Entity RetainedGrid;
}
