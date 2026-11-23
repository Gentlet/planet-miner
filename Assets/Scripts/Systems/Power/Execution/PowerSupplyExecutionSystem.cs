using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

/// <summary>준비된 예산을 신뢰하고 실제 연료 소비 조건을 검사한 뒤 공급을 한 번 확정한다.</summary>
[UpdateInGroup(typeof(PowerExecutionGroup), OrderFirst = true)]
public partial struct PowerSupplyExecutionSystem : ISystem
{
    private EntityQuery _generators;
    private EntityQuery _consumers;

    public void OnCreate(ref SystemState state)
    {
        _generators = SystemAPI.QueryBuilder().WithAll<PowerGenerator>()
            .WithNone<PendingBuildingDemolition>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).Build();
        _consumers = SystemAPI.QueryBuilder().WithAll<PowerConsumer>()
            .WithNone<PendingBuildingDemolition>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).Build();
    }

    public void OnUpdate(ref SystemState state)
    {
        var validationOwner = state.World.GetOrCreateSystemManaged<PowerValidationInitializationSystem>();
        var dependency = validationOwner.GetDependency(state.Dependency);
        Entity configEntity = SystemAPI.GetSingletonEntity<BuildingConfig>();
        var generators = _generators.ToEntityListAsync(Allocator.TempJob, dependency, out var generatorsReady);
        var consumers = _consumers.ToEntityListAsync(Allocator.TempJob, dependency, out var consumersReady);
        var ready = new NativeReference<bool>(Allocator.TempJob);
        var plans = SystemAPI.GetComponentLookup<PowerGeneratorSupplyPlan>(true);
        var reservations = SystemAPI.GetComponentLookup<PowerSupplyReservation>(true);
        var types = SystemAPI.GetComponentLookup<BuildingIdentity>(true);
        var validate = new ValidatePowerConsumptionJob
        {
            Generators = generators.AsDeferredJobArray(), Consumers = consumers.AsDeferredJobArray(),
            Plans = plans,
            Reservations = reservations,
            Types = types,
            FuelSpends = SystemAPI.GetBufferLookup<PowerFuelSpend>(true),
            FuelSlots = SystemAPI.GetComponentLookup<FuelStorageSlots>(true),
            ConfigLookup = SystemAPI.GetBufferLookup<BuildingConfigElement>(true), ConfigEntity = configEntity,
            Fuel = SystemAPI.GetComponentLookup<FuelEnergyState>(true),
            Stored = SystemAPI.GetBufferLookup<StorageSlotElement>(true),
            Validation = validationOwner.Validation, Ready = ready
        }.Schedule(JobHandle.CombineDependencies(generatorsReady, consumersReady, state.Dependency));
        var commit = new CommitPowerSupplyJob
        {
            Generators = generators.AsDeferredJobArray(), Consumers = consumers.AsDeferredJobArray(),
            Validation = validationOwner.Validation, Ready = ready,
            Plans = plans,
            FuelSlots = SystemAPI.GetComponentLookup<FuelStorageSlots>(true),
            FuelSpends = SystemAPI.GetBufferLookup<PowerFuelSpend>(true),
            Fuel = SystemAPI.GetComponentLookup<FuelEnergyState>(), Stored = SystemAPI.GetBufferLookup<StorageSlotElement>(),
            Reservations = reservations,
            Results = SystemAPI.GetComponentLookup<PowerSupplyResult>()
        }.Schedule(JobHandle.CombineDependencies(validate, state.Dependency));
        state.Dependency = JobHandle.CombineDependencies(generators.Dispose(commit), consumers.Dispose(commit), ready.Dispose(commit));
        validationOwner.Register(state.Dependency);
    }
}

[BurstCompile]
internal struct ValidatePowerConsumptionJob : IJob
{
    [ReadOnly] public NativeArray<Entity> Generators;
    [ReadOnly] public NativeArray<Entity> Consumers;
    [ReadOnly] public ComponentLookup<PowerGeneratorSupplyPlan> Plans;
    [ReadOnly] public ComponentLookup<PowerSupplyReservation> Reservations;
    [ReadOnly] public ComponentLookup<BuildingIdentity> Types;
    [ReadOnly] public BufferLookup<PowerFuelSpend> FuelSpends;
    [ReadOnly] public BufferLookup<BuildingConfigElement> ConfigLookup;
    public Entity ConfigEntity;
    [ReadOnly] public ComponentLookup<FuelStorageSlots> FuelSlots;
    [ReadOnly] public ComponentLookup<FuelEnergyState> Fuel;
    [ReadOnly] public BufferLookup<StorageSlotElement> Stored;
    public NativeReference<PowerValidationResult> Validation;
    public NativeReference<bool> Ready;

    public void Execute()
    {
        if (Validation.Value.HasError)
        {
            return;
        }
        // 미준비 계획은 정상 0과 구분하고 실제 연료 변경 전에 전체 입력의 준비를 확인한다.
        foreach (Entity generator in Generators)
        {
            if (!Plans.IsComponentEnabled(generator))
            {
                return;
            }
        }
        foreach (Entity consumer in Consumers)
        {
            if (!Reservations.IsComponentEnabled(consumer))
            {
                return;
            }
        }
        DynamicBuffer<BuildingConfigElement> configs = default;
        foreach (Entity generator in Generators)
        {
            var type = Types[generator];
            if (!FuelSlots.TryGetComponent(generator, out var range))
            {
                continue;
            }
            if (!configs.IsCreated)
            {
                configs = ConfigLookup[ConfigEntity];
            }
            var plan = Plans[generator];
            float remaining = Fuel[generator].RemainingEnergy;
            if (!BuildingConfigLookupUtility.TryGetConfig(configs, type.BuildingId, out var config) ||
                plan.AllocatedEnergy > plan.AvailableEnergy || remaining != plan.ExpectedFuelEnergy ||
                !FuelSpends.TryGetBuffer(generator, out var spends) ||
                !PowerFuelUtility.TryPlan(remaining, plan.AllocatedEnergy, Stored[generator], range, config,
                    out var expected, out var after) || after != plan.RemainingEnergyAfterConsumption ||
                spends.Length != expected.Length)
            {
                PowerValidationUtility.Fail(Validation, "[PowerSupplyExecutionSystem] Invalid fuel consumption plan or item.");
                return;
            }
            for (int i = 0; i < spends.Length; i++)
            {
                var spend = spends[i];
                var entry = expected[i];
                if (spend.Slot != entry.Slot || spend.ItemType != entry.ItemType || spend.Quantity != entry.Quantity)
                {
                    PowerValidationUtility.Fail(Validation, "[PowerSupplyExecutionSystem] Fuel slots changed after planning.");
                    return;
                }
            }
        }
        Ready.Value = true;
    }
}

[BurstCompile]
internal struct CommitPowerSupplyJob : IJob
{
    [ReadOnly] public NativeArray<Entity> Generators;
    [ReadOnly] public NativeArray<Entity> Consumers;
    [ReadOnly] public NativeReference<PowerValidationResult> Validation;
    [ReadOnly] public NativeReference<bool> Ready;
    [ReadOnly] public ComponentLookup<PowerGeneratorSupplyPlan> Plans;
    [ReadOnly] public ComponentLookup<FuelStorageSlots> FuelSlots;
    [ReadOnly] public BufferLookup<PowerFuelSpend> FuelSpends;
    [ReadOnly] public ComponentLookup<PowerSupplyReservation> Reservations;
    public ComponentLookup<FuelEnergyState> Fuel;
    public BufferLookup<StorageSlotElement> Stored;
    public ComponentLookup<PowerSupplyResult> Results;

    public void Execute()
    {
        if (Validation.Value.HasError || !Ready.Value)
        {
            return;
        }
        foreach (Entity generator in Generators)
        {
            if (!FuelSlots.HasComponent(generator))
            {
                continue;
            }
            var plan = Plans[generator];
            var stored = Stored[generator];
            foreach (var spend in FuelSpends[generator])
                InventorySlotUtility.Take(stored, spend.Slot, spend.ItemType, spend.Quantity);
            Fuel[generator] = new FuelEnergyState { RemainingEnergy = plan.RemainingEnergyAfterConsumption };
        }
        foreach (Entity consumer in Consumers)
        {
            var reservation = Reservations[consumer];
            Results[consumer] = new PowerSupplyResult
            {
                SupplyRatio = reservation.SupplyRatio,
                AllocatedEnergy = reservation.AllocatedEnergy
            };
        }
    }
}
