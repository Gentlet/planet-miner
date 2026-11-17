using System;
using Unity.Mathematics;

/// <summary>정수 석탄 전환 계획을 계산하고 실제 소비할 에너지와 수량의 조건을 확인한다.</summary>
public static class PowerFuelUtility
{
    public static void PrepareConsumption(ref PowerGeneratorSupplyPlan plan, float remaining, float energyPerItem)
    {
        plan.ExpectedFuelEnergy = remaining;
        double deficit = (double)plan.AllocatedEnergy - remaining;
        plan.FuelItemCount = deficit > 0d ? (int)Math.Ceiling(deficit / energyPerItem) : 0;
        double converted = remaining + plan.FuelItemCount * (double)energyPerItem;
        plan.RemainingEnergyAfterConsumption = PowerFrameUtility.RoundEnergyDown(converted - plan.AllocatedEnergy, float.MaxValue);
    }

    internal static bool IsConsumptionValid(float remaining, int storedItemCount,
        in BuildingConfigElement config, in PowerGeneratorSupplyPlan plan)
    {
        if (remaining != plan.ExpectedFuelEnergy)
        {
            return false;
        }
        if (plan.FuelItemCount < 0 || plan.FuelItemCount > storedItemCount)
        {
            return false;
        }
        double converted = remaining + plan.FuelItemCount * (double)config.Power.CoalEnergyPerItem;
        double afterConsumption = converted - plan.AllocatedEnergy;
        if (afterConsumption < 0d || afterConsumption > float.MaxValue)
        {
            return false;
        }
        var expected = plan;
        PrepareConsumption(ref expected, remaining, config.Power.CoalEnergyPerItem);
        return expected.FuelItemCount == plan.FuelItemCount &&
               expected.RemainingEnergyAfterConsumption == plan.RemainingEnergyAfterConsumption &&
               math.isfinite(plan.RemainingEnergyAfterConsumption);
    }

}
