using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

[UpdateInGroup(typeof(BuildingDecisionGroup))]
public partial struct ResearchDecisionSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        state.Dependency = new DecideResearchJob
        {
            Demands = SystemAPI.GetComponentLookup<PowerDemand>(true),
            Supplies = SystemAPI.GetComponentLookup<PowerSupplyResult>(true),
            DeltaTime = math.min(SystemAPI.Time.DeltaTime, GameConstants.MaxSimulationDeltaTime)
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile, WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)]
internal partial struct DecideResearchJob : IJobEntity
{
    [ReadOnly] public ComponentLookup<PowerDemand> Demands;
    [ReadOnly] public ComponentLookup<PowerSupplyResult> Supplies;
    public float DeltaTime;

    private void Execute(Entity entity, in ResearchWorkPreparation preparation, EnabledRefRO<ResearchWorkPreparation> prepared,
        ref ResearchDecision decision, EnabledRefRW<ResearchDecision> decided)
    {
        decision = default;
        decided.ValueRW = prepared.ValueRO;
        if (!prepared.ValueRO || preparation.DefinitionIndex < 0)
        {
            return;
        }
        decision.PoweredTime = PowerFrameUtility.GetPoweredDeltaTime(entity, DeltaTime, Demands, Supplies);
        decision.CanStartCycle = preparation.CanStartCycle && decision.PoweredTime > 0f;
        decision.CanAdvance = (preparation.CanAdvance || preparation.CanStartCycle) && decision.PoweredTime > 0f;
    }
}
