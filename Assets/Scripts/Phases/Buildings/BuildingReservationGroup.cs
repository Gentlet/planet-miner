using Unity.Entities;

/// <summary>
/// 역할·목적: 건물 Decision 뒤에 입고 슬롯·벨트 진입 목적지의 경합을 중재한다.
/// 입력·출력: 입고/출고/라우팅 후보를 읽고 승인·거부 결정을 기록한다.
/// 이용·정리: 건물 Execution/StateApply가 승인 결과를 사용하며 드론의 현장 공급 예약은 별도 그룹이 담당한다.
/// 드론 공급원 재고/보관 공간 예약이나 일반 벨트 이동 예약은 이 그룹의 현재 구현에 포함하지 않는다.
/// </summary>
[UpdateInGroup(typeof(BuildingSimulationGroup))]
[UpdateAfter(typeof(BuildingDecisionGroup))]
public partial class BuildingReservationGroup : ComponentSystemGroup
{
}
