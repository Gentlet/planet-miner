using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

/// <summary>소비 도메인의 프레임 초기화와 기존 작업 결정에 따른 수요 준비. 공급 여부로 작업 자격을 지우지 않는다.</summary>
[UpdateInGroup(typeof(SimulationPreparationGroup))]
[UpdateAfter(typeof(MinerWorkPreparationSystem))]
[UpdateAfter(typeof(CrafterWorkPreparationSystem))]
public partial struct BuildingPowerDemandPreparationSystem : ISystem
{
    private EntityQuery _consumerQuery;

    public void OnCreate(ref SystemState state)
    {
        _consumerQuery = SystemAPI.QueryBuilder().WithAll<PowerConsumer, PowerDemand>()
            .WithAll<BuildingType>().WithNone<PendingBuildingDemolition>()
            .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).Build();
    }

    public void OnUpdate(ref SystemState state)
    {
        var validationOwner = state.World.GetOrCreateSystemManaged<PowerValidationInitializationSystem>();
        var dependency = validationOwner.GetDependency(state.Dependency);
        dependency = new ResetPowerDemandJob().ScheduleParallel(dependency);

        Entity configEntity = SystemAPI.TryGetSingletonEntity<BuildingConfig>(out var config) ? config : Entity.Null;
        var prepare = new PrepareBuildingPowerDemandJob
        {
            Validation = validationOwner.Validation, ConfigEntity = configEntity,
            ConfigLookup = SystemAPI.GetBufferLookup<BuildingConfigElement>(true),
            MinerLookup = SystemAPI.GetComponentLookup<MinerWorkPreparation>(true),
            CrafterLookup = SystemAPI.GetComponentLookup<CrafterWorkPreparation>(true),
            DeltaTime = math.min(SystemAPI.Time.DeltaTime, GameConstants.MaxSimulationDeltaTime)
        }.ScheduleParallel(_consumerQuery, dependency);
        state.Dependency = prepare;
        validationOwner.Register(state.Dependency);
    }

}

[BurstCompile]
internal partial struct PrepareBuildingPowerDemandJob : IJobEntity
{
    [ReadOnly] public NativeReference<PowerValidationResult> Validation;
    public Entity ConfigEntity;
    [ReadOnly] public BufferLookup<BuildingConfigElement> ConfigLookup;
    [ReadOnly] public ComponentLookup<MinerWorkPreparation> MinerLookup;
    [ReadOnly] public ComponentLookup<CrafterWorkPreparation> CrafterLookup;
    public float DeltaTime;

    private void Execute(Entity entity, ref PowerDemand demand, EnabledRefRW<PowerDemand> prepared, EnabledRefRW<PowerConsumer> intent, in BuildingType type)
    {
        if (Validation.Value.HasError)
        {
            return;
        }

        var configs = ConfigLookup[ConfigEntity];
        if (!BuildingConfigLookupUtility.TryGetConfig(configs, type.Type, type.Tier, out var config))
        {
            return;
        }
        bool canWork = false;
        switch (type.Type)
        {
            case BuildingTypeEnum.Miner:
                if (!MinerLookup.IsComponentEnabled(entity))
                {
                    return;
                }
                canWork = MinerLookup[entity].CanMine;
                break;
            case BuildingTypeEnum.Crafter:
                if (!CrafterLookup.IsComponentEnabled(entity))
                {
                    return;
                }
                var decision = CrafterLookup[entity];
                canWork = decision.CanStartCraft || decision.CanAdvance;
                break;
        }
        // 연구/충전의 Producer는 후속이다. 고정 수요를 만들지 않고 준비 완료인 0으로 유지한다.
        demand.RequestedEnergy = canWork ? config.Power.MaximumConsumption * DeltaTime : 0f;
        intent.ValueRW = demand.RequestedEnergy > 0f;
        prepared.ValueRW = true;
    }
}
