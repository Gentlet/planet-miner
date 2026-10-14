using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

/// <summary>미게시 예산의 연결/수요/연료 근거를 최종 검사하고 이번 틱 공급을 생산 전에 한 번 게시한다.</summary>
[UpdateInGroup(typeof(BuildingExecutionGroup), OrderFirst = true)]
[UpdateBefore(typeof(MinerExecutionSystem))]
[UpdateBefore(typeof(CrafterExecutionSystem))]
public partial struct PowerSupplyExecutionSystem : ISystem
{
    private EntityQuery _generators;
    private EntityQuery _consumers;

    public void OnCreate(ref SystemState state)
    {
        _generators = SystemAPI.QueryBuilder().WithAll<PowerGenerator>().WithNone<PendingBuildingDemolition>().Build();
        _consumers = SystemAPI.QueryBuilder().WithAll<PowerConsumer>().WithNone<PendingBuildingDemolition>().Build();
    }

    public void OnUpdate(ref SystemState state)
    {
        if (_generators.IsEmptyIgnoreFilter && _consumers.IsEmptyIgnoreFilter)
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
        state.EntityManager.CompleteDependencyBeforeRO<PowerSupplyReservation>();
        state.EntityManager.CompleteDependencyBeforeRO<PowerGeneratorSupplyPlan>();
        state.EntityManager.CompleteDependencyBeforeRO<PowerDemand>();
        state.EntityManager.CompleteDependencyBeforeRO<PowerGridTopologyDecision>();
        state.EntityManager.CompleteDependencyBeforeRO<PowerGridConnectionDecision>();
        var configs = state.EntityManager.GetBuffer<BuildingConfigElement>(configEntity, true);
        using var generators = _generators.ToEntityArray(Allocator.Temp);
        using var consumers = _consumers.ToEntityArray(Allocator.Temp);
        // Reservation 미실행/입력 실패를 손상된 배정으로 오인하지 않는다. 준비되지 않았으면 게시하지 않는다.
        foreach (Entity consumer in consumers)
        {
            if (!state.EntityManager.HasComponent<PowerSupplyReservation>(consumer))
            {
                RecordFailure(ref state);
                return;
            }
            if (!state.EntityManager.IsComponentEnabled<PowerSupplyReservation>(consumer))
            {
                return;
            }
        }
        int capacity = math.max(1, generators.Length + consumers.Length);
        using var funded = new NativeParallelHashMap<Entity, double>(capacity, Allocator.Temp);
        using var allocated = new NativeParallelHashMap<Entity, double>(capacity, Allocator.Temp);
        float deltaTime = math.min(SystemAPI.Time.DeltaTime, GameConstants.MaxSimulationDeltaTime);

        foreach (Entity generator in generators)
        {
            if (!PowerFrameUtility.IsGeneratorValid(state.EntityManager, generator, configs))
            {
                RecordFailure(ref state);
                return;
            }
            if (!state.EntityManager.IsComponentEnabled<PowerGeneratorSupplyPlan>(generator))
            {
                return;
            }
            var plan = state.EntityManager.GetComponentData<PowerGeneratorSupplyPlan>(generator);
            if (!PowerFrameUtility.TryGetRepresentative(state.EntityManager, generator, out var representative))
            {
                RecordFailure(ref state);
                return;
            }
            if (representative != plan.RepresentativePole)
            {
                RecordFailure(ref state);
                return;
            }
            if (!PowerFrameUtility.IsSelectionValid(state.EntityManager, generator, configs))
            {
                RecordFailure(ref state);
                return;
            }
            PowerFrameUtility.TryGetConfig(state.EntityManager, generator, configs, out var config);
            if (!IsGeneratorBudgetValid(state.EntityManager, generator, config, plan, deltaTime))
            {
                RecordFailure(ref state);
                return;
            }
            if (representative == Entity.Null)
            {
                if (plan.AllocatedEnergy != 0f)
                {
                    RecordFailure(ref state);
                    return;
                }
                continue;
            }
            AccumulateEnergy(funded, representative, plan.AllocatedEnergy);
        }
        foreach (Entity consumer in consumers)
        {
            if (!PowerFrameUtility.IsConsumerValid(state.EntityManager, consumer, configs))
            {
                RecordFailure(ref state);
                return;
            }
            if (!state.EntityManager.IsComponentEnabled<PowerSupplyReservation>(consumer))
            {
                return;
            }
            if (!state.EntityManager.IsComponentEnabled<PowerDemand>(consumer))
            {
                RecordFailure(ref state);
                return;
            }
            if (!PowerFrameUtility.TryGetRepresentative(state.EntityManager, consumer, out var representative))
            {
                RecordFailure(ref state);
                return;
            }
            var reservation = state.EntityManager.GetComponentData<PowerSupplyReservation>(consumer);
            if (representative != reservation.RepresentativePole)
            {
                RecordFailure(ref state);
                return;
            }
            if (!PowerFrameUtility.IsSelectionValid(state.EntityManager, consumer, configs))
            {
                RecordFailure(ref state);
                return;
            }
            float requested = state.EntityManager.GetComponentData<PowerDemand>(consumer).RequestedEnergy;
            if (!IsConsumerBudgetValid(reservation, requested))
            {
                RecordFailure(ref state);
                return;
            }
            if (representative == Entity.Null)
            {
                if (reservation.AllocatedEnergy != 0f || reservation.SupplyRatio != 0f)
                {
                    RecordFailure(ref state);
                    return;
                }
                continue;
            }
            AccumulateEnergy(allocated, representative, reservation.AllocatedEnergy);
        }
        using var representatives = allocated.GetKeyArray(Allocator.Temp);
        foreach (Entity representative in representatives)
        {
            funded.TryGetValue(representative, out double available);
            if (allocated[representative] > available)
            {
                RecordFailure(ref state);
                return;
            }
        }
        var consumerEntities = _consumers.ToEntityArray(Allocator.TempJob);
        var handle = new PublishPowerSupplyJob
        {
            Consumers = consumerEntities,
            Reservations = SystemAPI.GetComponentLookup<PowerSupplyReservation>(true),
            Results = SystemAPI.GetComponentLookup<PowerSupplyResult>()
        }.Schedule(state.Dependency);
        state.Dependency = consumerEntities.Dispose(handle);
    }

    private static void AccumulateEnergy(NativeParallelHashMap<Entity, double> totals, Entity representative, float energy)
    {
        totals.TryGetValue(representative, out double total);
        totals[representative] = total + energy;
    }

    private static bool IsGeneratorBudgetValid(EntityManager manager, Entity generator,
        in BuildingConfigElement config, in PowerGeneratorSupplyPlan plan, float deltaTime)
    {
        if (!math.isfinite(plan.AvailableEnergy) || !math.isfinite(plan.AllocatedEnergy) ||
            plan.AllocatedEnergy < 0f || plan.AvailableEnergy < plan.AllocatedEnergy ||
            plan.AvailableEnergy > config.Power.MaximumGeneration * deltaTime)
        {
            return false;
        }
        if (config.BuildingType == BuildingTypeEnum.CoalGenerator)
        {
            float remaining = manager.GetComponentData<FuelEnergyState>(generator).RemainingEnergy;
            if (plan.AllocatedEnergy > remaining)
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsConsumerBudgetValid(in PowerSupplyReservation reservation, float requested)
    {
        if (reservation.RequestedEnergy != requested)
        {
            return false;
        }
        return math.isfinite(requested) && requested >= 0f && math.isfinite(reservation.SupplyRatio) &&
            reservation.SupplyRatio >= 0f && reservation.SupplyRatio <= 1f &&
            math.isfinite(reservation.AllocatedEnergy) && reservation.AllocatedEnergy >= 0f &&
            reservation.AllocatedEnergy <= requested;
    }

    private static void RecordFailure(ref SystemState state)
    {
        PowerFrameUtility.RecordFailure(ref state, "[PowerSupplyExecutionSystem] Power budget/connection/fuel changed before publication.");
    }
}

[BurstCompile]
internal struct PublishPowerSupplyJob : IJob
{
    [ReadOnly] public NativeArray<Entity> Consumers;
    [ReadOnly] public ComponentLookup<PowerSupplyReservation> Reservations;
    public ComponentLookup<PowerSupplyResult> Results;

    public void Execute()
    {
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
