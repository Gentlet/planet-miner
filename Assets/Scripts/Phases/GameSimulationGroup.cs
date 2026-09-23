using Unity.Entities;

/// <summary>
/// Architecture V2 최상위 게임 시뮬레이션 그룹.
/// Unity의 기본 SimulationSystemGroup 내에서 실행되며, 6대 Phase 그룹을 관할.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial class GameSimulationGroup : ComponentSystemGroup
{
}
