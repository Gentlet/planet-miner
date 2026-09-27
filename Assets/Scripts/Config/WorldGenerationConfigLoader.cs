using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

/// <summary>
/// WorldGenerationConfig.json 파일을 로드하고 유효성을 전수 검증하여 ECS 월드에 설정을 게시하는 정적 로더 유틸리티.
/// </summary>
public static class WorldGenerationConfigLoader
{
    public const string DefaultResourcePath = "Config/WorldGenerationConfig";

    [Serializable]
    public class WorldGenerationConfigFile
    {
        public uint worldSeed;
        public int initialChunkSize;
        public List<ResourceGenerationConfigData> configs;
    }

    [Serializable]
    public class ResourceGenerationConfigData
    {
        public string type;
        public float weight;
        public int minPatchRadius;
        public int maxPatchRadius;
        public float cellFillChance;
        public int minAmount;
        public int maxAmount;
    }

    /// <summary>
    /// Resources 경로에서 JSON 설정을 로드하고 전수 검증을 통과한 설정을 반환합니다.
    /// 유효성 검증 실패 시 설정을 반환하지 않으며 에러 로그를 남깁니다 (All-or-Nothing Fail-Fast).
    /// </summary>
    public static bool TryLoadConfigFromResources(
        string resourcePath,
        out uint worldSeed,
        out List<ResourceGenerationConfigElement> elements)
    {
        return TryLoadConfigFromResources(resourcePath, out worldSeed, out _, out elements);
    }

    /// <summary>
    /// Resources 경로에서 JSON 설정을 로드하고 initialChunkSize를 포함하여 전수 검증을 통과한 설정을 반환합니다.
    /// </summary>
    public static bool TryLoadConfigFromResources(
        string resourcePath,
        out uint worldSeed,
        out int initialChunkSize,
        out List<ResourceGenerationConfigElement> elements)
    {
        worldSeed = 0;
        initialChunkSize = 0;
        elements = null;

        TextAsset asset = Resources.Load<TextAsset>(resourcePath);
        if (asset == null)
        {
            Debug.LogError($"[WorldGenerationConfigLoader] Config asset not found at Resources/{resourcePath}");
            return false;
        }

        return TryParseAndValidateJson(asset.text, out worldSeed, out initialChunkSize, out elements);
    }

    /// <summary>
    /// JSON 문자열을 파싱하고 유효성을 전수 검증합니다.
    /// </summary>
    public static bool TryParseAndValidateJson(
        string jsonText,
        out uint worldSeed,
        out List<ResourceGenerationConfigElement> elements)
    {
        return TryParseAndValidateJson(jsonText, out worldSeed, out _, out elements);
    }

    /// <summary>
    /// JSON 문자열을 파싱하고 initialChunkSize를 포함하여 유효성을 전수 검증합니다.
    /// </summary>
    public static bool TryParseAndValidateJson(
        string jsonText,
        out uint worldSeed,
        out int initialChunkSize,
        out List<ResourceGenerationConfigElement> elements)
    {
        worldSeed = 0;
        initialChunkSize = 0;
        elements = null;

        if (string.IsNullOrEmpty(jsonText))
        {
            Debug.LogError("[WorldGenerationConfigLoader] Config json text is empty.");
            return false;
        }

        WorldGenerationConfigFile configFile;
        try
        {
            configFile = JsonUtility.FromJson<WorldGenerationConfigFile>(jsonText);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[WorldGenerationConfigLoader] Failed to deserialize json: {ex.Message}");
            return false;
        }

        if (configFile == null)
        {
            Debug.LogError("[WorldGenerationConfigLoader] Deserialized config file object is null.");
            return false;
        }

        if (configFile.worldSeed == 0)
        {
            Debug.LogError("[WorldGenerationConfigLoader] WorldSeed cannot be 0. WorldSeed must be a positive integer >= 1.");
            return false;
        }

        if (configFile.initialChunkSize < 0)
        {
            Debug.LogError("[WorldGenerationConfigLoader] initialChunkSize cannot be negative.");
            return false;
        }

        if (configFile.configs == null || configFile.configs.Count == 0)
        {
            Debug.LogError("[WorldGenerationConfigLoader] Configs list is null or empty.");
            return false;
        }

        var validatedList = new List<ResourceGenerationConfigElement>(configFile.configs.Count);
        var seenTypes = new HashSet<ItemTypeEnum>();

        foreach (var entry in configFile.configs)
        {
            if (entry == null)
            {
                Debug.LogError("[WorldGenerationConfigLoader] Config entry is null.");
                return false;
            }

            if (string.IsNullOrEmpty(entry.type) ||
                !Enum.TryParse<ItemTypeEnum>(entry.type, true, out var type) ||
                !Enum.IsDefined(typeof(ItemTypeEnum), type) || type == ItemTypeEnum.None)
            {
                Debug.LogError($"[WorldGenerationConfigLoader] Invalid or unrecognized resource type: '{entry.type}'.");
                return false;
            }

            if (!seenTypes.Add(type))
            {
                Debug.LogError($"[WorldGenerationConfigLoader] Duplicate config definition for resource type: '{type}'.");
                return false;
            }

            if (entry.weight < 0f || entry.weight > 1f)
            {
                Debug.LogError($"[WorldGenerationConfigLoader] Invalid weight for type '{type}': {entry.weight}. Must be between 0.0 and 1.0.");
                return false;
            }

            if (entry.minPatchRadius < 0 || entry.maxPatchRadius < entry.minPatchRadius)
            {
                Debug.LogError($"[WorldGenerationConfigLoader] Invalid patch radius for type '{type}': min={entry.minPatchRadius}, max={entry.maxPatchRadius}. Must satisfy 0 <= min <= max.");
                return false;
            }

            if (entry.cellFillChance < 0f || entry.cellFillChance > 1f)
            {
                Debug.LogError($"[WorldGenerationConfigLoader] Invalid cellFillChance for type '{type}': {entry.cellFillChance}. Must be between 0.0 and 1.0.");
                return false;
            }

            if (entry.minAmount <= 0 || entry.maxAmount < entry.minAmount || entry.maxAmount >= int.MaxValue)
            {
                Debug.LogError($"[WorldGenerationConfigLoader] Invalid amount for type '{type}': min={entry.minAmount}, max={entry.maxAmount}. Must satisfy 1 <= min <= max < {int.MaxValue}.");
                return false;
            }

            validatedList.Add(new ResourceGenerationConfigElement(
                type,
                entry.weight,
                entry.minPatchRadius,
                entry.maxPatchRadius,
                entry.cellFillChance,
                entry.minAmount,
                entry.maxAmount));
        }

        worldSeed = configFile.worldSeed;
        initialChunkSize = configFile.initialChunkSize > 0 ? configFile.initialChunkSize : 3;
        elements = validatedList;
        return true;
    }

    /// <summary>
    /// 검증된 월드 생성 설정을 ECS 싱글톤 엔티티와 DynamicBuffer로 게시합니다.
    /// </summary>
    public static Entity PublishConfig(
        EntityManager entityManager,
        uint worldSeed,
        List<ResourceGenerationConfigElement> elements)
    {
        return PublishConfig(entityManager, worldSeed, 3, elements);
    }

    /// <summary>
    /// 검증된 월드 생성 설정(initialChunkSize 포함)을 ECS 싱글톤 엔티티와 DynamicBuffer로 게시합니다.
    /// </summary>
    public static Entity PublishConfig(
        EntityManager entityManager,
        uint worldSeed,
        int initialChunkSize,
        List<ResourceGenerationConfigElement> elements)
    {
        Entity entity = entityManager.CreateEntity(typeof(ResourceGenerationSettings));
        entityManager.SetComponentData(entity, new ResourceGenerationSettings(worldSeed, initialChunkSize));

        DynamicBuffer<ResourceGenerationConfigElement> buffer = entityManager.AddBuffer<ResourceGenerationConfigElement>(entity);
        for (int i = 0; i < elements.Count; i++)
        {
            buffer.Add(elements[i]);
        }

        return entity;
    }
}
