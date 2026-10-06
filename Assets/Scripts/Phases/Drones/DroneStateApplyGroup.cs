using Unity.Entities;

/// <summary>
/// 드론 계획을 현재 원본으로 재검사해 실물·도착량·예약·배정 상태를 정산한다.
/// Publish가 공개 대기 배정을 마지막으로 검사한다. 구조 변경과 행동 결과는 EndSimulation에서 실체화된다.
/// </summary>
[UpdateInGroup(typeof(DroneSimulationGroup))]
[UpdateAfter(typeof(DroneExecutionGroup))]
public partial class DroneStateApplyGroup : ComponentSystemGroup
{
}

