using System;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Entities;

/// <summary>설정된 연료의 가용 에너지와 슬롯 순서의 소비 계획을 계산한다.</summary>
public static class PowerFuelUtility
{
    internal static double CountAvailable(float remaining, in DynamicBuffer<StorageSlotElement> stored,
        in FuelStorageSlots range, in BuildingConfigElement config)
    {
        double energy = remaining;
        for (int slot = range.Start; slot < range.Start + range.Count; slot++)
        {
            var item = stored[slot];
            energy += item.Quantity * (double)ReadEnergyPerItem(config, item.ItemType);
        }
        return energy;
    }

    // 슬롯 자체가 선택 순서를 소유한다. 설정의 품목 목록은 에너지 조회에만 사용한다.
    internal static bool TryPlan(float remaining, float allocated, in DynamicBuffer<StorageSlotElement> stored,
        in FuelStorageSlots range, in BuildingConfigElement config, out FixedList4096Bytes<PowerFuelSpend> spends,
        out float afterConsumption)
    {
        spends = default;
        afterConsumption = 0f;
        if (!math.isfinite(remaining) || remaining < 0f || !math.isfinite(allocated) || allocated < 0f ||
            range.Start < 0 || range.Count <= 0 || range.Count > stored.Length - range.Start)
            return false;

        double energy = remaining;
        for (int slot = range.Start; slot < range.Start + range.Count && energy < allocated; slot++)
        {
            var item = stored[slot];
            float energyPerItem = ReadEnergyPerItem(config, item.ItemType);
            if (item.Quantity == 0)
                continue;
            if (item.Quantity < 0 || energyPerItem <= 0f)
                return false;

            int take = (int)Math.Min(item.Quantity, Math.Ceiling(((double)allocated - energy) / energyPerItem));
            spends.Add(new PowerFuelSpend { Slot = slot, ItemType = item.ItemType, Quantity = take });
            energy += take * (double)energyPerItem;
        }
        double remainder = energy - allocated;
        if (remainder < 0d || remainder > float.MaxValue)
            return false;
        afterConsumption = PowerFrameUtility.RoundEnergyDown(remainder, float.MaxValue);
        return true;
    }

    private static float ReadEnergyPerItem(in BuildingConfigElement config, ItemTypeEnum itemType)
    {
        for (int i = 0; i < config.Power.Generation.FuelStorage.Fuels.Length; i++)
        {
            if (config.Power.Generation.FuelStorage.Fuels[i].ItemType == itemType)
                return config.Power.Generation.FuelStorage.Fuels[i].EnergyPerItem;
        }
        return 0f;
    }
}
