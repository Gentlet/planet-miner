using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

public class ResourceMapGeneratorAuthoring : MonoBehaviour
{
    [Serializable]
    private class ResourceConfigFile
    {
        public List<ResourceConfigData> configs = new List<ResourceConfigData>();
    }

    [Serializable]
    private struct ResourceConfigData
    {
        public string type;
        public float weight;
        public int minPatchRadius;
        public int maxPatchRadius;
        public float cellFillChance;
        public int minAmount;
        public int maxAmount;
    }

    [Serializable]
    public struct ResourceConfig
    {
        public ResourceTypeEnum type;
        [Range(0f, 1f)]
        public float weight;
        public int minPatchRadius;
        public int maxPatchRadius;
        [Range(0f, 1f)]
        public float cellFillChance;
        public int minAmount;
        public int maxAmount;
    }

    [SerializeField]
    private uint _worldSeed = 1;

    [SerializeField]
    private string _configResourcePath = "Config/ResourceGenerationConfig";

    [SerializeField]
    private List<ResourceConfig> _configs = new List<ResourceConfig>();

    private class Baker : Baker<ResourceMapGeneratorAuthoring>
    {
        public override void Bake(ResourceMapGeneratorAuthoring authoring)
        {
            if (!authoring.LoadConfigs())
                return;

            Entity entity = GetEntity(TransformUsageFlags.None);

            AddComponent(entity, new ResourceChunkGenerationSettings
            {
                worldSeed = authoring._worldSeed == 0 ? 1u : authoring._worldSeed
            });

            DynamicBuffer<ResourceGenerationConfigElement> configs = AddBuffer<ResourceGenerationConfigElement>(entity);

            foreach (ResourceConfig config in authoring._configs)
            {
                if (config.type <= ResourceTypeEnum.None || config.type >= ResourceTypeEnum.Count)
                    continue;

                int minRadius = Mathf.Max(0, config.minPatchRadius);
                int maxRadius = Mathf.Max(minRadius, config.maxPatchRadius);
                int minAmount = Mathf.Max(1, config.minAmount);
                int maxAmount = Mathf.Max(minAmount, config.maxAmount);

                configs.Add(new ResourceGenerationConfigElement
                {
                    type = config.type,
                    weight = Mathf.Clamp01(config.weight),
                    minPatchRadius = minRadius,
                    maxPatchRadius = maxRadius,
                    cellFillChance = Mathf.Clamp01(config.cellFillChance),
                    minAmount = minAmount,
                    maxAmount = maxAmount
                });
            }
        }
    }

    private bool LoadConfigs()
    {
        TextAsset configAsset = Resources.Load<TextAsset>(_configResourcePath);

        if (configAsset == null)
        {
            Debug.LogError($"Resource config file not found. Path : Resources/{_configResourcePath}");
            return false;
        }

        ResourceConfigFile configFile;

        try
        {
            configFile = JsonUtility.FromJson<ResourceConfigFile>(configAsset.text);
        }
        catch (Exception exception)
        {
            Debug.LogError($"Failed to parse resource config file. Path : Resources/{_configResourcePath}, Error : {exception.Message}");
            return false;
        }

        if (configFile == null || configFile.configs == null || configFile.configs.Count == 0)
        {
            Debug.LogError($"Resource config file is empty or invalid. Path : Resources/{_configResourcePath}");
            return false;
        }

        List<ResourceConfig> loadedConfigs = new List<ResourceConfig>();

        foreach (ResourceConfigData configData in configFile.configs)
        {
            if (!Enum.TryParse(configData.type, true, out ResourceTypeEnum type))
            {
                Debug.LogError($"Invalid resource type in config file. Type : {configData.type}");
                return false;
            }

            loadedConfigs.Add(new ResourceConfig
            {
                type = type,
                weight = configData.weight,
                minPatchRadius = configData.minPatchRadius,
                maxPatchRadius = configData.maxPatchRadius,
                cellFillChance = configData.cellFillChance,
                minAmount = configData.minAmount,
                maxAmount = configData.maxAmount
            });
        }

        if (loadedConfigs.Count == 0)
        {
            Debug.LogError($"Resource config file has no valid configs. Path : Resources/{_configResourcePath}");
            return false;
        }

        _configs = loadedConfigs;
        return true;
    }
}
