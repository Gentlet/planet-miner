using Unity.Entities;

/// <summary>
/// 드론 작업·경로·배정·행동 결과와 종료 정리의 구조 변경을 최종 확정한다.
/// 완공은 앞선 건물 그룹에서 처리한다. 공간 인덱스 재구축은 이 그룹 종료 뒤에 수행한다.
/// </summary>
[UpdateInGroup(typeof(GameSimulationGroup))]
[UpdateAfter(typeof(DroneSimulationGroup))]
public partial class SimulationCommitGroup : ComponentSystemGroup
{
}

