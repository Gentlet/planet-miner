using System;
using System.Collections.Generic;
using UnityEngine;

internal static class BuildingRuntimeConfigParser
{
    private static readonly BuildingTypeEnum[] requiredBuildingTypes =
    {
        BuildingTypeEnum.Belt,
        BuildingTypeEnum.Miner,
        BuildingTypeEnum.Crafter,
        BuildingTypeEnum.Storage
    };

    public static List<BuildingRuntimeConfigElement> Parse(
        string json,
        string resourcePath)
    {
        BuildingRuntimeConfigFile file;

        try
        {
            file = JsonUtility.FromJson<BuildingRuntimeConfigFile>(json);
        }
        catch (Exception exception)
        {
            Debug.LogError(
                $"Failed to parse building runtime config. Path : Resources/{resourcePath}, Error : {exception.Message}");
            return null;
        }

        if (file == null)
        {
            Debug.LogError(
                $"Building runtime config is empty. Path : Resources/{resourcePath}");
            return null;
        }

        if (file.buildings == null || file.buildings.Count == 0)
        {
            Debug.LogError(
                $"Building runtime config has no building entries. Path : Resources/{resourcePath}");
            return null;
        }

        var results = new List<BuildingRuntimeConfigElement>();
        var configuredTypes = new HashSet<BuildingTypeEnum>();

        for (int i = 0; i < file.buildings.Count; i++)
        {
            BuildingRuntimeConfigData data = file.buildings[i];

            if (data == null)
                return LogInvalidEntry(resourcePath, i, "entry is null");

            if (!Enum.TryParse(
                    data.buildingType,
                    true,
                    out BuildingTypeEnum buildingType) ||
                !RequiresRuntimeConfig(buildingType))
            {
                return LogInvalidEntry(
                    resourcePath,
                    i,
                    $"unsupported building type '{data.buildingType}'");
            }

            if (!configuredTypes.Add(buildingType))
            {
                return LogInvalidEntry(
                    resourcePath,
                    i,
                    $"building type '{buildingType}' is duplicated");
            }

            if (buildingType == BuildingTypeEnum.Storage)
            {
                if (data.storageCapacity <= 0)
                {
                    return LogInvalidEntry(
                        resourcePath,
                        i,
                        "storage capacity must be greater than zero");
                }
            }
            else if (data.speed <= 0f)
            {
                return LogInvalidEntry(
                    resourcePath,
                    i,
                    "speed must be greater than zero");
            }

            results.Add(new BuildingRuntimeConfigElement
            {
                buildingType = buildingType,
                speed = data.speed,
                storageCapacity = data.storageCapacity
            });
        }

        for (int i = 0; i < requiredBuildingTypes.Length; i++)
        {
            BuildingTypeEnum requiredType = requiredBuildingTypes[i];

            if (configuredTypes.Contains(requiredType))
                continue;

            Debug.LogError(
                $"Building runtime config is missing a required type. Path : Resources/{resourcePath}, Type : {requiredType}");
            return null;
        }

        return results;
    }

    public static bool RequiresRuntimeConfig(BuildingTypeEnum buildingType)
    {
        return buildingType == BuildingTypeEnum.Belt ||
            buildingType == BuildingTypeEnum.Miner ||
            buildingType == BuildingTypeEnum.Crafter ||
            buildingType == BuildingTypeEnum.Storage;
    }

    private static List<BuildingRuntimeConfigElement> LogInvalidEntry(
        string resourcePath,
        int index,
        string reason)
    {
        Debug.LogError(
            $"Invalid building runtime config entry. Path : Resources/{resourcePath}, Index : {index}, Reason : {reason}");
        return null;
    }
}
