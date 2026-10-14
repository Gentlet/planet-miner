using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

/// <summary>전력 소유자의 프레임 초기화와 가용 발전량 판단. 미전환 석탄 조회/전환은 8.6이다.</summary>
[UpdateInGroup(typeof(BuildingDecisionGroup))]
public partial struct PowerSupplyDecisionSystem : ISystem
{
    private EntityQuery _generatorQuery;
    private EntityQuery _activeGeneratorQuery;

    public void OnCreate(ref SystemState state)
    {
        _generatorQuery = SystemAPI.QueryBuilder().WithAll<PowerGenerator, PowerGeneratorSupplyPlan, BuildingType>()
            .WithNone<PendingBuildingDemolition>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).Build();
        _activeGeneratorQuery = SystemAPI.QueryBuilder().WithAll<PowerGenerator>()
            .WithNone<PendingBuildingDemolition>().Build();
    }

    public void OnUpdate(ref SystemState state)
    {
        foreach (var result in SystemAPI.Query<RefRW<PowerSupplyResult>>()
                     .WithAll<PowerConsumer>().WithNone<Prefab>()
                     .WithOptions(EntityQueryOptions.IncludeDisabledEntities))
        {
            result.ValueRW = default;
        }
        foreach (var (reservation, entity) in SystemAPI.Query<RefRW<PowerSupplyReservation>>()
                     .WithAll<PowerConsumer>().WithNone<Prefab>().WithEntityAccess()
                     .WithOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IgnoreComponentEnabledState))
        {
            reservation.ValueRW = default;
            state.EntityManager.SetComponentEnabled<PowerSupplyReservation>(entity, false);
        }
        foreach (var (plan, entity) in SystemAPI.Query<RefRW<PowerGeneratorSupplyPlan>>()
                     .WithAll<PowerGenerator>().WithNone<Prefab>().WithEntityAccess()
                     .WithOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IgnoreComponentEnabledState))
        {
            plan.ValueRW = default;
            state.EntityManager.SetComponentEnabled<PowerGeneratorSupplyPlan>(entity, false);
        }
        if (_activeGeneratorQuery.IsEmptyIgnoreFilter)
        {
            return;
        }
        if (!SystemAPI.TryGetSingletonEntity<BuildingConfig>(out var configEntity))
        {
            RecordFailure(ref state);
            return;
        }
        if (!state.EntityManager.HasBuffer<BuildingConfigElement>(configEntity))
        {
            RecordFailure(ref state);
            return;
        }
        var configs = state.EntityManager.GetBuffer<BuildingConfigElement>(configEntity, true);
        foreach (var (_, entity) in SystemAPI.Query<RefRO<PowerGenerator>>()
                     .WithNone<PendingBuildingDemolition>().WithEntityAccess())
        {
            if (!PowerFrameUtility.IsGeneratorValid(state.EntityManager, entity, configs))
            {
                RecordFailure(ref state);
                return;
            }
        }
        if (_generatorQuery.IsEmptyIgnoreFilter)
        {
            return;
        }
        state.Dependency = new PreparePowerAvailabilityJob
        {
            ConfigEntity = configEntity,
            ConfigLookup = SystemAPI.GetBufferLookup<BuildingConfigElement>(true),
            FuelLookup = SystemAPI.GetComponentLookup<FuelEnergyState>(true),
            DeltaTime = math.min(SystemAPI.Time.DeltaTime, GameConstants.MaxSimulationDeltaTime)
        }.ScheduleParallel(_generatorQuery, state.Dependency);
    }

    private static void RecordFailure(ref SystemState state)
    {
        PowerFrameUtility.RecordFailure(ref state, "[PowerSupplyDecisionSystem] Invalid generator input/configuration.");
    }
}

[BurstCompile]
internal partial struct PreparePowerAvailabilityJob : IJobEntity
{
    public Entity ConfigEntity;
    [ReadOnly] public BufferLookup<BuildingConfigElement> ConfigLookup;
    [ReadOnly] public ComponentLookup<FuelEnergyState> FuelLookup;
    public float DeltaTime;

    private void Execute(Entity entity, ref PowerGeneratorSupplyPlan plan,
        EnabledRefRW<PowerGeneratorSupplyPlan> prepared, in BuildingType type)
    {
        if (!BuildingConfigLookupUtility.TryGetConfig(ConfigLookup[ConfigEntity], type.Type, type.Tier, out var config))
        {
            return;
        }
        float maximumEnergy = config.Power.MaximumGeneration * DeltaTime;
        plan.AvailableEnergy = type.Type == BuildingTypeEnum.CoalGenerator
            ? math.min(maximumEnergy, FuelLookup[entity].RemainingEnergy)
            : maximumEnergy;
        prepared.ValueRW = true;
    }
}
