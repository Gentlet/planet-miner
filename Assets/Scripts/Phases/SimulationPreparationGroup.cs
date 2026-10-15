using Unity.Entities;

/// <summary>Command 확정 뒤 이번 틱 작업 의도와 전력 수요를 한 번 준비한다.</summary>
[UpdateInGroup(typeof(GameSimulationGroup))]
[UpdateAfter(typeof(CommandGroup))]
public partial class SimulationPreparationGroup : ComponentSystemGroup
{
}
