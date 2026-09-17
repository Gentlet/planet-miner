using Unity.Entities;

/// <summary>
/// Phase 4: 확정된 작업 실행 단계.
/// 이미 결정된 작업(이동 진행, 채굴 진행, 제작 진행 등)을 실제로 진행합니다.
/// 이 단계에서는 무엇을 할지 다시 판단하지 않고, 오직 작업의 진행 및 계산만 수행합니다.
/// </summary>
[UpdateInGroup(typeof(GameSimulationGroup))]
[UpdateAfter(typeof(ReservationGroup))]
public partial class ExecutionGroup : ComponentSystemGroup
{
}
