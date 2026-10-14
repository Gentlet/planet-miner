using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>전력 프레임의 구성/설정 검사와 이번 틱 대표 조회. 상태·집계의 생성/갱신은 각 소유 시스템이 담당한다.</summary>
public static class PowerFrameUtility
{
    public static bool TryGetConfig(EntityManager manager, Entity entity,
        DynamicBuffer<BuildingConfigElement> configs, out BuildingConfigElement config)
    {
        config = default;
        if (!manager.HasComponent<BuildingType>(entity))
        {
            return false;
        }
        var type = manager.GetComponentData<BuildingType>(entity);
        if (!BuildingConfigLookupUtility.TryGetConfig(configs, type.Type, type.Tier, out config))
        {
            return false;
        }
        return PlanetMiner.Config.BuildingPowerConfigParser.Validate(config);
    }

    public static bool IsConsumerValid(EntityManager manager, Entity entity,
        DynamicBuffer<BuildingConfigElement> configs)
    {
        if (!manager.HasComponent<PowerDemand>(entity) || !manager.HasComponent<PowerUsageResult>(entity) ||
            !manager.HasComponent<PowerSupplyResult>(entity) ||
            !manager.HasComponent<PowerSupplyReservation>(entity) || !manager.HasComponent<PowerGridConnection>(entity))
        {
            return false;
        }
        if (!TryGetConfig(manager, entity, configs, out var config))
        {
            return false;
        }
        if (!HasFrameConnectionData(manager, entity, config.BuildingType))
        {
            return false;
        }
        switch (config.BuildingType)
        {
            case BuildingTypeEnum.Miner:
                return manager.HasComponent<MinerDecision>(entity);
            case BuildingTypeEnum.Crafter:
                return manager.HasComponent<CrafterDecision>(entity);
            case BuildingTypeEnum.MainFacility:
            case BuildingTypeEnum.ResearchBuilding:
            case BuildingTypeEnum.DroneStation:
                return true;
            default:
                return false;
        }
    }

    public static bool IsGeneratorValid(EntityManager manager, Entity entity,
        DynamicBuffer<BuildingConfigElement> configs)
    {
        if (!manager.HasComponent<PowerGeneratorSupplyPlan>(entity) ||
            !manager.HasComponent<PowerGridConnection>(entity))
        {
            return false;
        }
        if (!TryGetConfig(manager, entity, configs, out var config))
        {
            return false;
        }
        if (!HasFrameConnectionData(manager, entity, config.BuildingType))
        {
            return false;
        }
        if (config.BuildingType == BuildingTypeEnum.MainFacility)
        {
            return true;
        }
        if (config.BuildingType != BuildingTypeEnum.CoalGenerator)
        {
            return false;
        }
        if (!manager.HasComponent<FuelEnergyState>(entity))
        {
            return false;
        }
        float energy = manager.GetComponentData<FuelEnergyState>(entity).RemainingEnergy;
        return math.isfinite(energy) && energy >= 0f;
    }

    private static bool HasFrameConnectionData(EntityManager manager, Entity entity, BuildingTypeEnum type)
    {
        if (type == BuildingTypeEnum.MainFacility)
        {
            return manager.HasComponent<PowerPole>(entity) && manager.HasComponent<PowerGridTopologyDecision>(entity);
        }
        if (manager.HasComponent<PowerPole>(entity))
        {
            return false;
        }
        return manager.HasComponent<PowerGridConnectionDecision>(entity);
    }

    public static bool TryGetRepresentative(EntityManager manager, Entity entity, out Entity representative)
    {
        representative = Entity.Null;
        Entity pole = entity;
        if (!manager.HasComponent<PowerPole>(entity))
        {
            if (!manager.HasComponent<PowerGridConnectionDecision>(entity))
            {
                return false;
            }
            if (!manager.IsComponentEnabled<PowerGridConnectionDecision>(entity))
            {
                return false;
            }
            pole = manager.GetComponentData<PowerGridConnectionDecision>(entity).PowerPole;
            if (pole == Entity.Null)
            {
                return true;
            }
        }
        if (!manager.Exists(pole))
        {
            return false;
        }
        if (!manager.HasComponent<PowerPole>(pole) || !manager.HasComponent<PowerGridTopologyDecision>(pole))
        {
            return false;
        }
        if (manager.HasComponent<Disabled>(pole) || manager.HasComponent<Prefab>(pole) ||
            manager.HasComponent<PendingBuildingDemolition>(pole))
        {
            return false;
        }
        representative = manager.GetComponentData<PowerGridTopologyDecision>(pole).RepresentativePole;
        if (representative == Entity.Null)
        {
            return false;
        }
        if (!manager.Exists(representative))
        {
            return false;
        }
        if (!manager.HasComponent<PowerPole>(representative) ||
            !manager.HasComponent<PowerGridTopologyDecision>(representative))
        {
            return false;
        }
        if (manager.HasComponent<Disabled>(representative) || manager.HasComponent<Prefab>(representative) ||
            manager.HasComponent<PendingBuildingDemolition>(representative))
        {
            return false;
        }
        return manager.GetComponentData<PowerGridTopologyDecision>(representative).RepresentativePole == representative;
    }

    public static Entity GetRepresentative(Entity entity, ComponentLookup<PowerPole> poles,
        ComponentLookup<PowerGridConnectionDecision> connections, ComponentLookup<PowerGridTopologyDecision> topology)
    {
        Entity pole = poles.HasComponent(entity) ? entity : connections[entity].PowerPole;
        if (pole == Entity.Null)
        {
            return Entity.Null;
        }
        return topology[pole].RepresentativePole;
    }

    public static bool IsSelectionValid(EntityManager manager, Entity entity,
        DynamicBuffer<BuildingConfigElement> configs)
    {
        if (manager.HasComponent<PowerPole>(entity))
        {
            return true;
        }
        if (!PowerGridConnectionDecisionSystem.IsParticipantValid(manager, entity))
        {
            return false;
        }
        Entity pole = manager.GetComponentData<PowerGridConnectionDecision>(entity).PowerPole;
        if (pole == Entity.Null)
        {
            return true;
        }
        if (!manager.Exists(pole))
        {
            return false;
        }
        if (!manager.HasComponent<GridPosition>(pole))
        {
            return false;
        }
        if (!TryGetConfig(manager, pole, configs, out var config))
        {
            return false;
        }
        var footprint = manager.GetComponentData<BuildingFootprint>(entity);
        var direction = manager.GetComponentData<Direction>(entity);
        return PowerRangeUtility.TryGetFootprintDistanceSquared(manager.GetComponentData<GridPosition>(pole).Value,
            config.Power.SupplyRange, manager.GetComponentData<GridPosition>(entity).Value,
            footprint.GetEffectiveSize(direction.dir), out _);
    }

    /// <summary>입력을 검사한 시스템이 EndBuilding에 Fatal을 기록하고 정상 예산을 공개하지 않는다.</summary>
    public static void RecordFailure(ref SystemState state, FixedString128Bytes message)
    {
        var failureEcb = state.World.GetOrCreateSystemManaged<EndBuildingEntityCommandBufferSystem>()
            .CreateCommandBuffer();
        SimulationFailureUtility.Record(ref failureEcb, message);
    }

    /// <summary>float 변환이 원래 double 예산을 초과하지 않게 한다. 미세한 미배정량도 같은 틱에 재배분하지 않는다.</summary>
    public static float RoundEnergyDown(double energy, float limit)
    {
        if (energy <= 0d)
        {
            return 0f;
        }
        float result = math.min((float)energy, limit);
        if ((double)result > energy)
        {
            result = math.asfloat(math.asuint(result) - 1u);
        }
        return result;
    }
}
