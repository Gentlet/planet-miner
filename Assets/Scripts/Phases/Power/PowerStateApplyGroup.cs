using Unity.Entities;

[UpdateInGroup(typeof(PowerSimulationGroup))]
[UpdateAfter(typeof(PowerExecutionGroup))]
public partial class PowerStateApplyGroup : ComponentSystemGroup
{
}
