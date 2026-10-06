using Unity.Entities;

/// <summary>
/// 역할·목적: 건물 Reservation 뒤에 승인된 벨트 이동·채굴·제작을 한 번 실행한다.
/// 입력·출력: 승인 결정으로 진행도·위치·재료 선소비·생산 결과를 해당 소유자가 기록한다.
/// 이용·정리·가시화: 건물 StateApply가 결과를 반영하고 구조 변경은 EndBuilding에서 확정한다.
/// </summary>
[UpdateInGroup(typeof(BuildingSimulationGroup))]
[UpdateAfter(typeof(BuildingReservationGroup))]
public partial class BuildingExecutionGroup : ComponentSystemGroup
{
}
