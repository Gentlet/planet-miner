using Unity.Entities;

/// <summary>
/// 역할·목적: Execution 뒤의 다섯 번째 phase로 실물 인계·생산 결과·수명주기와 영속 상태 전이를 반영한다.
/// 입력·출력: 앞선 결정/계획·생산 결과·요청을 각 소유자가 소비하여 버퍼/Owner·도착량/예약·건물 상태를 갱신한다.
/// 같은 그룹의 순서는 선언된 UpdateBefore/After·OrderLast와 Job 의존성으로만 정하며 파일 나열 순서는 근거가 아니다.
/// 정리·가시화: 값 변경과 요청/결과 소비는 도메인 계약을 따르고 생성/삭제/렌더 태그 구조 변경은 EndStateApply에 확정한다.
/// </summary>
[UpdateInGroup(typeof(GameSimulationGroup))]
[UpdateAfter(typeof(ExecutionGroup))]
public partial class StateApplyGroup : ComponentSystemGroup
{
}
