using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

/// <summary>소비 도메인의 프레임 초기화와 기존 작업 결정에 따른 수요 준비. 공급 여부로 작업 자격을 지우지 않는다.</summary>
[UpdateInGroup(typeof(BuildingDecisionGroup))]
[UpdateAfter(typeof(MinerDecisionSystem))]
[UpdateAfter(typeof(CrafterDecisionSystem))]
public partial struct BuildingPowerDemandDecisionSystem : ISystem
{
    private EntityQuery _consumerQuery;
    private EntityQuery _activeConsumerQuery;

    public void OnCreate(ref SystemState state)
    {
        _consumerQuery = SystemAPI.QueryBuilder().WithAll<PowerConsumer, PowerDemand, PowerUsageResult>()
            .WithAll<BuildingType>().WithNone<PendingBuildingDemolition>()
            .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).Build();
        _activeConsumerQuery = SystemAPI.QueryBuilder().WithAll<PowerConsumer>()
            .WithNone<PendingBuildingDemolition>().Build();
    }

    public void OnUpdate(ref SystemState state)
    {
        foreach (var (demand, entity) in SystemAPI.Query<RefRW<PowerDemand>>()
                     .WithAll<PowerConsumer>().WithNone<Prefab>().WithEntityAccess()
                     .WithOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IgnoreComponentEnabledState))
        {
            demand.ValueRW = default;
            state.EntityManager.SetComponentEnabled<PowerDemand>(entity, false);
        }
        foreach (var usage in SystemAPI.Query<RefRW<PowerUsageResult>>().WithAll<PowerConsumer>().WithNone<Prefab>()
                     .WithOptions(EntityQueryOptions.IncludeDisabledEntities))
        {
            usage.ValueRW = default;
        }
        if (_activeConsumerQuery.IsEmptyIgnoreFilter)
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
        foreach (var (_, entity) in SystemAPI.Query<RefRO<PowerConsumer>>()
                     .WithNone<PendingBuildingDemolition>().WithEntityAccess())
        {
            if (!PowerFrameUtility.IsConsumerValid(state.EntityManager, entity, configs))
            {
                RecordFailure(ref state);
                return;
            }
        }
        if (_consumerQuery.IsEmptyIgnoreFilter)
        {
            return;
        }
        state.Dependency = new PrepareBuildingPowerDemandJob
        {
            ConfigEntity = configEntity,
            ConfigLookup = SystemAPI.GetBufferLookup<BuildingConfigElement>(true),
            MinerLookup = SystemAPI.GetComponentLookup<MinerDecision>(true),
            CrafterLookup = SystemAPI.GetComponentLookup<CrafterDecision>(true),
            DeltaTime = math.min(SystemAPI.Time.DeltaTime, GameConstants.MaxSimulationDeltaTime)
        }.ScheduleParallel(_consumerQuery, state.Dependency);
    }

    private static void RecordFailure(ref SystemState state)
    {
        PowerFrameUtility.RecordFailure(ref state, "[BuildingPowerDemandDecisionSystem] Invalid consumer input/configuration.");
    }
}

[BurstCompile]
internal partial struct PrepareBuildingPowerDemandJob : IJobEntity
{
    public Entity ConfigEntity;
    [ReadOnly] public BufferLookup<BuildingConfigElement> ConfigLookup;
    [ReadOnly] public ComponentLookup<MinerDecision> MinerLookup;
    [ReadOnly] public ComponentLookup<CrafterDecision> CrafterLookup;
    public float DeltaTime;

    private void Execute(Entity entity, ref PowerDemand demand, EnabledRefRW<PowerDemand> prepared, in BuildingType type)
    {
        var configs = ConfigLookup[ConfigEntity];
        if (!BuildingConfigLookupUtility.TryGetConfig(configs, type.Type, type.Tier, out var config))
        {
            return;
        }
        bool canWork = false;
        switch (type.Type)
        {
            case BuildingTypeEnum.Miner:
                if (MinerLookup.IsComponentEnabled(entity))
                {
                    canWork = MinerLookup[entity].CanMine;
                }
                break;
            case BuildingTypeEnum.Crafter:
                if (CrafterLookup.IsComponentEnabled(entity))
                {
                    var decision = CrafterLookup[entity];
                    canWork = decision.CanStartCraft || decision.CanAdvance;
                }
                break;
        }
        // 연구/충전의 Producer는 후속이다. 고정 수요를 만들지 않고 준비 완료인 0으로 유지한다.
        demand.RequestedEnergy = canWork ? config.Power.MaximumConsumption * DeltaTime : 0f;
        prepared.ValueRW = true;
    }
}
