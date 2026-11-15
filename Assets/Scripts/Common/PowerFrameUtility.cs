using Unity.Entities;
using Unity.Mathematics;

/// <summary>확정 전력 공급에 따른 작업 자격/시간과 에너지 반올림 계산. 상태/집계는 각 소유 시스템이 작성한다.</summary>
public static class PowerFrameUtility
{

    public static bool HasAllocatedPower(Entity entity,
        ComponentLookup<PowerDemand> demands, ComponentLookup<PowerSupplyResult> supplies, out float powerTimeRatio)
    {
        powerTimeRatio = 0f;
        if (!demands.TryGetComponent(entity, out var demand) || !supplies.TryGetComponent(entity, out var supply))
        {
            return false;
        }
        if (!demands.IsComponentEnabled(entity))
        {
            return false;
        }
        float requested = demand.RequestedEnergy;
        if (!(requested <= 0f || supply.AllocatedEnergy <= 0f || supply.SupplyRatio <= 0f))
        {
            powerTimeRatio = math.min(supply.SupplyRatio, supply.AllocatedEnergy / requested);
        }
        return requested > 0f && supply.AllocatedEnergy > 0f && supply.SupplyRatio > 0f;
    }

    /// <summary>공통 시간에 확정 공급률과 배정/요청 비율을 반영한 진행 가능 시간.</summary>
    public static float GetPoweredDeltaTime(Entity entity, float deltaTime,
        ComponentLookup<PowerDemand> demands, ComponentLookup<PowerSupplyResult> supplies)
    {
        if (!demands.TryGetComponent(entity, out var demand) || !supplies.TryGetComponent(entity, out var supply))
        {
            return 0f;
        }
        if (!demands.IsComponentEnabled(entity))
        {
            return 0f;
        }
        float requested = demand.RequestedEnergy;
        if (requested <= 0f || supply.AllocatedEnergy <= 0f || supply.SupplyRatio <= 0f)
        {
            return 0f;
        }
        return deltaTime * math.min(supply.SupplyRatio, supply.AllocatedEnergy / requested);
    }

    /// <summary>요청량은 위로 반올림해 마지막 충전 단위의 에너지가 부족해지지 않게 한다.</summary>
    public static float RoundEnergyUp(double energy)
    {
        if (energy <= 0d)
        {
            return 0f;
        }
        float result = math.min((float)energy, float.MaxValue);
        if ((double)result < energy && result < float.MaxValue)
        {
            result = math.asfloat(math.asuint(result) + 1u);
        }
        return result;
    }

    /// <summary>음이 아닌 double 목표량과 limit을 넘지 않는 float 값으로 내림한다.</summary>
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
