using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

/// <summary>
/// BuildingConfig JSON 직렬화를 위한 DTO 클래스들.
/// </summary>
[Serializable]
public class BuildingConfigJsonData
{
    public List<BuildingConfigJsonEntry> buildings = new List<BuildingConfigJsonEntry>();
}

[Serializable]
public class BuildingConfigJsonEntry
{
    public string buildingType;
    public float speed;
    public int storageCapacity;
    public bool isUnlockedByDefault = true;
    public string requiredResearch;
    public List<BuildingMaterialJsonEntry> materials = new List<BuildingMaterialJsonEntry>();
}

[Serializable]
public class BuildingMaterialJsonEntry
{
    public string itemType;
    public int quantity = 1;
}

/// <summary>
/// BuildingConfig JSON을 로드, 파싱, 검증하고 ECS 월드에 게시하는 정적 유틸리티.
/// </summary>
public static class BuildingConfigLoader
{
    public const string DefaultResourcePath = "Config/BuildingConfig";

    public static bool TryLoadConfigFromResources(
        string resourcePath,
        out List<BuildingConfigElement> configs,
        out List<BuildingConstructionMaterialElement> materials)
    {
        configs = null;
        materials = null;

        var textAsset = Resources.Load<TextAsset>(resourcePath);
        if (textAsset == null)
        {
            Debug.LogError($"[BuildingConfigLoader] Failed to load config TextAsset at Resources path: '{resourcePath}'.");
            return false;
        }

        return TryParseJson(textAsset.text, out configs, out materials);
    }

    public static bool TryParseJson(
        string json,
        out List<BuildingConfigElement> configs,
        out List<BuildingConstructionMaterialElement> materials)
    {
        configs = null;
        materials = null;

        if (string.IsNullOrEmpty(json))
        {
            Debug.LogError("[BuildingConfigLoader] JSON string is null or empty.");
            return false;
        }

        BuildingConfigJsonData data;
        try
        {
            data = JsonUtility.FromJson<BuildingConfigJsonData>(json);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[BuildingConfigLoader] JSON parsing exception: {ex.Message}");
            return false;
        }

        if (data == null || data.buildings == null || data.buildings.Count == 0)
        {
            Debug.LogError("[BuildingConfigLoader] Parsed buildings list is null or empty.");
            return false;
        }

        var configList = new List<BuildingConfigElement>(data.buildings.Count);
        var materialList = new List<BuildingConstructionMaterialElement>();
        var seenTypes = new HashSet<BuildingTypeEnum>();

        for (int i = 0; i < data.buildings.Count; i++)
        {
            var entry = data.buildings[i];
            if (string.IsNullOrEmpty(entry.buildingType))
            {
                Debug.LogError($"[BuildingConfigLoader] Entry at index {i} has missing or empty buildingType.");
                return false;
            }

            if (!Enum.TryParse<BuildingTypeEnum>(entry.buildingType, true, out var buildingType) ||
                buildingType == BuildingTypeEnum.None ||
                buildingType >= BuildingTypeEnum.Count)
            {
                Debug.LogError($"[BuildingConfigLoader] Invalid buildingType '{entry.buildingType}' at index {i}.");
                return false;
            }

            if (!seenTypes.Add(buildingType))
            {
                Debug.LogError($"[BuildingConfigLoader] Duplicate buildingType '{buildingType}' detected at index {i}.");
                return false;
            }

            // 용량 및 속도 검증
            if (buildingType == BuildingTypeEnum.Storage)
            {
                if (entry.storageCapacity <= 0 || entry.storageCapacity > GameConstants.MaxStorageSlots)
                {
                    Debug.LogError($"[BuildingConfigLoader] Storage has invalid storageCapacity {entry.storageCapacity} (allowed: 1 ~ {GameConstants.MaxStorageSlots}).");
                    return false;
                }
            }
            else if (entry.speed < 0f)
            {
                Debug.LogError($"[BuildingConfigLoader] {buildingType} has invalid speed {entry.speed} (cannot be negative).");
                return false;
            }

            FixedString32Bytes reqResearch = default;
            if (!string.IsNullOrEmpty(entry.requiredResearch))
            {
                reqResearch = new FixedString32Bytes(entry.requiredResearch);
            }

            configList.Add(new BuildingConfigElement(
                buildingType,
                entry.speed,
                entry.storageCapacity,
                entry.isUnlockedByDefault,
                reqResearch));

            // 건설 자재 검증
            if (entry.materials != null)
            {
                for (int m = 0; m < entry.materials.Count; m++)
                {
                    var mat = entry.materials[m];
                    if (!Enum.TryParse<ItemTypeEnum>(mat.itemType, true, out var itemType) ||
                        !Enum.IsDefined(typeof(ItemTypeEnum), itemType) ||
                        itemType == ItemTypeEnum.None)
                    {
                        Debug.LogError($"[BuildingConfigLoader] Invalid itemType '{mat.itemType}' for building {buildingType}.");
                        return false;
                    }

                    if (mat.quantity <= 0)
                    {
                        Debug.LogError($"[BuildingConfigLoader] Invalid quantity {mat.quantity} for material {itemType} in building {buildingType}.");
                        return false;
                    }

                    materialList.Add(new BuildingConstructionMaterialElement(buildingType, itemType, mat.quantity));
                }
            }
        }

        configs = configList;
        materials = materialList;
        return true;
    }

    /// <summary>
    /// 검증된 설정 엘리먼트들을 ECS 월드의 BuildingConfig 싱글톤 엔티티로 게시.
    /// (기존 BuildingRuntimeConfig 호환성 버퍼도 함께 부착)
    /// </summary>
    public static Entity PublishConfig(
        EntityManager entityManager,
        List<BuildingConfigElement> configs,
        List<BuildingConstructionMaterialElement> materials)
    {
        if (configs == null || configs.Count == 0)
        {
            Debug.LogError("[BuildingConfigLoader] Cannot publish null or empty config elements.");
            return Entity.Null;
        }

        var entity = entityManager.CreateEntity(
            typeof(BuildingConfig),
            typeof(BuildingRuntimeConfig),
            typeof(BuildingConfigElement),
            typeof(BuildingRuntimeConfigElement),
            typeof(BuildingConstructionMaterialElement));

        var configBuffer = entityManager.GetBuffer<BuildingConfigElement>(entity);
        var runtimeBuffer = entityManager.GetBuffer<BuildingRuntimeConfigElement>(entity);
        var materialBuffer = entityManager.GetBuffer<BuildingConstructionMaterialElement>(entity);

        for (int i = 0; i < configs.Count; i++)
        {
            configBuffer.Add(configs[i]);
            runtimeBuffer.Add(new BuildingRuntimeConfigElement(
                configs[i].BuildingType,
                configs[i].Speed,
                configs[i].StorageCapacity));
        }

        if (materials != null)
        {
            for (int i = 0; i < materials.Count; i++)
            {
                materialBuffer.Add(materials[i]);
            }
        }

        return entity;
    }
}
