using Unity.Entities;

/// <summary>
/// 역할·목적: 건물 Execution 뒤에 일반 물류·생산 결과·아이템/건물 수명주기를 반영하고 마지막에 완공을 판단한다.
/// 입력·출력: 결정·생산 결과·요청을 각 소유자가 소비하여 버퍼·Owner·건물 상태를 갱신한다.
/// 같은 그룹의 순서는 선언된 UpdateBefore/After·OrderLast와 Job 의존성으로만 정하며 파일 나열 순서는 근거가 아니다.
/// 정리·가시화: 지난 틱 드론 도착량·바닥 정리로 완공을 판단한다. 생성·삭제·렌더 변경은 EndBuilding에서 확정한다.
/// </summary>
[UpdateInGroup(typeof(BuildingSimulationGroup))]
[UpdateAfter(typeof(BuildingExecutionGroup))]
public partial class BuildingStateApplyGroup : ComponentSystemGroup
{
}
