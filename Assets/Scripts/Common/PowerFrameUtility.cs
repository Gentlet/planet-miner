using Unity.Entities;
using Unity.Mathematics;

/// <summary>전력 준비 결과의 대표 조회와 공급에 따른 작업 시간 계산. 상태·집계의 생성/갱신은 각 소유 시스템이 담당한다.</summary>
public static class PowerFrameUtility
{
    internal static bool TryGetRepresentative(ComponentLookup<Disabled> disabledLookup,
        EntityStorageInfoLookup entityStorageLookup,
        ComponentLookup<PendingBuildingDemolition> pendingBuildingDemolitionLookup,
        ComponentLookup<PowerGridConnectionDecision> powerGridConnectionDecisionLookup,
        ComponentLookup<PowerGridTopologyDecision> powerGridTopologyDecisionLookup,
        ComponentLookup<PowerPole> powerPoleLookup,
        ComponentLookup<Prefab> prefabLookup, Entity entity, out Entity representative)
    {
        representative = Entity.Null;
        Entity pole = entity;
        if (!powerPoleLookup.HasComponent(entity))
        {
            if (!powerGridConnectionDecisionLookup.HasComponent(entity))
            {
                return false;
            }
            if (!powerGridConnectionDecisionLookup.IsComponentEnabled(entity))
            {
                return false;
            }
            pole = powerGridConnectionDecisionLookup[entity].PowerPole;
            if (pole == Entity.Null)
            {
                return true;
            }
        }
        if (!entityStorageLookup.Exists(pole))
        {
            return false;
        }
        if (!powerPoleLookup.HasComponent(pole) || !powerGridTopologyDecisionLookup.HasComponent(pole))
        {
            return false;
        }
        if (disabledLookup.HasComponent(pole) || prefabLookup.HasComponent(pole) ||
            pendingBuildingDemolitionLookup.HasComponent(pole))
        {
            return false;
        }
        representative = powerGridTopologyDecisionLookup[pole].RepresentativePole;
        if (representative == Entity.Null)
        {
            return false;
        }
        if (!entityStorageLookup.Exists(representative))
        {
            return false;
        }
        if (!powerPoleLookup.HasComponent(representative) ||
            !powerGridTopologyDecisionLookup.HasComponent(representative))
        {
            return false;
        }
        if (disabledLookup.HasComponent(representative) || prefabLookup.HasComponent(representative) ||
            pendingBuildingDemolitionLookup.HasComponent(representative))
        {
            return false;
        }
        return powerGridTopologyDecisionLookup[representative].RepresentativePole == representative;
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

    /// <summary>작업 결정은 배정 자격을 확인하고 실제 시간 누적은 Execution이 담당한다.</summary>
    public static bool HasAllocatedPower(Entity entity,
        ComponentLookup<PowerDemand> demands, ComponentLookup<PowerSupplyResult> supplies)
    {
        if (!demands.HasComponent(entity) || !supplies.HasComponent(entity))
        {
            return false;
        }
        if (!demands.IsComponentEnabled(entity))
        {
            return false;
        }
        var supply = supplies[entity];
        return demands[entity].RequestedEnergy > 0f && supply.AllocatedEnergy > 0f && supply.SupplyRatio > 0f;
    }

    /// <summary>공통 시간에 확정 공급률과 배정/요청 비율을 반영한 진행 가능 시간.</summary>
    public static float GetPoweredDeltaTime(Entity entity, float deltaTime,
        ComponentLookup<PowerDemand> demands, ComponentLookup<PowerSupplyResult> supplies)
    {
        if (!demands.HasComponent(entity) || !supplies.HasComponent(entity))
        {
            return 0f;
        }
        if (!demands.IsComponentEnabled(entity))
        {
            return 0f;
        }
        float requested = demands[entity].RequestedEnergy;
        var supply = supplies[entity];
        if (requested <= 0f || supply.AllocatedEnergy <= 0f || supply.SupplyRatio <= 0f)
        {
            return 0f;
        }
        return deltaTime * math.min(supply.SupplyRatio, supply.AllocatedEnergy / requested);
    }

    /// <summary>float 변환이 double 예산을 초과하지 않게 한다. 미세한 잔여분도 재배분하지 않는다.</summary>
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
