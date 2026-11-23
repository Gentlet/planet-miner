using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

/// <summary>소비 전력 수요를 초기화하고 채굴/제작/연구의 전력 외 작업 자격과 드론 충전 부족분으로 작성한다. 공급 여부로 작업 자격을 지우지 않는다.</summary>
[UpdateInGroup(typeof(SimulationPreparationGroup))]
[UpdateAfter(typeof(MinerWorkPreparationSystem))]
[UpdateAfter(typeof(CrafterWorkPreparationSystem))]
[UpdateAfter(typeof(ResearchWorkPreparationSystem))]
public partial struct BuildingPowerDemandPreparationSystem : ISystem
{
    private EntityQuery _consumerQuery;

    public void OnCreate(ref SystemState state)
    {
        _consumerQuery = SystemAPI.QueryBuilder().WithAll<PowerConsumer, PowerDemand>()
            .WithAll<BuildingIdentity>().WithNone<PendingBuildingDemolition>()
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
            EntityHandle = SystemAPI.GetEntityTypeHandle(),
            PowerDemandHandle = SystemAPI.GetComponentTypeHandle<PowerDemand>(false),
            PowerConsumerHandle = SystemAPI.GetComponentTypeHandle<PowerConsumer>(false),
            IdentityHandle = SystemAPI.GetComponentTypeHandle<BuildingIdentity>(true),
            Validation = validationOwner.Validation, ConfigEntity = configEntity,
            ConfigLookup = SystemAPI.GetBufferLookup<BuildingConfigElement>(true),
            MinerLookup = SystemAPI.GetComponentLookup<MinerWorkPreparation>(true),
            CrafterLookup = SystemAPI.GetComponentLookup<CrafterWorkPreparation>(true),
            ResearchLookup = SystemAPI.GetComponentLookup<ResearchWorkPreparation>(true),
            ChargingLookup = SystemAPI.GetBufferLookup<DroneChargingPreparationElement>(true),
            DeltaTime = math.min(SystemAPI.Time.DeltaTime, GameConstants.MaxSimulationDeltaTime)
        }.ScheduleParallel(_consumerQuery, dependency);
        state.Dependency = prepare;
        validationOwner.Register(state.Dependency);
    }

}

[BurstCompile]
internal partial struct PrepareBuildingPowerDemandJob : IJobChunk
{
    [ReadOnly] public EntityTypeHandle EntityHandle;
    public ComponentTypeHandle<PowerDemand> PowerDemandHandle;
    public ComponentTypeHandle<PowerConsumer> PowerConsumerHandle;
    [ReadOnly] public ComponentTypeHandle<BuildingIdentity> IdentityHandle;

    private struct RegistryReadContext
    {
        public RegistryBufferReadCache<BuildingConfigElement> ConfigLookup;
    }

    public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
    {
        var entities = chunk.GetNativeArray(EntityHandle);
        var powerDemandValues = chunk.GetNativeArray(ref PowerDemandHandle);
        var powerDemandEnabled = chunk.GetEnabledMask(ref PowerDemandHandle);
        var powerConsumerEnabled = chunk.GetEnabledMask(ref PowerConsumerHandle);
        var buildingTypeValues = chunk.GetNativeArray(ref IdentityHandle);
        var registryRead = new RegistryReadContext();
        var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
        while (enumerator.NextEntityIndex(out int index))
        {
            var demandRef = new RefRW<PowerDemand>(powerDemandValues, index);
            var typeValue = buildingTypeValues[index];
            ExecuteEntity(entities[index],
                ref demandRef.ValueRW,
                powerDemandEnabled.GetEnabledRefRW<PowerDemand>(index),
                powerConsumerEnabled.GetEnabledRefRW<PowerConsumer>(index),
                in typeValue, ref registryRead);
        }
    }

    [ReadOnly] public NativeReference<PowerValidationResult> Validation;
    public Entity ConfigEntity;
    [ReadOnly] public BufferLookup<BuildingConfigElement> ConfigLookup;
    [ReadOnly] public ComponentLookup<MinerWorkPreparation> MinerLookup;
    [ReadOnly] public ComponentLookup<CrafterWorkPreparation> CrafterLookup;
    [ReadOnly] public ComponentLookup<ResearchWorkPreparation> ResearchLookup;
    [ReadOnly] public BufferLookup<DroneChargingPreparationElement> ChargingLookup;
    public float DeltaTime;

    private void ExecuteEntity(Entity entity, ref PowerDemand demand, EnabledRefRW<PowerDemand> prepared, EnabledRefRW<PowerConsumer> intent, in BuildingIdentity type, ref RegistryReadContext registryRead)
    {
        if (Validation.Value.HasError)
        {
            return;
        }

        var configs = registryRead.ConfigLookup.Read(ConfigLookup, ConfigEntity);
        if (!BuildingConfigLookupUtility.TryGetConfig(configs, type.BuildingId, out var config))
        {
            return;
        }
        bool canWork = false;
        float requestedEnergy = 0f;
        if (MinerLookup.HasComponent(entity))
        {
            if (!MinerLookup.IsComponentEnabled(entity))
                return;
            canWork = MinerLookup[entity].CanMine;
        }
        else if (CrafterLookup.HasComponent(entity))
        {
            if (!CrafterLookup.IsComponentEnabled(entity))
                return;
            var preparation = CrafterLookup[entity];
            canWork = preparation.CanStartCraft || preparation.CanAdvance;
        }
        else if (ResearchLookup.HasComponent(entity))
        {
            if (!ResearchLookup.IsComponentEnabled(entity))
                return;
            var preparation = ResearchLookup[entity];
            canWork = preparation.CanStartCycle || preparation.CanAdvance;
        }
        if (ChargingLookup.TryGetBuffer(entity, out var chargingPlan))
        {
            double chargingEnergy = 0d;
            foreach (var charging in chargingPlan)
                chargingEnergy += charging.RequestedEnergy;
            requestedEnergy = PowerFrameUtility.RoundEnergyUp(chargingEnergy);
        }
        demand.RequestedEnergy = canWork ? config.Power.Work.MaximumConsumption * DeltaTime : requestedEnergy;
        intent.ValueRW = demand.RequestedEnergy > 0f;
        prepared.ValueRW = true;
    }
}
