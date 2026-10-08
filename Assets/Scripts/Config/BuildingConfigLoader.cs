using System;
using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// 건물 JSON의 최상위 목록 DTO. Resources 파싱 시에만 쓰며 ECS에 부착하거나 런타임 설정 원본으로 유지하지 않는다.
/// </summary>
[Serializable]
public class BuildingConfigJsonData
{
    public List<BuildingConfigJsonEntry> buildings = new List<BuildingConfigJsonEntry>();
}

/// <summary>건물 한 종류의 JSON 입력 행. 속도/용량/해금/기본 크기와 비용 목록은 검증 후 ECS 버퍼 값으로 변환한다.</summary>
[Serializable]
public class BuildingConfigJsonEntry
{
    public string buildingType;
    public float speed;
    public int storageCapacity;
    public bool isUnlockedByDefault = true;
    public string requiredResearch;
    public int[] footprint;
    public List<BuildingMaterialJsonEntry> materials = new List<BuildingMaterialJsonEntry>();
}

/// <summary>건축 비용의 품목 문자열/수량 DTO. 실제 현장의 요구량이나 도착 실물 상태가 아니다.</summary>
[Serializable]
public class BuildingMaterialJsonEntry
{
    public string itemType;
    public int quantity = 1;
}

/// <summary>
/// 역할·목적: Resources의 건물 JSON을 파싱/검증하고 통합 설정·건축 비용·호환 런타임 버퍼로 게시한다.
/// 입력·출력: 유효하지 않은 입력은 false/Null로 거부한다. PublishConfig는 제공된 검증 결과로 새 ECS 엔티티를 만든다.
/// 이용: BuildingConfigInitSystem(Initialization)과 설정 테스트가 호출한다. 초기화 시점/중복 게시 관리는 호출자가 소유한다.
/// 수명·경계: JSON DTO/managed 목록은 게시 전 입력이며 게임 실행 Reader는 ECS 버퍼를 사용한다. 해금의 연구 Writer는 별도 후속이다.
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

        try
        {
            var textAsset = Resources.Load<TextAsset>(resourcePath);
            if (textAsset == null)
            {
                Debug.LogError($"[BuildingConfigLoader] Failed to load config TextAsset at Resources path: '{resourcePath}'.");
                return false;
            }

            return TryParseJson(textAsset.text, out configs, out materials);
        }
        catch (Exception exception)
        {
            configs = null;
            materials = null;
            Debug.LogError($"[BuildingConfigLoader] Failed to read config at '{resourcePath}': {exception.Message}");
            return false;
        }
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
            if (entry == null)
            {
                Debug.LogError($"[BuildingConfigLoader] Entry at index {i} is null.");
                return false;
            }

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
            if (buildingType == BuildingTypeEnum.Storage ||
                buildingType == BuildingTypeEnum.MainFacility ||
                buildingType == BuildingTypeEnum.DroneStation)
            {
                if (entry.storageCapacity <= 0 || entry.storageCapacity > GameConstants.MaxStorageSlots)
                {
                    Debug.LogError($"[BuildingConfigLoader] {buildingType} has invalid storageCapacity {entry.storageCapacity} (allowed: 1 ~ {GameConstants.MaxStorageSlots}).");
                    return false;
                }
            }
            else if (entry.speed < 0f)
            {
                Debug.LogError($"[BuildingConfigLoader] {buildingType} has invalid speed {entry.speed} (cannot be negative).");
                return false;
            }

            if (buildingType == BuildingTypeEnum.Belt)
            {
                if (!math.isfinite(entry.speed) || entry.speed > GameConstants.MaxBeltSpeed)
                {
                    Debug.LogError($"[BuildingConfigLoader] Belt has invalid speed {entry.speed} (must be finite and between 0 and {GameConstants.MaxBeltSpeed}).");
                    return false;
                }
            }

            // Footprint 검증
            int2 footprint = new int2(1, 1);
            if (entry.footprint != null)
            {
                if (entry.footprint.Length != 2 || entry.footprint[0] < 1 || entry.footprint[1] < 1)
                {
                    Debug.LogError($"[BuildingConfigLoader] {buildingType} has invalid footprint. Dimensions must be 2 positive integers (width, height >= 1).");
                    return false;
                }
                footprint = new int2(entry.footprint[0], entry.footprint[1]);
            }

            FixedString32Bytes reqResearch = default;
            if (!string.IsNullOrEmpty(entry.requiredResearch))
            {
                if (Encoding.UTF8.GetByteCount(entry.requiredResearch) > reqResearch.Capacity)
                {
                    Debug.LogError($"[BuildingConfigLoader] {buildingType} requiredResearch exceeds {reqResearch.Capacity} UTF-8 bytes at index {i}.");
                    return false;
                }

                reqResearch = new FixedString32Bytes(entry.requiredResearch);
            }

            configList.Add(new BuildingConfigElement(
                buildingType,
                entry.speed,
                entry.storageCapacity,
                entry.isUnlockedByDefault,
                reqResearch,
                footprint));

            // 건설 자재 검증
            if (entry.materials != null)
            {
                for (int m = 0; m < entry.materials.Count; m++)
                {
                    var mat = entry.materials[m];
                    if (mat == null)
                    {
                        Debug.LogError($"[BuildingConfigLoader] Material at index {m} for {buildingType} is null.");
                        return false;
                    }

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

        if (!ValidateRequiredTypes(seenTypes))
        {
            return false;
        }

        configs = configList;
        materials = materialList;
        return true;
    }

    /// <summary>제품 초기화의 사전 등록도 전체 건물 종류와 게시 버퍼를 요구한다. 격리 테스트는 Init을 실행하지 않는다.</summary>
    public static bool ValidatePublishedConfig(EntityManager entityManager, Entity entity)
    {
        if (!entityManager.HasBuffer<BuildingConfigElement>(entity) ||
            !entityManager.HasBuffer<BuildingConstructionMaterialElement>(entity) ||
            !entityManager.HasBuffer<BuildingRuntimeConfigElement>(entity))
        {
            Debug.LogError("[BuildingConfigLoader] Pre-registered BuildingConfig is missing its configuration buffers.");
            return false;
        }

        var types = new HashSet<BuildingTypeEnum>();
        foreach (var entry in entityManager.GetBuffer<BuildingConfigElement>(entity, true))
        {
            types.Add(entry.BuildingType);
        }
        return ValidateRequiredTypes(types);
    }

    private static bool ValidateRequiredTypes(HashSet<BuildingTypeEnum> types)
    {
        var missing = new List<BuildingTypeEnum>();
        foreach (BuildingTypeEnum type in Enum.GetValues(typeof(BuildingTypeEnum)))
        {
            if (type == BuildingTypeEnum.None || type == BuildingTypeEnum.Count || type == BuildingTypeEnum.ConstructionSite)
            {
                continue;
            }
            if (!types.Contains(type))
            {
                missing.Add(type);
            }
        }
        if (missing.Count == 0)
        {
            return true;
        }
        Debug.LogError($"[BuildingConfigLoader] Missing required building types: {string.Join(", ", missing)}.");
        return false;
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

        // 통합 설정과 호환 버퍼를 같은 입력에서 게시한다. 이후 게임 Reader가 managed JSON 목록을 원본으로 다시 읽지 않게 한다.
        var entity = entityManager.CreateEntity(
            typeof(BuildingConfig),
            typeof(BuildingRuntimeConfig),
            typeof(BuildingConfigElement),
            typeof(BuildingRuntimeConfigElement),
            typeof(BuildingConstructionMaterialElement));

        try
        {
            var configBuffer = entityManager.GetBuffer<BuildingConfigElement>(entity);
            var runtimeBuffer = entityManager.GetBuffer<BuildingRuntimeConfigElement>(entity);
            var materialBuffer = entityManager.GetBuffer<BuildingConstructionMaterialElement>(entity);

            for (int i = 0; i < configs.Count; i++)
            {
                configBuffer.Add(configs[i]);
                runtimeBuffer.Add(new BuildingRuntimeConfigElement(configs[i].BuildingType, configs[i].Speed, configs[i].StorageCapacity));
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
        catch
        {
            entityManager.DestroyEntity(entity);
            throw;
        }
    }
}
