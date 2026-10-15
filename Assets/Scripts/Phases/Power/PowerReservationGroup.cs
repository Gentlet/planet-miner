using Unity.Entities;

[UpdateInGroup(typeof(PowerSimulationGroup))]
[UpdateAfter(typeof(PowerDecisionGroup))]
public partial class PowerReservationGroup : ComponentSystemGroup
{
}
