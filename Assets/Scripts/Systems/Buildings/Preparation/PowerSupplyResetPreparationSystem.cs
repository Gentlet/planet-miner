using Unity.Burst;
using Unity.Entities;

/// <summary>틱 시작의 공급/계획/준비 초기화. Native 검증 scratch의 수명은 PowerValidationInitializationSystem이 소유한다.</summary>
[UpdateInGroup(typeof(SimulationPreparationGroup), OrderFirst = true)]
public partial struct PowerSupplyResetPreparationSystem : ISystem
{
    public void OnCreate(ref SystemState state) { }
    public void OnUpdate(ref SystemState state)
    {
        var validationOwner = state.World.GetOrCreateSystemManaged<PowerValidationInitializationSystem>();
        validationOwner.PrepareFrame();
        var dependency = validationOwner.GetDependency(state.Dependency);
        dependency = new ResetPowerSupplyResultJob().ScheduleParallel(dependency);
        state.Dependency = dependency;
        validationOwner.Register(dependency);
    }
}

[BurstCompile, WithOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IgnoreComponentEnabledState)]
internal partial struct ResetPowerSupplyResultJob : IJobEntity
{
    private void Execute(ref PowerSupplyResult result)
    {
        result = default;
    }
}

[BurstCompile, WithOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IgnoreComponentEnabledState)]
internal partial struct ResetPowerDemandJob : IJobEntity
{
    private void Execute(ref PowerDemand demand, EnabledRefRW<PowerDemand> prepared, EnabledRefRW<PowerConsumer> intent)
    {
        demand = default;
        prepared.ValueRW = false;
        intent.ValueRW = false;
    }
}

[BurstCompile, WithOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IgnoreComponentEnabledState)]
internal partial struct ResetPowerSupplyReservationJob : IJobEntity
{
    private void Execute(ref PowerSupplyReservation reservation, EnabledRefRW<PowerSupplyReservation> prepared)
    {
        reservation = default;
        prepared.ValueRW = false;
    }
}

[BurstCompile, WithOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IgnoreComponentEnabledState)]
internal partial struct ResetPowerGeneratorSupplyPlanJob : IJobEntity
{
    private void Execute(ref PowerGeneratorSupplyPlan plan, EnabledRefRW<PowerGeneratorSupplyPlan> prepared, EnabledRefRW<PowerGenerator> available)
    {
        plan = default;
        prepared.ValueRW = false;
        available.ValueRW = false;
    }
}

[BurstCompile, WithOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IgnoreComponentEnabledState)]
internal partial struct ResetPowerGridConnectionDecisionJob : IJobEntity
{
    private void Execute(ref PowerGridConnectionDecision decision, EnabledRefRW<PowerGridConnectionDecision> prepared)
    {
        decision = default;
        prepared.ValueRW = false;
    }
}

[BurstCompile, WithOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IgnoreComponentEnabledState)]
internal partial struct ResetPowerGridTopologyDecisionJob : IJobEntity
{
    private void Execute(ref PowerGridTopologyDecision decision)
    {
        decision = default;
    }
}

[BurstCompile, WithOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IgnoreComponentEnabledState)]
internal partial struct ResetMinerWorkPreparationJob : IJobEntity
{
    private void Execute(ref MinerWorkPreparation preparation, EnabledRefRW<MinerWorkPreparation> prepared)
    {
        preparation = default;
        prepared.ValueRW = false;
    }
}

[BurstCompile, WithOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IgnoreComponentEnabledState)]
internal partial struct ResetCrafterWorkPreparationJob : IJobEntity
{
    private void Execute(ref CrafterWorkPreparation preparation, EnabledRefRW<CrafterWorkPreparation> prepared)
    {
        preparation = default;
        prepared.ValueRW = false;
    }
}
