using Unity.Entities;

[UpdateInGroup(typeof(PowerSimulationGroup))]
[UpdateAfter(typeof(PowerReservationGroup))]
public partial class PowerExecutionGroup : ComponentSystemGroup
{
}
