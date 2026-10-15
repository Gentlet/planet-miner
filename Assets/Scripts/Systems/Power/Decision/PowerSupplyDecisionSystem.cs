using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

/// <summary>발전 계획 초기화·원본 검증·가용량 준비를 Job으로 연결한다. 실제 소비는 Execution이 소유한다.</summary>
[UpdateInGroup(typeof(PowerDecisionGroup))]
public partial struct PowerSupplyDecisionSystem : ISystem
{
    private EntityQuery _generatorQuery;

    public void OnCreate(ref SystemState state)
    {
        _generatorQuery = SystemAPI.QueryBuilder().WithAll<PowerGenerator, PowerGeneratorSupplyPlan, BuildingType>()
            .WithNone<PendingBuildingDemolition>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).Build();
    }

    public void OnUpdate(ref SystemState state)
    {
        var validationOwner = state.World.GetOrCreateSystemManaged<PowerValidationInitializationSystem>();
        var dependency = validationOwner.GetDependency(state.Dependency);
        dependency = new ResetPowerSupplyReservationJob().ScheduleParallel(dependency);
        dependency = new ResetPowerGeneratorSupplyPlanJob().ScheduleParallel(dependency);

        Entity configEntity = SystemAPI.TryGetSingletonEntity<BuildingConfig>(out var config) ? config : Entity.Null;
        var prepare = new PreparePowerAvailabilityJob
        {
            Validation = validationOwner.Validation, ConfigEntity = configEntity,
            ConfigLookup = SystemAPI.GetBufferLookup<BuildingConfigElement>(true),
            FuelLookup = SystemAPI.GetComponentLookup<FuelEnergyState>(true),
            StoredLookup = SystemAPI.GetBufferLookup<StoredItemElement>(true),
            DeltaTime = math.min(SystemAPI.Time.DeltaTime, GameConstants.MaxSimulationDeltaTime)
        }.ScheduleParallel(_generatorQuery, dependency);
        state.Dependency = prepare;
        validationOwner.Register(state.Dependency);
    }

}

[BurstCompile]
internal partial struct PreparePowerAvailabilityJob : IJobEntity
{
    [ReadOnly] public NativeReference<PowerValidationResult> Validation;
    public Entity ConfigEntity;
    [ReadOnly] public BufferLookup<BuildingConfigElement> ConfigLookup;
    [ReadOnly] public ComponentLookup<FuelEnergyState> FuelLookup;
    [ReadOnly] public BufferLookup<StoredItemElement> StoredLookup;
    public float DeltaTime;

    private void Execute(Entity entity, ref PowerGeneratorSupplyPlan plan,
        EnabledRefRW<PowerGeneratorSupplyPlan> prepared, EnabledRefRW<PowerGenerator> available, in BuildingType type)
    {
        if (Validation.Value.HasError)
        {
            return;
        }

        if (!BuildingConfigLookupUtility.TryGetConfig(ConfigLookup[ConfigEntity], type.Type, type.Tier, out var config))
        {
            return;
        }
        float maximumEnergy = config.Power.MaximumGeneration * DeltaTime;
        plan.AvailableEnergy = type.Type == BuildingTypeEnum.CoalGenerator
            ? PowerFrameUtility.RoundEnergyDown(System.Math.Min(maximumEnergy,
                (double)FuelLookup[entity].RemainingEnergy + StoredLookup[entity].Length * (double)config.Power.CoalEnergyPerItem), maximumEnergy)
            : maximumEnergy;
        available.ValueRW = plan.AvailableEnergy > 0f;
        prepared.ValueRW = true;
    }
}
