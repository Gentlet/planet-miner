using System;
using System.Collections.Generic;
using UnityEngine;

internal static class StartingItemConfigParser
{
    public static StartingItemConfigElement[] Parse(
        string json,
        string resourcePath)
    {
        StartingItemConfigFile configFile;

        try
        {
            configFile = JsonUtility.FromJson<StartingItemConfigFile>(json);
        }
        catch (Exception exception)
        {
            Debug.LogError(
                $"Failed to parse starting item config file. Path : Resources/{resourcePath}, Error : {exception.Message}");
            return null;
        }

        if (configFile == null)
        {
            Debug.LogError(
                $"Starting item config file is empty or invalid. Path : Resources/{resourcePath}");
            return null;
        }

        if (configFile.items == null)
        {
            Debug.LogError(
                $"Starting item config contains no item list. Path : Resources/{resourcePath}");
            return null;
        }

        var itemConfigs = new List<StartingItemConfigElement>(
            configFile.items.Length);
        var configuredItemTypes = new HashSet<ItemTypeEnum>();

        for (int i = 0; i < configFile.items.Length; i++)
        {
            StartingItemConfigEntryFile itemConfig = configFile.items[i];

            if (itemConfig == null)
            {
                Debug.LogError(
                    $"Starting item config contains a null entry. Index : {i}");
                return null;
            }

            if (!Enum.TryParse(
                    itemConfig.itemType,
                    out ItemTypeEnum itemType) ||
                !itemType.IsValid())
            {
                Debug.LogError(
                    $"Starting item config contains an invalid item type. Index : {i}, Value : {itemConfig.itemType}");
                return null;
            }

            if (itemConfig.quantity < 0)
            {
                Debug.LogError(
                    $"Starting item config contains a negative quantity. Item : {itemType}, Quantity : {itemConfig.quantity}");
                return null;
            }

            if (!configuredItemTypes.Add(itemType))
            {
                Debug.LogError(
                    $"Starting item config contains a duplicate item type. Item : {itemType}");
                return null;
            }

            itemConfigs.Add(new StartingItemConfigElement
            {
                itemType = itemType,
                quantity = itemConfig.quantity
            });
        }

        return itemConfigs.ToArray();
    }
}
