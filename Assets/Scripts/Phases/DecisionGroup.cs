using Unity.Entities;

/// <summary>
/// Phase 2: 도메인별 게임 규칙 및 의사결정 판단 단계.
/// 각 도메인 시스템이 자신의 규칙만 독립적으로 판단합니다 (이동 가능 여부, 생산 진행 가능 여부 등).
/// 다른 도메인의 내부 상태 직접 변경 금지.
/// </summary>
[UpdateInGroup(typeof(GameSimulationGroup))]
[UpdateAfter(typeof(CommandGroup))]
public partial class DecisionGroup : ComponentSystemGroup
{
}
