using System;
using System.Collections.Generic;
using UnityEngine;

internal static class ConstructionConfigParser
{
    public static List<ConstructionMaterialConfigElement> Parse(
        string json,
        string resourcePath)
    {
        ConstructionConfigFile file;

        try
        {
            file = JsonUtility.FromJson<ConstructionConfigFile>(json);
        }
        catch (Exception exception)
        {
            Debug.LogError(
                $"Failed to parse construction config. Path : Resources/{resourcePath}, Error : {exception.Message}");
            return null;
        }

        if (file == null)
        {
            Debug.LogError(
                $"Construction config is empty. Path : Resources/{resourcePath}");
            return null;
        }

        if (file.materials == null)
        {
            Debug.LogError(
                $"Construction config has no material list. Path : Resources/{resourcePath}");
            return null;
        }

        if (file.materials.Count == 0)
        {
            Debug.LogError(
                $"Construction config has no material entries. Path : Resources/{resourcePath}");
            return null;
        }

        List<ConstructionMaterialConfigElement> results = new();
        HashSet<(BuildingTypeEnum, ItemTypeEnum)> entries = new();

        for (int i = 0; i < file.materials.Count; i++)
        {
            ConstructionMaterialConfigData data = file.materials[i];

            if (data == null)
                return LogInvalidEntry(resourcePath, i, "entry is null");

            if (!Enum.TryParse(
                    data.buildingType,
                    true,
                    out BuildingTypeEnum buildingType))
                return LogInvalidEntry(resourcePath, i, $"invalid building type '{data.buildingType}'");

            if (buildingType >= BuildingTypeEnum.Count)
                return LogInvalidEntry(resourcePath, i, $"invalid building type '{data.buildingType}'");

            if (buildingType == BuildingTypeEnum.MainFacility)
                return LogInvalidEntry(resourcePath, i, "main facility cannot be player-constructed");

            if (!Enum.TryParse(
                    data.itemType,
                    true,
                    out ItemTypeEnum itemType))
                return LogInvalidEntry(resourcePath, i, $"invalid item type '{data.itemType}'");

            if (!itemType.IsValid())
                return LogInvalidEntry(resourcePath, i, $"invalid item type '{data.itemType}'");

            if (data.quantity <= 0)
                return LogInvalidEntry(resourcePath, i, "quantity must be greater than zero");

            if (!entries.Add((buildingType, itemType)))
                return LogInvalidEntry(resourcePath, i, "building and item pair is duplicated");

            results.Add(new ConstructionMaterialConfigElement
            {
                buildingType = buildingType,
                itemType = itemType,
                quantity = data.quantity
            });
        }

        return results;
    }

    private static List<ConstructionMaterialConfigElement> LogInvalidEntry(
        string resourcePath,
        int index,
        string reason)
    {
        Debug.LogError(
            $"Invalid construction material entry. Path : Resources/{resourcePath}, Index : {index}, Reason : {reason}");
        return null;
    }
}
