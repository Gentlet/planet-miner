using System;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Mathematics;
using UnityEngine;

namespace PlanetMiner.Config
{
    /// <summary>통합 건물 설정의 역할별 전력 필드를 읽고 JSON·공개 게시·사전 등록의 같은 값 계약을 검사한다.</summary>
    public static class BuildingPowerConfigParser
    {
        private static readonly string[] PoleFields = { "supplyRange", "connectionRange" };
        private static readonly string[] MainFacilityFields = { "supplyRange", "connectionRange", "maximumGeneration" };
        private static readonly string[] CoalFields = { "maximumGeneration", "coalEnergyPerItem" };
        private static readonly string[] ConsumerFields = { "maximumConsumption" };

        public static bool TryRead(JObject entry, BuildingTypeEnum type, int tier, out BuildingPowerConfig power)
        {
            power = default;
            if (entry == null)
            {
                return Reject(type, tier, "is missing its building object");
            }
            string[] fields = GetFields(type);
            JToken token = entry["power"];
            if (fields.Length == 0)
            {
                if (token != null)
                {
                    return Reject(type, tier, "does not accept a static power configuration");
                }
                return true;
            }
            if (token == null)
            {
                return Reject(type, tier, "is missing its required power object");
            }
            if (token is not JObject powerObject)
            {
                return Reject(type, tier, "power must be an object");
            }
            if (type == BuildingTypeEnum.CoalGenerator)
            {
                JToken slots = entry["storageCapacity"];
                if (slots == null || slots.Type != JTokenType.Integer ||
                    !int.TryParse(slots.ToString(Formatting.None), NumberStyles.Integer, CultureInfo.InvariantCulture, out int count) ||
                    count <= 0 || count > GameConstants.MaxStorageSlots)
                {
                    return Reject(type, tier, "fuel storageCapacity must be an explicit positive JSON slot count within MaxStorageSlots");
                }
            }
            foreach (var property in powerObject.Properties())
            {
                if (Array.IndexOf(fields, property.Name) < 0)
                {
                    return Reject(type, tier, $"has an unsupported power field '{property.Name}'");
                }
            }

            power.HasConfiguration = true;
            foreach (string field in fields)
            {
                JToken value = powerObject[field];
                if (value == null)
                {
                    return Reject(type, tier, $"is missing power.{field}");
                }
                if (field == "supplyRange" || field == "connectionRange")
                {
                    if (value.Type != JTokenType.Integer ||
                        !int.TryParse(value.ToString(Formatting.None), NumberStyles.Integer, CultureInfo.InvariantCulture, out int range) ||
                        range < 0)
                    {
                        return Reject(type, tier, $"power.{field} must be a nonnegative JSON integer representable as int");
                    }
                    if (field == "supplyRange") power.SupplyRange = range;
                    else power.ConnectionRange = range;
                    continue;
                }
                if ((value.Type != JTokenType.Integer && value.Type != JTokenType.Float) ||
                    !float.TryParse(value.ToString(Formatting.None), NumberStyles.Float, CultureInfo.InvariantCulture, out float amount) ||
                    !math.isfinite(amount) || amount <= 0f)
                {
                    return Reject(type, tier, $"power.{field} must be a finite positive JSON number");
                }
                switch (field)
                {
                    case "maximumGeneration": power.MaximumGeneration = amount; break;
                    case "maximumConsumption": power.MaximumConsumption = amount; break;
                    case "coalEnergyPerItem": power.CoalEnergyPerItem = amount; break;
                }
            }
            return true;
        }

        public static bool Validate(in BuildingConfigElement config)
        {
            BuildingPowerConfig power = config.Power;
            BuildingTypeEnum type = config.BuildingType;
            int tier = config.Tier;
            if (power.SupplyRange < 0 || power.ConnectionRange < 0 ||
                !math.isfinite(power.MaximumGeneration) || !math.isfinite(power.MaximumConsumption) ||
                !math.isfinite(power.CoalEnergyPerItem) || power.MaximumGeneration < 0f ||
                power.MaximumConsumption < 0f || power.CoalEnergyPerItem < 0f)
            {
                return Reject(type, tier, "has invalid power values");
            }
            if (power.HasConfiguration != (GetFields(type).Length > 0))
            {
                return Reject(type, tier, "has missing or unexpected power configuration");
            }
            bool valid;
            switch (type)
            {
                case BuildingTypeEnum.PowerPole:
                    valid = power.MaximumGeneration == 0f && power.MaximumConsumption == 0f && power.CoalEnergyPerItem == 0f;
                    break;
                case BuildingTypeEnum.MainFacility:
                    valid = power.SupplyRange == 0 && power.MaximumGeneration > 0f &&
                        power.MaximumConsumption == 0f && power.CoalEnergyPerItem == 0f;
                    break;
                case BuildingTypeEnum.CoalGenerator:
                    valid = power.SupplyRange == 0 && power.ConnectionRange == 0 && power.MaximumGeneration > 0f &&
                        power.MaximumConsumption == 0f && power.CoalEnergyPerItem > 0f &&
                        config.StorageCapacity > 0 && config.StorageCapacity <= GameConstants.MaxStorageSlots;
                    break;
                case BuildingTypeEnum.Miner:
                case BuildingTypeEnum.Crafter:
                case BuildingTypeEnum.ResearchBuilding:
                    valid = power.SupplyRange == 0 && power.ConnectionRange == 0 && power.MaximumGeneration == 0f &&
                        power.MaximumConsumption > 0f && power.CoalEnergyPerItem == 0f;
                    break;
                default:
                    valid = power.SupplyRange == 0 && power.ConnectionRange == 0 && power.MaximumGeneration == 0f &&
                        power.MaximumConsumption == 0f && power.CoalEnergyPerItem == 0f;
                    break;
            }
            if (!valid)
            {
                return Reject(type, tier, "has power values or fuel slots inconsistent with its building role");
            }
            return true;
        }

        private static string[] GetFields(BuildingTypeEnum type)
        {
            return type switch
            {
                BuildingTypeEnum.PowerPole => PoleFields,
                BuildingTypeEnum.MainFacility => MainFacilityFields,
                BuildingTypeEnum.CoalGenerator => CoalFields,
                BuildingTypeEnum.Miner or BuildingTypeEnum.Crafter or BuildingTypeEnum.ResearchBuilding => ConsumerFields,
                _ => Array.Empty<string>()
            };
        }

        private static bool Reject(BuildingTypeEnum type, int tier, string reason)
        {
            Debug.LogError($"[BuildingConfigLoader] {type}/{tier} {reason}.");
            return false;
        }
    }
}
