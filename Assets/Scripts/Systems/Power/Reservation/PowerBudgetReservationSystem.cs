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
        var generatorInputs = new NativeList<PowerGeneratorBudgetInput>(Allocator.TempJob);
        var consumerInputs = new NativeList<PowerConsumerBudgetInput>(Allocator.TempJob);
        var demands = SystemAPI.GetComponentLookup<PowerDemand>(true);

        var validation = new ValidatePowerBudgetsJob
        {
            Generators = generators.AsDeferredJobArray(), Consumers = consumers.AsDeferredJobArray(),
            DisabledLookup = SystemAPI.GetComponentLookup<Disabled>(true),
            PendingBuildingDemolitionLookup = SystemAPI.GetComponentLookup<PendingBuildingDemolition>(true),
            PowerDemandLookup = demands,
            PowerGeneratorSupplyPlanLookup = SystemAPI.GetComponentLookup<PowerGeneratorSupplyPlan>(true),
            PowerGridConnectionDecisionLookup = SystemAPI.GetComponentLookup<PowerGridConnectionDecision>(true),
            PowerGridTopologyDecisionLookup = SystemAPI.GetComponentLookup<PowerGridTopologyDecision>(true),
            PowerPoleLookup = SystemAPI.GetComponentLookup<PowerPole>(true),
            PrefabLookup = SystemAPI.GetComponentLookup<Prefab>(true),
            Validation = validationOwner.Validation,
            GeneratorInputs = generatorInputs, ConsumerInputs = consumerInputs,
            Ready = ready
        }.Schedule(JobHandle.CombineDependencies(JobHandle.CombineDependencies(generatorsReady, consumersReady), state.Dependency));
        var handle = new AllocatePowerBudgetsJob
        {
            Validation = validationOwner.Validation,
            GeneratorInputs = generatorInputs, ConsumerInputs = consumerInputs,
            Ready = ready,
            Generators = generators.AsDeferredJobArray(),
            Consumers = consumers.AsDeferredJobArray(),
            ConfigEntity = configEntity,
            ConfigLookup = SystemAPI.GetBufferLookup<BuildingConfigElement>(true),
            Identities = SystemAPI.GetComponentLookup<BuildingIdentity>(true),
            ConsumerLookup = SystemAPI.GetComponentLookup<PowerConsumer>(true),
            GeneratorLookup = SystemAPI.GetComponentLookup<PowerGenerator>(true),
            FuelLookup = SystemAPI.GetComponentLookup<FuelEnergyState>(true),
            FuelSlots = SystemAPI.GetComponentLookup<FuelStorageSlots>(true),
            Stored = SystemAPI.GetBufferLookup<StorageSlotElement>(true),
            FuelSpends = SystemAPI.GetBufferLookup<PowerFuelSpend>(),
            GeneratorPlans = SystemAPI.GetComponentLookup<PowerGeneratorSupplyPlan>(),
            Reservations = SystemAPI.GetComponentLookup<PowerSupplyReservation>(),
            DeltaTime = math.min(SystemAPI.Time.DeltaTime, GameConstants.MaxSimulationDeltaTime)
        }.Schedule(JobHandle.CombineDependencies(validation, state.Dependency));
        state.Dependency = JobHandle.CombineDependencies(
            JobHandle.CombineDependencies(generators.Dispose(handle), consumers.Dispose(handle), ready.Dispose(handle)),
            JobHandle.CombineDependencies(generatorInputs.Dispose(handle), consumerInputs.Dispose(handle)));
        validationOwner.Register(state.Dependency);
    }

}

// 집계는 이 Job 안의 임시 파생 값이며 별도 ECS 원본이나 공개 망 상태가 아니다.
internal struct PowerGridBudget
{
    public double Requested;
    public double BaseAvailable;
    public double FuelAvailable;
    public double Assigned;
}

internal struct PowerGeneratorBudgetInput
{
    public Entity Representative;
    public bool PowerSettingsRead;
    public double RatedEnergy;
}

internal struct PowerConsumerBudgetInput
{
    public Entity Representative;
    public float RequestedEnergy;
}

[BurstCompile]
internal struct AllocatePowerBudgetsJob : IJob
{
    [ReadOnly] public NativeReference<PowerValidationResult> Validation;
    [ReadOnly] public NativeReference<bool> Ready;
    [ReadOnly] public NativeArray<Entity> Generators;
    [ReadOnly] public NativeArray<Entity> Consumers;
    public NativeList<PowerGeneratorBudgetInput> GeneratorInputs;
    [ReadOnly] public NativeList<PowerConsumerBudgetInput> ConsumerInputs;
    public Entity ConfigEntity;
    [ReadOnly] public BufferLookup<BuildingConfigElement> ConfigLookup;
    [ReadOnly] public ComponentLookup<BuildingIdentity> Identities;
    [ReadOnly] public ComponentLookup<PowerConsumer> ConsumerLookup;
    [ReadOnly] public ComponentLookup<PowerGenerator> GeneratorLookup;
    [ReadOnly] public ComponentLookup<FuelEnergyState> FuelLookup;
    [ReadOnly] public ComponentLookup<FuelStorageSlots> FuelSlots;
    [ReadOnly] public BufferLookup<StorageSlotElement> Stored;
    public BufferLookup<PowerFuelSpend> FuelSpends;
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
        DynamicBuffer<BuildingConfigElement> configs = default;
        using var representatives = budgets.GetKeyArray(Allocator.Temp);
        foreach (Entity representative in representatives)
        {
            var budget = budgets[representative];
            double requestedAllocation = System.Math.Min(budget.Requested, budget.BaseAvailable + budget.FuelAvailable);
            double baseAllocation = System.Math.Min(requestedAllocation, budget.BaseAvailable);
            AllocateGenerators(representative, usesFuel: false, baseAllocation, ref configs);
            AllocateGenerators(representative, usesFuel: true, requestedAllocation - baseAllocation, ref configs);
        }
        // 발전기 float 예산을 반영한 합계로 공급률을 다시 계산하여 소비자에게 더 큰 예산을 게시하지 않는다.
        for (int i = 0; i < Generators.Length; i++)
        {
            Entity generator = Generators[i];
            Entity representative = GeneratorInputs[i].Representative;
            var plan = GeneratorPlans[generator];
            if (FuelSlots.TryGetComponent(generator, out var fuelSlots) && FuelSpends.TryGetBuffer(generator, out var spends))
            {
                if (!configs.IsCreated)
                    configs = ConfigLookup[ConfigEntity];
                spends.Clear();
                plan.ExpectedFuelEnergy = FuelLookup[generator].RemainingEnergy;
                if (BuildingConfigLookupUtility.TryGetConfig(configs, Identities[generator].BuildingId, out var config) &&
                    PowerFuelUtility.TryPlan(plan.ExpectedFuelEnergy, plan.AllocatedEnergy, Stored[generator],
                        fuelSlots, config, out var planned, out plan.RemainingEnergyAfterConsumption, out plan.ConvertedFuelCapacity))
                {
                    foreach (var spend in planned)
                        spends.Add(spend);
                }
            }
            GeneratorPlans[generator] = plan;
            if (representative == Entity.Null)
            {
                continue;
            }
            var budget = budgets[representative];
            budget.Assigned += plan.AllocatedEnergy;
            budgets[representative] = budget;
        }
        PrepareConsumerReservations(budgets);
        budgets.Dispose();
    }

    private void CollectBudgets(NativeParallelHashMap<Entity, PowerGridBudget> budgets)
    {
        for (int i = 0; i < Generators.Length; i++)
        {
            Entity generator = Generators[i];
            Entity representative = GeneratorInputs[i].Representative;
            if (representative == Entity.Null)
            {
                continue;
            }
            budgets.TryGetValue(representative, out var budget);
            float available = GeneratorLookup.IsComponentEnabled(generator) ? GeneratorPlans[generator].AvailableEnergy : 0f;
            if (!FuelSlots.HasComponent(generator))
            {
                budget.BaseAvailable += available;
            }
            else
            {
                budget.FuelAvailable += available;
            }
            budgets[representative] = budget;
        }
        for (int i = 0; i < Consumers.Length; i++)
        {
            Entity consumer = Consumers[i];
            var input = ConsumerInputs[i];
            Entity representative = input.Representative;
            if (representative == Entity.Null)
            {
                continue;
            }
            budgets.TryGetValue(representative, out var budget);
            if (ConsumerLookup.IsComponentEnabled(consumer))
            {
                budget.Requested += input.RequestedEnergy;
            }
            budgets[representative] = budget;
        }
    }

    private PowerGeneratorBudgetInput GetGeneratorPowerSettings(int index,
        ref DynamicBuffer<BuildingConfigElement> configs)
    {
        var input = GeneratorInputs[index];
        if (input.PowerSettingsRead)
        {
            return input;
        }
        if (!configs.IsCreated)
        {
            configs = ConfigLookup[ConfigEntity];
        }
        BuildingConfigLookupUtility.TryGetConfig(configs, Identities[Generators[index]].BuildingId, out var config);
        // 기존 에너지 계산과 같이 float 곱셈을 마친 뒤 합산용 double로 보관한다.
        float ratedEnergy = config.Power.Generation.MaximumGeneration * DeltaTime;
        input.RatedEnergy = ratedEnergy;
        input.PowerSettingsRead = true;
        GeneratorInputs[index] = input;
        return input;
    }

    private void AllocateGenerators(Entity representative, bool usesFuel, double required,
        ref DynamicBuffer<BuildingConfigElement> configs)
    {
        if (required <= 0d)
        {
            return;
        }
        double remainingRated = 0d;
        for (int i = 0; i < Generators.Length; i++)
        {
            if (FuelSlots.HasComponent(Generators[i]) == usesFuel && GeneratorInputs[i].Representative == representative)
            {
                remainingRated += GetGeneratorPowerSettings(i, ref configs).RatedEnergy;
            }
        }
        var limited = new NativeParallelHashSet<Entity>(math.max(1, Generators.Length), Allocator.Temp);
        double remaining = required;
        while (remainingRated > 0d)
        {
            double utilization = System.Math.Min(1d, remaining / remainingRated);
            bool foundLimit = false;
            for (int i = 0; i < Generators.Length; i++)
            {
                Entity generator = Generators[i];
                if (FuelSlots.HasComponent(generator) != usesFuel || GeneratorInputs[i].Representative != representative)
                {
                    continue;
                }
                if (limited.Contains(generator))
                {
                    continue;
                }
                var plan = GeneratorPlans[generator];
                double rated = GetGeneratorPowerSettings(i, ref configs).RatedEnergy;
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
            for (int i = 0; i < Generators.Length; i++)
            {
                Entity generator = Generators[i];
                if (FuelSlots.HasComponent(generator) != usesFuel || GeneratorInputs[i].Representative != representative)
                {
                    continue;
                }
                if (limited.Contains(generator))
                {
                    continue;
                }
                var plan = GeneratorPlans[generator];
                plan.AllocatedEnergy = PowerFrameUtility.RoundEnergyDown(GetGeneratorPowerSettings(i, ref configs).RatedEnergy * utilization,
                    plan.AvailableEnergy);
                GeneratorPlans[generator] = plan;
            }
            break;
        }
        limited.Dispose();
    }

    private void PrepareConsumerReservations(NativeParallelHashMap<Entity, PowerGridBudget> budgets)
    {
        for (int i = 0; i < Consumers.Length; i++)
        {
            Entity consumer = Consumers[i];
            var input = ConsumerInputs[i];
            Entity representative = input.Representative;
            var reservation = default(PowerSupplyReservation);
            if (representative != Entity.Null)
            {
                var budget = budgets[representative];
                double ratio = budget.Requested > 0d ? System.Math.Min(1d, budget.Assigned / budget.Requested) : 1d;
                reservation.SupplyRatio = (float)ratio;
                if (reservation.SupplyRatio > 0f)
                {
                    float requested = input.RequestedEnergy;
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
    [ReadOnly] public ComponentLookup<PendingBuildingDemolition> PendingBuildingDemolitionLookup;
    [ReadOnly] public ComponentLookup<PowerDemand> PowerDemandLookup;
    [ReadOnly] public ComponentLookup<PowerGeneratorSupplyPlan> PowerGeneratorSupplyPlanLookup;
    [ReadOnly] public ComponentLookup<PowerGridConnectionDecision> PowerGridConnectionDecisionLookup;
    [ReadOnly] public ComponentLookup<PowerGridTopologyDecision> PowerGridTopologyDecisionLookup;
    [ReadOnly] public ComponentLookup<PowerPole> PowerPoleLookup;
    [ReadOnly] public ComponentLookup<Prefab> PrefabLookup;
    public NativeReference<PowerValidationResult> Validation;
    public NativeReference<bool> Ready;
    public NativeList<PowerGeneratorBudgetInput> GeneratorInputs;
    public NativeList<PowerConsumerBudgetInput> ConsumerInputs;
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
            if (!TryGetRepresentative(generator, out var representative))
            {
                return;
            }
            float available = PowerGeneratorSupplyPlanLookup[generator].AvailableEnergy;
            if (!math.isfinite(available) || available < 0f)
            {
                PowerValidationUtility.Fail(Validation, "[PowerBudgetReservationSystem] Invalid power frame input.");
                return;
            }
            GeneratorInputs.Add(new PowerGeneratorBudgetInput { Representative = representative });
        }
        foreach (Entity consumer in Consumers)
        {
            if (!PowerDemandLookup.IsComponentEnabled(consumer))
            {
                return;
            }
            if (!TryGetRepresentative(consumer, out var representative))
            {
                return;
            }
            float requested = PowerDemandLookup[consumer].RequestedEnergy;
            if (!math.isfinite(requested) || requested < 0f)
            {
                PowerValidationUtility.Fail(Validation, "[PowerBudgetReservationSystem] Invalid power frame input.");
                return;
            }
            ConsumerInputs.Add(new PowerConsumerBudgetInput
            {
                Representative = representative,
                RequestedEnergy = requested
            });
        }
        Ready.Value = true;
    }

    // 미연결은 정상 입력으로 Entity.Null을 반환한다. 잘못된 연결은 false로 거부한다.
    private bool TryGetRepresentative(Entity entity, out Entity representative)
    {
        representative = Entity.Null;
        Entity pole = entity;
        bool isPowerPole = PowerPoleLookup.HasComponent(entity);
        if (!isPowerPole)
        {
            if (!PowerGridConnectionDecisionLookup.TryGetComponent(entity, out var connection))
            {
                return false;
            }
            if (!PowerGridConnectionDecisionLookup.IsComponentEnabled(entity))
            {
                return false;
            }
            pole = connection.PowerPole;
            if (pole == Entity.Null)
            {
                return true;
            }
        }
        if (!isPowerPole && !PowerPoleLookup.HasComponent(pole))
        {
            return false;
        }
        if (!PowerGridTopologyDecisionLookup.TryGetComponent(pole, out var topology))
        {
            return false;
        }
        if (DisabledLookup.HasComponent(pole) || PrefabLookup.HasComponent(pole) ||
            PendingBuildingDemolitionLookup.HasComponent(pole))
        {
            return false;
        }
        representative = topology.RepresentativePole;
        if (representative == Entity.Null)
        {
            return false;
        }
        if (representative == pole)
        {
            return true;
        }
        if (!PowerPoleLookup.HasComponent(representative) ||
            !PowerGridTopologyDecisionLookup.TryGetComponent(representative, out var representativeTopology))
        {
            return false;
        }
        if (DisabledLookup.HasComponent(representative) || PrefabLookup.HasComponent(representative) ||
            PendingBuildingDemolitionLookup.HasComponent(representative))
        {
            return false;
        }
        return representativeTopology.RepresentativePole == representative;
    }
}
