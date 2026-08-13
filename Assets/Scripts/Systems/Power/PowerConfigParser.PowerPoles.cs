using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

internal static partial class PowerConfigParser
{
    private static bool TryParsePowerPoles(
        List<PowerPoleConfigData> configData,
        List<PowerPoleConfigElement> result)
    {
        HashSet<BuildingTypeEnum> usedBuildingTypes = new HashSet<BuildingTypeEnum>();

        for (int i = 0; i < configData.Count; i++)
        {
            PowerPoleConfigData poleData = configData[i];

            if (poleData == null)
            {
                Debug.LogError($"Null power pole config. Index : {i}");
                return false;
            }

            if (!TryParsePowerPole(poleData, out PowerPoleConfigElement powerPole))
                return false;

            if (!usedBuildingTypes.Add(powerPole.buildingType))
            {
                Debug.LogError($"Duplicated power pole config. Type : {powerPole.buildingType}");
                return false;
            }

            result.Add(powerPole);
        }

        if (!usedBuildingTypes.Contains(BuildingTypeEnum.PowerPole))
        {
            Debug.LogError("Required power pole config was not found. Type : PowerPole");
            return false;
        }

        return true;
    }

    private static bool TryParsePowerPole(
        PowerPoleConfigData poleData,
        out PowerPoleConfigElement powerPole)
    {
        powerPole = default;

        if (!TryParseBuildingType(
                poleData.buildingType,
                out BuildingTypeEnum buildingType))
            return false;

        if (buildingType != BuildingTypeEnum.PowerPole)
        {
            Debug.LogError($"Invalid power pole building type. Type : {poleData.buildingType}");
            return false;
        }

        if (!IsValidRange(poleData.supplyRangeX, nameof(poleData.supplyRangeX)))
            return false;

        if (!IsValidRange(poleData.supplyRangeY, nameof(poleData.supplyRangeY)))
            return false;

        if (!IsValidRange(poleData.connectionRangeX, nameof(poleData.connectionRangeX)))
            return false;

        if (!IsValidRange(poleData.connectionRangeY, nameof(poleData.connectionRangeY)))
            return false;

        powerPole = new PowerPoleConfigElement
        {
            buildingType = buildingType,
            supplyRange = new int2(poleData.supplyRangeX, poleData.supplyRangeY),
            connectionRange = new int2(poleData.connectionRangeX, poleData.connectionRangeY)
        };
        return true;
    }

    private static bool IsValidRange(int value, string fieldName)
    {
        if (value >= 0)
            return true;

        Debug.LogError($"Invalid power pole range. Field : {fieldName}, Value : {value}");
        return false;
    }
}
