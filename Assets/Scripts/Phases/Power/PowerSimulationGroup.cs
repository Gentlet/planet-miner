using Unity.Entities;

/// <summary>Preparation 이후 전력 단계를 실행하고 Building 이전에 종료한다.</summary>
[UpdateInGroup(typeof(GameSimulationGroup))]
[UpdateAfter(typeof(SimulationPreparationGroup))]
public partial class PowerSimulationGroup : ComponentSystemGroup
{
}
