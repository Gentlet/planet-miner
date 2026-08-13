using System.Collections.Generic;
using UnityEngine;

internal static partial class PowerConfigParser
{
    private static bool TryParseConsumers(
        List<PowerConsumerConfigData> configData,
        List<PowerConsumerConfigElement> result)
    {
        HashSet<BuildingTypeEnum> usedBuildingTypes = new HashSet<BuildingTypeEnum>();

        for (int i = 0; i < configData.Count; i++)
        {
            PowerConsumerConfigData consumerData = configData[i];

            if (consumerData == null)
            {
                Debug.LogError($"Null power consumer config. Index : {i}");
                return false;
            }

            if (!TryParseConsumer(consumerData, out PowerConsumerConfigElement consumer))
                return false;

            if (!usedBuildingTypes.Add(consumer.buildingType))
            {
                Debug.LogError($"Duplicated power consumer config. Type : {consumer.buildingType}");
                return false;
            }

            result.Add(consumer);
        }

        if (!usedBuildingTypes.Contains(BuildingTypeEnum.Miner))
        {
            Debug.LogError("Required power consumer config was not found. Type : Miner");
            return false;
        }

        if (!usedBuildingTypes.Contains(BuildingTypeEnum.Crafter))
        {
            Debug.LogError("Required power consumer config was not found. Type : Crafter");
            return false;
        }

        return true;
    }

    private static bool TryParseConsumer(
        PowerConsumerConfigData consumerData,
        out PowerConsumerConfigElement consumer)
    {
        consumer = default;

        if (!TryParseBuildingType(
                consumerData.buildingType,
                out BuildingTypeEnum buildingType))
            return false;

        if (!IsPowerConsumer(buildingType))
        {
            Debug.LogError($"Invalid power consumer building type. Type : {consumerData.buildingType}");
            return false;
        }

        if (consumerData.maximumConsumption < 0f)
        {
            Debug.LogError(
                $"Invalid maximum power consumption. Type : {consumerData.buildingType}, " +
                $"Value : {consumerData.maximumConsumption}");
            return false;
        }

        consumer = new PowerConsumerConfigElement
        {
            buildingType = buildingType,
            maximumConsumption = consumerData.maximumConsumption
        };
        return true;
    }

    private static bool IsPowerConsumer(BuildingTypeEnum buildingType)
    {
        return buildingType == BuildingTypeEnum.Miner ||
               buildingType == BuildingTypeEnum.Crafter;
    }
}
