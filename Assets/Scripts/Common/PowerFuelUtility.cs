using System;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>정수 석탄 전환 계획을 계산하고 실제 소비할 에너지·실물의 조건을 확인한다.</summary>
public static class PowerFuelUtility
{
    internal static bool AreConsumedItemsValid(ComponentLookup<DestroyItemRequest> destroyItemRequestLookup,
        ComponentLookup<ItemIdentity> itemIdentityLookup,
        ComponentLookup<ItemOwnership> itemOwnershipLookup,
        DynamicBuffer<StoredItemElement> items, Entity generator, int consumedCount)
    {
        // 소비하지 않는 보관품은 재검사하지 않는다. 실제 삭제 대상이 다른 슬롯에도 남는지는 확인한다.
        for (int i = items.Length - consumedCount; i < items.Length; i++)
        {
            Entity item = items[i].ItemEntity;
            if (item == Entity.Null)
            {
                return false;
            }
            if (!itemIdentityLookup.HasComponent(item) || !itemOwnershipLookup.HasComponent(item))
            {
                return false;
            }
            if (items[i].ItemType != ItemTypeEnum.Coal || itemIdentityLookup[item].Type != ItemTypeEnum.Coal)
            {
                return false;
            }
            if (itemOwnershipLookup[item].Owner != generator)
            {
                return false;
            }
            if (destroyItemRequestLookup.HasComponent(item) && destroyItemRequestLookup.IsComponentEnabled(item))
            {
                return false;
            }
            for (int other = 0; other < items.Length; other++)
            {
                if (other != i && items[other].ItemEntity == item)
                {
                    return false;
                }
            }
        }
        return true;
    }

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
