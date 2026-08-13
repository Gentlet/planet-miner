using System.Collections.Generic;
using UnityEngine;

internal static partial class PowerConfigParser
{
    private static bool TryParseGenerators(
        List<PowerGeneratorConfigData> configData,
        List<PowerGeneratorConfigElement> result)
    {
        HashSet<PowerGeneratorTypeEnum> usedGeneratorTypes = new HashSet<PowerGeneratorTypeEnum>();

        for (int i = 0; i < configData.Count; i++)
        {
            PowerGeneratorConfigData generatorData = configData[i];

            if (generatorData == null)
            {
                Debug.LogError($"Null power generator config. Index : {i}");
                return false;
            }

            if (!TryParseGenerator(generatorData, out PowerGeneratorConfigElement generator))
                return false;

            if (!usedGeneratorTypes.Add(generator.generatorType))
            {
                Debug.LogError($"Duplicated power generator config. Type : {generator.generatorType}");
                return false;
            }

            result.Add(generator);
        }

        if (!usedGeneratorTypes.Contains(PowerGeneratorTypeEnum.MainFacility))
        {
            Debug.LogError("Required power generator config was not found. Type : MainFacility");
            return false;
        }

        if (!usedGeneratorTypes.Contains(PowerGeneratorTypeEnum.CoalGenerator))
        {
            Debug.LogError("Required power generator config was not found. Type : CoalGenerator");
            return false;
        }

        return true;
    }

    private static bool TryParseGenerator(
        PowerGeneratorConfigData generatorData,
        out PowerGeneratorConfigElement generator)
    {
        generator = default;

        if (!TryParseGeneratorType(
                generatorData.generatorType,
                out PowerGeneratorTypeEnum generatorType))
            return false;

        if (generatorData.maximumGeneration < 0f)
        {
            Debug.LogError(
                $"Invalid maximum power generation. Type : {generatorData.generatorType}, " +
                $"Value : {generatorData.maximumGeneration}");
            return false;
        }

        generator = new PowerGeneratorConfigElement
        {
            generatorType = generatorType,
            maximumGeneration = generatorData.maximumGeneration
        };
        return true;
    }

    private static bool TryParseCoalGeneratorConfig(
        CoalGeneratorConfigData configData,
        out CoalGeneratorConfig config)
    {
        config = default;

        if (configData.coalEnergyPerItem <= 0f)
        {
            Debug.LogError($"Invalid coal energy per item. Value : {configData.coalEnergyPerItem}");
            return false;
        }

        if (configData.fuelStorageCapacity <= 0)
        {
            Debug.LogError(
                $"Invalid coal generator fuel storage capacity. Value : {configData.fuelStorageCapacity}");
            return false;
        }

        config = new CoalGeneratorConfig
        {
            coalEnergyPerItem = configData.coalEnergyPerItem,
            fuelStorageCapacity = configData.fuelStorageCapacity
        };
        return true;
    }
}
