using Unity.Burst;
using Unity.Burst.Intrinsics;
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
        _generatorQuery = SystemAPI.QueryBuilder().WithAll<PowerGenerator, PowerGeneratorSupplyPlan, BuildingIdentity>()
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
            EntityHandle = SystemAPI.GetEntityTypeHandle(),
            PlanHandle = SystemAPI.GetComponentTypeHandle<PowerGeneratorSupplyPlan>(false),
            GeneratorHandle = SystemAPI.GetComponentTypeHandle<PowerGenerator>(false),
            IdentityHandle = SystemAPI.GetComponentTypeHandle<BuildingIdentity>(true),
            Validation = validationOwner.Validation, ConfigEntity = configEntity,
            ConfigLookup = SystemAPI.GetBufferLookup<BuildingConfigElement>(true),
            FuelLookup = SystemAPI.GetComponentLookup<FuelEnergyState>(true),
            FuelSlots = SystemAPI.GetComponentLookup<FuelStorageSlots>(true),
            StoredLookup = SystemAPI.GetBufferLookup<StorageSlotElement>(true),
            DeltaTime = math.min(SystemAPI.Time.DeltaTime, GameConstants.MaxSimulationDeltaTime)
        }.ScheduleParallel(_generatorQuery, dependency);
        state.Dependency = prepare;
        validationOwner.Register(state.Dependency);
    }

}

[BurstCompile]
internal struct PreparePowerAvailabilityJob : IJobChunk
{
    [ReadOnly] public EntityTypeHandle EntityHandle;
    public ComponentTypeHandle<PowerGeneratorSupplyPlan> PlanHandle;
    public ComponentTypeHandle<PowerGenerator> GeneratorHandle;
    [ReadOnly] public ComponentTypeHandle<BuildingIdentity> IdentityHandle;
    [ReadOnly] public NativeReference<PowerValidationResult> Validation;
    public Entity ConfigEntity;
    [ReadOnly] public BufferLookup<BuildingConfigElement> ConfigLookup;
    [ReadOnly] public ComponentLookup<FuelEnergyState> FuelLookup;
    [ReadOnly] public BufferLookup<StorageSlotElement> StoredLookup;
    [ReadOnly] public ComponentLookup<FuelStorageSlots> FuelSlots;
    public float DeltaTime;

    public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
    {
        var entities = chunk.GetNativeArray(EntityHandle);
        var plans = chunk.GetNativeArray(ref PlanHandle);
        var planEnabled = chunk.GetEnabledMask(ref PlanHandle);
        var generatorEnabled = chunk.GetEnabledMask(ref GeneratorHandle);
        var buildingTypes = chunk.GetNativeArray(ref IdentityHandle);
        RegistryBufferReadCache<BuildingConfigElement> configs = default;
        var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
        while (enumerator.NextEntityIndex(out int index))
        {
            var plan = new RefRW<PowerGeneratorSupplyPlan>(plans, index);
            Prepare(entities[index], ref plan.ValueRW, planEnabled.GetEnabledRefRW<PowerGeneratorSupplyPlan>(index),
                generatorEnabled.GetEnabledRefRW<PowerGenerator>(index), buildingTypes[index], ref configs);
        }
    }

    private void Prepare(Entity entity, ref PowerGeneratorSupplyPlan plan,
        EnabledRefRW<PowerGeneratorSupplyPlan> prepared, EnabledRefRW<PowerGenerator> available, in BuildingIdentity type,
        ref RegistryBufferReadCache<BuildingConfigElement> configs)
    {
        if (Validation.Value.HasError)
        {
            return;
        }

        if (!BuildingConfigLookupUtility.TryGetConfig(configs.Read(ConfigLookup, ConfigEntity), type.BuildingId, out var config))
        {
            return;
        }
        float maximumEnergy = config.Power.Generation.MaximumGeneration * DeltaTime;
        plan.AvailableEnergy = maximumEnergy;
        if (FuelSlots.TryGetComponent(entity, out var range))
        {
            if (!StoredLookup.TryGetBuffer(entity, out var stored) ||
                range.Start < 0 || range.Count <= 0 || range.Count > stored.Length - range.Start)
                return;
            double energy = PowerFuelUtility.CountAvailable(FuelLookup[entity].RemainingEnergy, stored, range, config);
            plan.AvailableEnergy = PowerFrameUtility.RoundEnergyDown(System.Math.Min(maximumEnergy, energy), maximumEnergy);
        }
        available.ValueRW = plan.AvailableEnergy > 0f;
        prepared.ValueRW = true;
    }
}
