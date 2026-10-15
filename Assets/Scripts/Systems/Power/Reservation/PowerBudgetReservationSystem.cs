using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

/// <summary>이번 틱 선택/토폴로지와 준비된 수요/가용량에서 망별 유한 전력 예산을 배정한다.</summary>
[UpdateInGroup(typeof(PowerReservationGroup), OrderLast = true)]
[UpdateAfter(typeof(PowerGridTopologyReservationSystem))]
public partial struct PowerBudgetReservationSystem : ISystem
{
    private EntityQuery _generators;
    private EntityQuery _consumers;

    public void OnCreate(ref SystemState state)
    {
        _generators = SystemAPI.QueryBuilder().WithAll<PowerGenerator>().WithNone<PendingBuildingDemolition>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).Build();
        _consumers = SystemAPI.QueryBuilder().WithAll<PowerConsumer>().WithNone<PendingBuildingDemolition>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).Build();
    }

    public void OnUpdate(ref SystemState state)
    {
        var validationOwner = state.World.GetOrCreateSystemManaged<PowerValidationInitializationSystem>();
        var dependency = validationOwner.GetDependency(state.Dependency);
        Entity configEntity = SystemAPI.TryGetSingletonEntity<BuildingConfig>(out var config) ? config : Entity.Null;
        var generators = _generators.ToEntityListAsync(Allocator.TempJob, dependency, out var generatorsReady);
        var consumers = _consumers.ToEntityListAsync(Allocator.TempJob, dependency, out var consumersReady);
        var ready = new NativeReference<bool>(Allocator.TempJob);

        var validation = new ValidatePowerBudgetsJob
        {
            Generators = generators.AsDeferredJobArray(), Consumers = consumers.AsDeferredJobArray(),
            DisabledLookup = state.GetComponentLookup<Disabled>(true),
            EntityStorageLookup = state.GetEntityStorageInfoLookup(),
            PendingBuildingDemolitionLookup = state.GetComponentLookup<PendingBuildingDemolition>(true),
            PowerDemandLookup = state.GetComponentLookup<PowerDemand>(true),
            PowerGeneratorSupplyPlanLookup = state.GetComponentLookup<PowerGeneratorSupplyPlan>(true),
            PowerGridConnectionDecisionLookup = state.GetComponentLookup<PowerGridConnectionDecision>(true),
            PowerGridTopologyDecisionLookup = state.GetComponentLookup<PowerGridTopologyDecision>(true),
            PowerPoleLookup = state.GetComponentLookup<PowerPole>(true),
            PrefabLookup = state.GetComponentLookup<Prefab>(true),
            Validation = validationOwner.Validation,
            Ready = ready
        }.Schedule(JobHandle.CombineDependencies(JobHandle.CombineDependencies(generatorsReady, consumersReady), state.Dependency));
        var handle = new AllocatePowerBudgetsJob
        {
            Validation = validationOwner.Validation,
            Ready = ready,
            Generators = generators.AsDeferredJobArray(),
            Consumers = consumers.AsDeferredJobArray(),
            ConfigEntity = configEntity,
            ConfigLookup = SystemAPI.GetBufferLookup<BuildingConfigElement>(true),
            TypeLookup = SystemAPI.GetComponentLookup<BuildingType>(true),
            PoleLookup = SystemAPI.GetComponentLookup<PowerPole>(true),
            ConnectionLookup = SystemAPI.GetComponentLookup<PowerGridConnectionDecision>(true),
            TopologyLookup = SystemAPI.GetComponentLookup<PowerGridTopologyDecision>(true),
            DemandLookup = SystemAPI.GetComponentLookup<PowerDemand>(true),
            ConsumerLookup = SystemAPI.GetComponentLookup<PowerConsumer>(true),
            GeneratorLookup = SystemAPI.GetComponentLookup<PowerGenerator>(true),
            FuelLookup = SystemAPI.GetComponentLookup<FuelEnergyState>(true),
            GeneratorPlans = SystemAPI.GetComponentLookup<PowerGeneratorSupplyPlan>(),
            Reservations = SystemAPI.GetComponentLookup<PowerSupplyReservation>(),
            DeltaTime = math.min(SystemAPI.Time.DeltaTime, GameConstants.MaxSimulationDeltaTime)
        }.Schedule(JobHandle.CombineDependencies(validation, state.Dependency));
        state.Dependency = JobHandle.CombineDependencies(
            JobHandle.CombineDependencies(generators.Dispose(handle), consumers.Dispose(handle)), ready.Dispose(handle));
        validationOwner.Register(state.Dependency);
    }

}

// 집계는 이 Job 안의 임시 파생 값이며 별도 ECS 원본이나 공개 망 상태가 아니다.
internal struct PowerGridBudget
{
    public double Requested;
    public double BaseAvailable;
    public double CoalAvailable;
    public double Assigned;
}

[BurstCompile]
internal struct AllocatePowerBudgetsJob : IJob
{
    [ReadOnly] public NativeReference<PowerValidationResult> Validation;
    [ReadOnly] public NativeReference<bool> Ready;
    [ReadOnly] public NativeArray<Entity> Generators;
    [ReadOnly] public NativeArray<Entity> Consumers;
    public Entity ConfigEntity;
    [ReadOnly] public BufferLookup<BuildingConfigElement> ConfigLookup;
    [ReadOnly] public ComponentLookup<BuildingType> TypeLookup;
    [ReadOnly] public ComponentLookup<PowerPole> PoleLookup;
    [ReadOnly] public ComponentLookup<PowerGridConnectionDecision> ConnectionLookup;
    [ReadOnly] public ComponentLookup<PowerGridTopologyDecision> TopologyLookup;
    [ReadOnly] public ComponentLookup<PowerDemand> DemandLookup;
    [ReadOnly] public ComponentLookup<PowerConsumer> ConsumerLookup;
    [ReadOnly] public ComponentLookup<PowerGenerator> GeneratorLookup;
    [ReadOnly] public ComponentLookup<FuelEnergyState> FuelLookup;
    public ComponentLookup<PowerGeneratorSupplyPlan> GeneratorPlans;
    public ComponentLookup<PowerSupplyReservation> Reservations;
    public float DeltaTime;

    public void Execute()
    {
        if (Validation.Value.HasError || !Ready.Value)
        {
            return;
        }

        int capacity = math.max(1, Generators.Length + Consumers.Length);
        var budgets = new NativeParallelHashMap<Entity, PowerGridBudget>(capacity, Allocator.Temp);
        CollectBudgets(budgets);
        using var representatives = budgets.GetKeyArray(Allocator.Temp);
        foreach (Entity representative in representatives)
        {
            var budget = budgets[representative];
            double requestedAllocation = System.Math.Min(budget.Requested, budget.BaseAvailable + budget.CoalAvailable);
            double baseAllocation = System.Math.Min(requestedAllocation, budget.BaseAvailable);
            AllocateGenerators(representative, BuildingTypeEnum.MainFacility, baseAllocation);
            AllocateGenerators(representative, BuildingTypeEnum.CoalGenerator, requestedAllocation - baseAllocation);
        }
        // 발전기 float 예산을 반영한 합계로 공급률을 다시 계산하여 소비자에게 더 큰 예산을 게시하지 않는다.
        foreach (Entity generator in Generators)
        {
            Entity representative = GetRepresentative(generator);
            var plan = GeneratorPlans[generator];
            if (TypeLookup[generator].Type == BuildingTypeEnum.CoalGenerator)
            {
                var type = TypeLookup[generator];
                BuildingConfigLookupUtility.TryGetConfig(ConfigLookup[ConfigEntity], type.Type, type.Tier, out var config);
                PowerFuelUtility.PrepareConsumption(ref plan, FuelLookup[generator].RemainingEnergy, config.Power.CoalEnergyPerItem);
            }
            GeneratorPlans[generator] = plan;
            if (representative == Entity.Null)
            {
                continue;
            }
            var budget = budgets[representative];
            budget.Assigned += GeneratorPlans[generator].AllocatedEnergy;
            budgets[representative] = budget;
        }
        PrepareConsumerReservations(budgets);
        budgets.Dispose();
    }

    private Entity GetRepresentative(Entity entity)
    {
        return PowerFrameUtility.GetRepresentative(entity, PoleLookup, ConnectionLookup, TopologyLookup);
    }

    private void CollectBudgets(NativeParallelHashMap<Entity, PowerGridBudget> budgets)
    {
        foreach (Entity generator in Generators)
        {
            Entity representative = GetRepresentative(generator);
            if (representative == Entity.Null)
            {
                continue;
            }
            budgets.TryGetValue(representative, out var budget);
            float available = GeneratorLookup.IsComponentEnabled(generator) ? GeneratorPlans[generator].AvailableEnergy : 0f;
            if (TypeLookup[generator].Type == BuildingTypeEnum.MainFacility)
            {
                budget.BaseAvailable += available;
            }
            else
            {
                budget.CoalAvailable += available;
            }
            budgets[representative] = budget;
        }
        foreach (Entity consumer in Consumers)
        {
            Entity representative = GetRepresentative(consumer);
            if (representative == Entity.Null)
            {
                continue;
            }
            budgets.TryGetValue(representative, out var budget);
            if (ConsumerLookup.IsComponentEnabled(consumer))
            {
                budget.Requested += DemandLookup[consumer].RequestedEnergy;
            }
            budgets[representative] = budget;
        }
    }

    private double GetRatedEnergy(Entity generator)
    {
        var type = TypeLookup[generator];
        BuildingConfigLookupUtility.TryGetConfig(ConfigLookup[ConfigEntity], type.Type, type.Tier, out var config);
        return config.Power.MaximumGeneration * DeltaTime;
    }

    private void AllocateGenerators(Entity representative, BuildingTypeEnum kind, double required)
    {
        if (required <= 0d)
        {
            return;
        }
        double remainingRated = 0d;
        foreach (Entity generator in Generators)
        {
            if (TypeLookup[generator].Type == kind && GetRepresentative(generator) == representative)
            {
                remainingRated += GetRatedEnergy(generator);
            }
        }
        var limited = new NativeParallelHashSet<Entity>(math.max(1, Generators.Length), Allocator.Temp);
        double remaining = required;
        while (remainingRated > 0d)
        {
            double utilization = System.Math.Min(1d, remaining / remainingRated);
            bool foundLimit = false;
            foreach (Entity generator in Generators)
            {
                if (TypeLookup[generator].Type != kind || GetRepresentative(generator) != representative)
                {
                    continue;
                }
                if (limited.Contains(generator))
                {
                    continue;
                }
                var plan = GeneratorPlans[generator];
                double rated = GetRatedEnergy(generator);
                if (plan.AvailableEnergy < rated * utilization)
                {
                    plan.AllocatedEnergy = plan.AvailableEnergy;
                    GeneratorPlans[generator] = plan;
                    remaining = System.Math.Max(0d, remaining - plan.AvailableEnergy);
                    remainingRated = System.Math.Max(0d, remainingRated - rated);
                    limited.Add(generator);
                    foundLimit = true;
                }
            }
            if (foundLimit)
            {
                continue;
            }
            foreach (Entity generator in Generators)
            {
                if (TypeLookup[generator].Type != kind || GetRepresentative(generator) != representative)
                {
                    continue;
                }
                if (limited.Contains(generator))
                {
                    continue;
                }
                var plan = GeneratorPlans[generator];
                plan.AllocatedEnergy = PowerFrameUtility.RoundEnergyDown(GetRatedEnergy(generator) * utilization,
                    plan.AvailableEnergy);
                GeneratorPlans[generator] = plan;
            }
            break;
        }
        limited.Dispose();
    }

    private void PrepareConsumerReservations(NativeParallelHashMap<Entity, PowerGridBudget> budgets)
    {
        foreach (Entity consumer in Consumers)
        {
            Entity representative = GetRepresentative(consumer);
            var reservation = default(PowerSupplyReservation);
            if (representative != Entity.Null)
            {
                var budget = budgets[representative];
                double ratio = budget.Requested > 0d ? System.Math.Min(1d, budget.Assigned / budget.Requested) : 1d;
                reservation.SupplyRatio = (float)ratio;
                if (reservation.SupplyRatio > 0f)
                {
                    float requested = DemandLookup[consumer].RequestedEnergy;
                    reservation.AllocatedEnergy = PowerFrameUtility.RoundEnergyDown(requested * ratio, requested);
                }
            }
            Reservations[consumer] = reservation;
            Reservations.SetComponentEnabled(consumer, true);
        }
    }
}

[BurstCompile]
internal struct ValidatePowerBudgetsJob : IJob
{
    [ReadOnly] public NativeArray<Entity> Generators;
    [ReadOnly] public NativeArray<Entity> Consumers;
    [ReadOnly] public ComponentLookup<Disabled> DisabledLookup;
    [ReadOnly] public EntityStorageInfoLookup EntityStorageLookup;
    [ReadOnly] public ComponentLookup<PendingBuildingDemolition> PendingBuildingDemolitionLookup;
    [ReadOnly] public ComponentLookup<PowerDemand> PowerDemandLookup;
    [ReadOnly] public ComponentLookup<PowerGeneratorSupplyPlan> PowerGeneratorSupplyPlanLookup;
    [ReadOnly] public ComponentLookup<PowerGridConnectionDecision> PowerGridConnectionDecisionLookup;
    [ReadOnly] public ComponentLookup<PowerGridTopologyDecision> PowerGridTopologyDecisionLookup;
    [ReadOnly] public ComponentLookup<PowerPole> PowerPoleLookup;
    [ReadOnly] public ComponentLookup<Prefab> PrefabLookup;
    public NativeReference<PowerValidationResult> Validation;
    public NativeReference<bool> Ready;
    public void Execute()
    {
        if (Validation.Value.HasError || (Generators.Length == 0 && Consumers.Length == 0))
        {
            return;
        }
        foreach (Entity generator in Generators)
        {
            if (!PowerGeneratorSupplyPlanLookup.IsComponentEnabled(generator))
            {
                return;
            }
            if (!PowerFrameUtility.TryGetRepresentative(
                DisabledLookup, EntityStorageLookup, PendingBuildingDemolitionLookup, PowerGridConnectionDecisionLookup,
                PowerGridTopologyDecisionLookup, PowerPoleLookup, PrefabLookup, generator, out _))
            {
                return;
            }
            float available = PowerGeneratorSupplyPlanLookup[generator].AvailableEnergy;
            if (!math.isfinite(available) || available < 0f)
            {
                PowerValidationUtility.Fail(Validation, "[PowerBudgetReservationSystem] Invalid power frame input.");
                return;
            }
        }
        foreach (Entity consumer in Consumers)
        {
            if (!PowerDemandLookup.IsComponentEnabled(consumer))
            {
                return;
            }
            if (!PowerFrameUtility.TryGetRepresentative(
                DisabledLookup, EntityStorageLookup, PendingBuildingDemolitionLookup, PowerGridConnectionDecisionLookup,
                PowerGridTopologyDecisionLookup, PowerPoleLookup, PrefabLookup, consumer, out _))
            {
                return;
            }
            float requested = PowerDemandLookup[consumer].RequestedEnergy;
            if (!math.isfinite(requested) || requested < 0f)
            {
                PowerValidationUtility.Fail(Validation, "[PowerBudgetReservationSystem] Invalid power frame input.");
                return;
            }
        }
        Ready.Value = true;
    }
}
