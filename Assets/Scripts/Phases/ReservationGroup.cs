using Unity.Entities;

/// <summary>
/// 역할·목적: Decision 뒤의 세 번째 phase로 같은 틱 후보들의 공유 목적지/수량 경합을 중재한다.
/// 입력·출력: 입고 슬롯·벨트 진입·현장 공급 후보를 읽고 승인/거부 결정과 현장 예약량을 기록한다.
/// 이용·정리: Execution/StateApply가 승인 결과를 사용한다. 임시 슬롯/벨트 집계와 영속 현장 예약의 수명을 구분한다.
/// 드론 공급원 재고/보관 공간 예약이나 일반 벨트 이동 예약은 이 그룹의 현재 구현에 포함하지 않는다.
/// </summary>
[UpdateInGroup(typeof(GameSimulationGroup))]
[UpdateAfter(typeof(DecisionGroup))]
public partial class ReservationGroup : ComponentSystemGroup
{
}
