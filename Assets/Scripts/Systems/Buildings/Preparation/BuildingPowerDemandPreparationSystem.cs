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
            ChargingLookup = SystemAPI.GetBufferLookup<DroneChargingPreparationElement>(true),
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
    [ReadOnly] public BufferLookup<DroneChargingPreparationElement> ChargingLookup;
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
        float requestedEnergy = 0f;
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
            case BuildingTypeEnum.DroneStation:
            case BuildingTypeEnum.MainFacility:
                double chargingEnergy = 0d;
                foreach (var charging in ChargingLookup[entity]) chargingEnergy += charging.RequestedEnergy;
                requestedEnergy = PowerFrameUtility.RoundEnergyUp(chargingEnergy);
                break;
        }
        // 연구 수요는 후속이다. 충전은 준비된 부족분만 요청하며 대기 중 고정 소비를 만들지 않는다.
        demand.RequestedEnergy = canWork ? config.Power.MaximumConsumption * DeltaTime : requestedEnergy;
        intent.ValueRW = demand.RequestedEnergy > 0f;
        prepared.ValueRW = true;
    }
}
