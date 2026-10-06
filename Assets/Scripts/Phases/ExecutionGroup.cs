using Unity.Entities;

/// <summary>
/// 역할·목적: Reservation 뒤의 네 번째 phase로 결정된 이동·생산을 진행하고 명시된 작업/경로 의도를 실행한다.
/// 입력·출력: 승인 결정을 읽어 진행도/위치·재료 선소비·생산 결과와 드론 인계 계획을 해당 소유자가 작성한다.
/// 적용 검사는 수행하지만 작업 필요 여부를 새로 탐색하지 않는다. 드론 인계 계획은 물류 실행 전 OrderFirst에서 준비한다.
/// 이용·정리·가시화: StateApply가 결과를 반영하며 실물/작업 구조 변경은 EndStateApply에서 재생한다. 실제 드론 이동은 아직 후속이다.
/// </summary>
[UpdateInGroup(typeof(GameSimulationGroup))]
[UpdateAfter(typeof(ReservationGroup))]
public partial class ExecutionGroup : ComponentSystemGroup
{
}
