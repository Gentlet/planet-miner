using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PlanetMiner.Config;
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
    public int tier;
    public string displayName;
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
        JArray rawEntries;
        try
        {
            // JsonUtility는 소수/문자열 숫자를 int로 보정한다. 원본 토큰으로 크기 형식만 먼저 보존한다.
            rawEntries = JObject.Parse(json)["buildings"] as JArray;
            data = JsonUtility.FromJson<BuildingConfigJsonData>(json);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[BuildingConfigLoader] JSON parsing exception: {ex.Message}");
            return false;
        }

        if (data == null || data.buildings == null || data.buildings.Count == 0 || rawEntries == null)
        {
            Debug.LogError("[BuildingConfigLoader] Parsed buildings list is null or empty.");
            return false;
        }

        var configList = new List<BuildingConfigElement>(data.buildings.Count);
        var materialList = new List<BuildingConstructionMaterialElement>();
        var seenTypes = new HashSet<(BuildingTypeEnum Type, int Tier)>();

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
                buildingType == BuildingTypeEnum.ConstructionSite ||
                buildingType >= BuildingTypeEnum.Count)
            {
                Debug.LogError($"[BuildingConfigLoader] Invalid buildingType '{entry.buildingType}' at index {i}.");
                return false;
            }

            // 용량 및 속도 검증
            bool hasStorageSettings = buildingType == BuildingTypeEnum.Storage ||
                buildingType == BuildingTypeEnum.MainFacility ||
                buildingType == BuildingTypeEnum.DroneStation;
            if (hasStorageSettings || buildingType == BuildingTypeEnum.CoalGenerator)
            {
                if (entry.storageCapacity <= 0 || entry.storageCapacity > GameConstants.MaxStorageSlots)
                {
                    Debug.LogError($"[BuildingConfigLoader] {buildingType} has invalid storageCapacity {entry.storageCapacity} (allowed: 1 ~ {GameConstants.MaxStorageSlots}).");
                    return false;
                }
            }
            if (!hasStorageSettings && entry.speed < 0f)
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

            var rawEntry = rawEntries[i] as JObject;
            if (rawEntry == null)
            {
                Debug.LogError($"[BuildingConfigLoader] Entry at index {i} is not a building object.");
                return false;
            }
            if (!TryReadDefinition(rawEntry, buildingType, out int tier, out FixedString128Bytes displayName))
            {
                return false;
            }
            if (!seenTypes.Add((buildingType, tier)))
            {
                Debug.LogError($"[BuildingConfigLoader] Duplicate building type/tier '{buildingType}/{tier}' at index {i}.");
                return false;
            }
            if (!TryReadFootprint(buildingType, rawEntry["footprint"], out int2 footprint))
            {
                return false;
            }
            if (!BuildingPowerConfigParser.TryRead(rawEntry, buildingType, tier, out var power))
            {
                return false;
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
                tier,
                displayName,
                entry.speed,
                entry.storageCapacity,
                entry.isUnlockedByDefault,
                reqResearch,
                footprint,
                power));

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

                    materialList.Add(new BuildingConstructionMaterialElement(buildingType, tier, itemType, mat.quantity));
                }
            }
        }

        if (!ValidateDefinitions(configList, materialList, out _) || !ValidateRequiredTypes(seenTypes))
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

        var configs = new List<BuildingConfigElement>();
        foreach (var entry in entityManager.GetBuffer<BuildingConfigElement>(entity, true))
        {
            configs.Add(entry);
        }
        var materials = new List<BuildingConstructionMaterialElement>();
        foreach (var entry in entityManager.GetBuffer<BuildingConstructionMaterialElement>(entity, true))
        {
            materials.Add(entry);
        }
        if (!ValidateDefinitions(configs, materials, out var keys))
        {
            return false;
        }
        var runtimeKeys = new HashSet<(BuildingTypeEnum Type, int Tier)>();
        foreach (var entry in entityManager.GetBuffer<BuildingRuntimeConfigElement>(entity, true))
        {
            if (!keys.Contains((entry.BuildingType, entry.Tier)) || !runtimeKeys.Add((entry.BuildingType, entry.Tier)))
            {
                Debug.LogError("[BuildingConfigLoader] Pre-registered runtime configuration has an invalid or duplicate type/tier.");
                return false;
            }
        }
        if (!runtimeKeys.SetEquals(keys))
        {
            Debug.LogError("[BuildingConfigLoader] Pre-registered runtime configuration is missing a type/tier.");
            return false;
        }
        return ValidateRequiredTypes(keys);
    }

    private static bool TryReadDefinition(JObject entry, BuildingTypeEnum type, out int tier, out FixedString128Bytes displayName)
    {
        tier = 0;
        displayName = default;
        JToken tierToken = entry["tier"];
        if (tierToken == null)
        {
            Debug.LogError($"[BuildingConfigLoader] {type} is missing its required tier.");
            return false;
        }
        if (tierToken.Type != JTokenType.Integer ||
            !long.TryParse(tierToken.ToString(Formatting.None), NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) ||
            value <= 0 || value > int.MaxValue)
        {
            Debug.LogError($"[BuildingConfigLoader] {type} tier must be an explicit positive JSON integer representable as int.");
            return false;
        }
        JToken nameToken = entry["displayName"];
        if (nameToken == null)
        {
            Debug.LogError($"[BuildingConfigLoader] {type}/{value} is missing its required displayName.");
            return false;
        }
        if (nameToken.Type != JTokenType.String)
        {
            Debug.LogError($"[BuildingConfigLoader] {type}/{value} displayName must be a nonempty string.");
            return false;
        }
        string name = nameToken.Value<string>();
        if (string.IsNullOrWhiteSpace(name) || Encoding.UTF8.GetByteCount(name) > displayName.Capacity)
        {
            Debug.LogError($"[BuildingConfigLoader] {type}/{value} displayName must be nonempty and fit {displayName.Capacity} UTF-8 bytes.");
            return false;
        }
        tier = (int)value;
        displayName = new FixedString128Bytes(name);
        return true;
    }

    private static bool ValidateDefinitions(List<BuildingConfigElement> configs, List<BuildingConstructionMaterialElement> materials,
        out HashSet<(BuildingTypeEnum Type, int Tier)> keys)
    {
        keys = new HashSet<(BuildingTypeEnum Type, int Tier)>();
        foreach (var config in configs)
        {
            if (config.BuildingType <= BuildingTypeEnum.None || config.BuildingType >= BuildingTypeEnum.Count ||
                config.BuildingType == BuildingTypeEnum.ConstructionSite || config.Tier <= 0 ||
                string.IsNullOrWhiteSpace(config.DisplayName.ToString()) || !keys.Add((config.BuildingType, config.Tier)))
            {
                Debug.LogError("[BuildingConfigLoader] Invalid or duplicate building type/tier, or empty displayName.");
                return false;
            }
            if (!ValidateFootprint(config.BuildingType, config.Footprint))
            {
                return false;
            }
            if (!BuildingPowerConfigParser.Validate(config))
            {
                return false;
            }
        }
        if (materials != null)
        {
            foreach (var material in materials)
            {
                if (!keys.Contains((material.BuildingType, material.Tier)))
                {
                    Debug.LogError("[BuildingConfigLoader] Construction material references an undefined building type/tier.");
                    return false;
                }
                if (material.ItemType == ItemTypeEnum.None || !Enum.IsDefined(typeof(ItemTypeEnum), material.ItemType))
                {
                    Debug.LogError("[BuildingConfigLoader] Construction material has an invalid item type.");
                    return false;
                }
                if (material.Quantity <= 0)
                {
                    Debug.LogError("[BuildingConfigLoader] Construction material quantity must be positive.");
                    return false;
                }
            }
        }
        return true;
    }

    private static bool TryReadFootprint(BuildingTypeEnum type, JToken rawFootprint, out int2 footprint)
    {
        footprint = int2.zero;
        if (rawFootprint == null)
        {
            Debug.LogError($"[BuildingConfigLoader] {type} is missing its required footprint.");
            return false;
        }
        if (rawFootprint.Type == JTokenType.Null)
        {
            Debug.LogError($"[BuildingConfigLoader] {type} is missing its required footprint.");
            return false;
        }
        if (rawFootprint is not JArray dimensions || dimensions.Count != 2)
        {
            Debug.LogError($"[BuildingConfigLoader] {type} footprint must contain exactly two integer dimensions.");
            return false;
        }
        for (int axis = 0; axis < 2; axis++)
        {
            JToken value = dimensions[axis];
            if (value.Type != JTokenType.Integer ||
                !long.TryParse(value.ToString(Formatting.None), NumberStyles.Integer, CultureInfo.InvariantCulture, out long dimension) ||
                dimension < int.MinValue || dimension > int.MaxValue)
            {
                Debug.LogError($"[BuildingConfigLoader] {type} footprint dimensions must be JSON integers representable as int.");
                return false;
            }
            footprint[axis] = (int)dimension;
        }
        return ValidateFootprint(type, footprint);
    }

    private static bool ValidateFootprint(BuildingTypeEnum type, int2 footprint)
    {
        if (footprint.x <= 0 || footprint.y <= 0)
        {
            Debug.LogError($"[BuildingConfigLoader] {type} has invalid footprint. Both dimensions must be positive.");
            return false;
        }
        return true;
    }

    private static bool ValidateRequiredTypes(HashSet<(BuildingTypeEnum Type, int Tier)> types)
    {
        var missing = new List<BuildingTypeEnum>();
        foreach (BuildingTypeEnum type in Enum.GetValues(typeof(BuildingTypeEnum)))
        {
            if (type == BuildingTypeEnum.None || type == BuildingTypeEnum.Count || type == BuildingTypeEnum.ConstructionSite)
            {
                continue;
            }
            if (!types.Contains((type, 1)))
            {
                missing.Add(type);
            }
        }
        if (missing.Count == 0)
        {
            return true;
        }
        Debug.LogError($"[BuildingConfigLoader] Missing required tier 1 building types: {string.Join(", ", missing)}.");
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

        // 공개 게시와 사전 등록 Init도 JSON과 같은 크기 계약을 따른다. 무효 입력은 부분 게시하지 않는다.
        if (!ValidateDefinitions(configs, materials, out _))
        {
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
                runtimeBuffer.Add(new BuildingRuntimeConfigElement(configs[i].BuildingType, configs[i].Tier, configs[i].Speed, configs[i].StorageCapacity));
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
