using System;
using System.Collections.Generic;
using UnityEngine;

internal sealed class PowerConfigParseResult
{
    public readonly List<PowerPoleConfigElement> PowerPoles = new List<PowerPoleConfigElement>();
    public readonly List<PowerGeneratorConfigElement> Generators = new List<PowerGeneratorConfigElement>();
    public readonly List<PowerConsumerConfigElement> Consumers = new List<PowerConsumerConfigElement>();
    public CoalGeneratorConfig CoalGenerator;
}

internal static partial class PowerConfigParser
{
    public static PowerConfigParseResult Parse(string json, string resourcePath)
    {
        PowerConfigFile configFile;

        try
        {
            configFile = JsonUtility.FromJson<PowerConfigFile>(json);
        }
        catch (Exception exception)
        {
            Debug.LogError($"Failed to parse power config file. Path : Resources/{resourcePath}, Error : {exception.Message}");
            return null;
        }

        if (configFile == null)
        {
            Debug.LogError($"Power config file is empty or invalid. Path : Resources/{resourcePath}");
            return null;
        }

        if (configFile.powerPoles == null)
        {
            Debug.LogError($"Power pole config section is missing. Path : Resources/{resourcePath}");
            return null;
        }

        if (configFile.generators == null)
        {
            Debug.LogError($"Power generator config section is missing. Path : Resources/{resourcePath}");
            return null;
        }

        if (configFile.coalGenerator == null)
        {
            Debug.LogError($"Coal generator config section is missing. Path : Resources/{resourcePath}");
            return null;
        }

        if (configFile.consumers == null)
        {
            Debug.LogError($"Power consumer config section is missing. Path : Resources/{resourcePath}");
            return null;
        }

        if (!TryParseCoalGeneratorConfig(
                configFile.coalGenerator,
                out CoalGeneratorConfig coalGeneratorConfig))
            return null;

        PowerConfigParseResult result = new PowerConfigParseResult
        {
            CoalGenerator = coalGeneratorConfig
        };

        if (!TryParsePowerPoles(configFile.powerPoles, result.PowerPoles))
            return null;

        if (!TryParseGenerators(configFile.generators, result.Generators))
            return null;

        if (!TryParseConsumers(configFile.consumers, result.Consumers))
            return null;

        return result;
    }

    private static bool TryParseBuildingType(
        string value,
        out BuildingTypeEnum buildingType)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            Enum.TryParse(value, true, out buildingType) &&
            buildingType < BuildingTypeEnum.Count)
            return true;

        buildingType = BuildingTypeEnum.Count;
        Debug.LogError($"Invalid building type in power config. Type : {value}");
        return false;
    }

    private static bool TryParseGeneratorType(
        string value,
        out PowerGeneratorTypeEnum generatorType)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            Enum.TryParse(value, true, out generatorType) &&
            generatorType < PowerGeneratorTypeEnum.Count)
            return true;

        generatorType = PowerGeneratorTypeEnum.Count;
        Debug.LogError($"Invalid generator type in power config. Type : {value}");
        return false;
    }
}
