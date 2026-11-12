using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public static class BuildingConfigLoader
{
    public const string DefaultResourcePath = "Config/" + BuildingResourceName;
    public const string DefaultMaterialsResourcePath = "Config/" + MaterialsResourceName;
    private const string BuildingResourceName = "BuildingDefinitions";
    private const string MaterialsResourceName = "BuildingConstructionMaterials";

    /// <summary>생성된 건물·자재 리소스를 함께 읽는다. 별도 경로의 내장 자재는 해당 정의의 건물 ID로 연결한다.</summary>
    public static bool TryLoadConfigFromResources(
        string resourcePath,
        out List<BuildingConfigElement> configs,
        out List<BuildingConstructionMaterialElement> materials, IReadOnlyDictionary<int, ItemTypeEnum> itemIds = null)
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

            var table = ReadBuildingDefinitions(textAsset.text);
            if (!IsStandaloneResource(resourcePath, table))
            {
                string materialPath = GetMaterialsResourcePath(resourcePath);
                var materialAsset = Resources.Load<TextAsset>(materialPath);
                if (materialAsset == null)
                {
                    Debug.LogError($"[BuildingConfigLoader] Failed to load config TextAsset at Resources path: '{materialPath}'.");
                    return false;
                }
                AttachConstructionMaterials(table, materialAsset.text);
            }

            bool requireMaterialItemIds = string.Equals(resourcePath, DefaultResourcePath, StringComparison.OrdinalIgnoreCase);
            DecodeTable(table, out configs, out materials, itemIds, requireMaterialItemIds);
            return true;
        }
        catch (Exception exception)
        {
            configs = null;
            materials = null;
            Debug.LogError($"[BuildingConfigLoader] Failed to read config at '{resourcePath}': {exception.Message}");
            return false;
        }
    }

    private static bool IsStandaloneResource(string resourcePath, BuildingDefinitions table)
    {
        if (string.Equals(resourcePath, DefaultResourcePath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return table.ConstructionMaterials != null || table.Buildings.Exists(row => row?.Materials != null);
    }

    private static string GetMaterialsResourcePath(string buildingPath)
    {
        int folderLength = buildingPath.LastIndexOf('/') + 1;
        string buildingName = buildingPath.Substring(folderLength);
        string materialName = string.Equals(buildingName, BuildingResourceName, StringComparison.OrdinalIgnoreCase)
            ? MaterialsResourceName
            : buildingName + "." + MaterialsResourceName;
        return buildingPath.Substring(0, folderLength) + materialName;
    }

    /// <summary>기존 단일 건물 JSON 입력을 해석한다.</summary>
    public static bool TryParseJson(
        string json,
        out List<BuildingConfigElement> configs,
        out List<BuildingConstructionMaterialElement> materials, IReadOnlyDictionary<int, ItemTypeEnum> itemIds = null)
    {
        return TryDecodeJson(json, null, out configs, out materials, itemIds);
    }

    public static bool TryParseJson(
        string buildingJson,
        string materialJson,
        out List<BuildingConfigElement> configs,
        out List<BuildingConstructionMaterialElement> materials, IReadOnlyDictionary<int, ItemTypeEnum> itemIds = null)
    {
        if (materialJson == null)
        {
            configs = null;
            materials = null;
            Debug.LogError("[BuildingConfigLoader] Missing BuildingConstructionMaterials JSON for the generated building table.");
            return false;
        }
        return TryDecodeJson(buildingJson, materialJson, out configs, out materials, itemIds);
    }

    private static bool TryDecodeJson(
        string buildingJson,
        string materialJson,
        out List<BuildingConfigElement> configs,
        out List<BuildingConstructionMaterialElement> materials, IReadOnlyDictionary<int, ItemTypeEnum> itemIds = null)
    {
        configs = null;
        materials = null;
        try
        {
            var table = ReadBuildingDefinitions(buildingJson);
            if (materialJson != null)
            {
                AttachConstructionMaterials(table, materialJson);
            }
            DecodeTable(table, out var decodedConfigs, out var decodedMaterials, itemIds);
            configs = decodedConfigs;
            materials = decodedMaterials;
            return true;
        }
        catch (Exception error) when (error is JsonException or ArgumentException)
        {
            Debug.LogError($"[BuildingConfigLoader] Failed to decode building JSON: {error.Message}");
            return false;
        }
    }

    private static BuildingDefinitions ReadBuildingDefinitions(string json)
    {
        var table = JsonConvert.DeserializeObject<BuildingDefinitions>(json, new StringEnumConverter { AllowIntegerValues = false });
        if (table == null)
        {
            throw new JsonSerializationException("Missing building table.");
        }
        if (table.LegacyBuildings != null)
        {
            if (table.Buildings != null)
            {
                throw new JsonSerializationException("Use one building definitions array: buildingDefinitions or legacy buildings.");
            }
            table.Buildings = table.LegacyBuildings;
        }
        if (table.Buildings == null)
        {
            throw new JsonSerializationException("Missing buildingDefinitions array.");
        }
        if (table.Buildings.Count == 0)
        {
            throw new JsonSerializationException("Empty buildingDefinitions array.");
        }
        return table;
    }

    private static void AttachConstructionMaterials(BuildingDefinitions buildings, string json)
    {
        var materials = JsonConvert.DeserializeObject<ConstructionMaterialTable>(json, new StringEnumConverter { AllowIntegerValues = false });
        if (materials == null)
        {
            throw new JsonSerializationException("Missing buildingConstructionMaterials table.");
        }
        if (buildings.ConstructionMaterials != null)
        {
            throw new JsonSerializationException("The generated building table must use the separate BuildingConstructionMaterials JSON.");
        }
        buildings.ConstructionMaterials = materials.ConstructionMaterials;
    }

    private static void DecodeTable(
        BuildingDefinitions table,
        out List<BuildingConfigElement> configs,
        out List<BuildingConstructionMaterialElement> materials, IReadOnlyDictionary<int, ItemTypeEnum> itemIds,
        bool requireMaterialItemIds = false)
    {
        var materialRows = table.ConstructionMaterials;
        configs = new List<BuildingConfigElement>(table.Buildings.Count);
        materials = new List<BuildingConstructionMaterialElement>(materialRows?.Count ?? 0);
        var buildingIds = new HashSet<int>();

        foreach (var row in table.Buildings)
        {
            if (row == null)
            {
                throw new JsonSerializationException("Null building definition.");
            }
            if (row.BuildingId <= 0 || !buildingIds.Add(row.BuildingId))
            {
                throw new JsonSerializationException($"Missing, invalid, or duplicate buildingId {row.BuildingId}.");
            }
            configs.Add(DecodeBuilding(row));

            if (materialRows != null)
            {
                if (row.Materials != null)
                {
                    throw new JsonSerializationException($"Building {row.BuildingId} must use the separate buildingConstructionMaterials table.");
                }
            }
            else if (row.Materials != null)
            {
                // 기존 직접 JSON 호출은 정의 행 안에 자재를 담을 수 있다.
                foreach (var material in row.Materials)
                {
                    if (material == null)
                    {
                        throw new JsonSerializationException($"Null material for building {row.BuildingType}/{row.Tier}.");
                    }
                    if (material.BuildingId != 0 && material.BuildingId != row.BuildingId)
                    {
                        throw new JsonSerializationException($"Inline material buildingId {material.BuildingId} does not match buildingId {row.BuildingId}.");
                    }
                    materials.Add(new BuildingConstructionMaterialElement(row.BuildingId, DecodeMaterialItem(material, itemIds),
                        material.Quantity ?? 1));
                }
            }
        }

        if (materialRows == null)
        {
            return;
        }
        foreach (var material in materialRows)
        {
            if (material == null)
            {
                throw new JsonSerializationException("Null buildingConstructionMaterials row.");
            }
            if (!buildingIds.Contains(material.BuildingId))
            {
                throw new JsonSerializationException($"buildingConstructionMaterials references unknown buildingId {material.BuildingId}.");
            }
            if (!material.Quantity.HasValue)
            {
                throw new JsonSerializationException($"Missing quantity for buildingId {material.BuildingId} and itemType {material.ItemType}.");
            }
            if (requireMaterialItemIds && material.ItemId == 0)
            {
                throw new JsonSerializationException($"Construction material for buildingId {material.BuildingId} requires itemId.");
            }
            materials.Add(new BuildingConstructionMaterialElement(material.BuildingId, DecodeMaterialItem(material, itemIds),
                material.Quantity.Value));
        }
    }

    private static ItemTypeEnum DecodeMaterialItem(ConstructionMaterialRow material, IReadOnlyDictionary<int, ItemTypeEnum> itemIds)
    {
        if (material.ItemId != 0)
        {
            return ItemConfigInitSystem.ResolveItemId(material.ItemId, itemIds);
        }
        if (material.ItemType == ItemTypeEnum.None)
        {
            throw new JsonSerializationException("Construction material requires an itemId or a legacy itemType.");
        }
        return material.ItemType;
    }

    private static BuildingConfigElement DecodeBuilding(BuildingRow row)
    {
        if (!string.IsNullOrEmpty(row.LegacyRequiredResearch) ||
            (row.RequiredResearchId != 0 && !ResearchConfigLoader.IsResearchId(row.RequiredResearchId)))
        {
            throw new JsonSerializationException($"Building {row.BuildingType}/{row.Tier} requires a numeric requiredResearchId in category 140 or 0.");
        }
        if (row.Footprint.Length != 2)
        {
            throw new JsonSerializationException($"Building {row.BuildingType}/{row.Tier} footprint must contain two dimensions.");
        }
        BuildingPowerConfig power = default;
        if (row.Power != null)
        {
            power = new BuildingPowerConfig(row.Power.SupplyRange, row.Power.ConnectionRange, row.Power.MaximumGeneration,
                row.Power.MaximumConsumption, row.Power.CoalEnergyPerItem);
        }
        return new BuildingConfigElement(
            row.BuildingType,
            row.Tier,
            new FixedString128Bytes(row.DisplayName),
            row.Speed,
            row.StorageCapacity,
            row.IsUnlockedByDefault,
            row.RequiredResearchId,
            new int2(row.Footprint[0], row.Footprint[1]),
            power,
            row.DroneStationRangeInChunks,
            row.DroneStationStorageCapacity,
            row.BuildingId);
    }

    private sealed class BuildingDefinitions
    {
        [JsonProperty("buildingDefinitions")]
        public List<BuildingRow> Buildings { get; set; }
        [JsonProperty("buildings")]
        public List<BuildingRow> LegacyBuildings { get; set; }
        [JsonProperty(Required = Required.DisallowNull)]
        public List<ConstructionMaterialRow> ConstructionMaterials { get; set; }
    }

    private sealed class ConstructionMaterialTable
    {
        [JsonProperty("buildingConstructionMaterials", Required = Required.Always)]
        public List<ConstructionMaterialRow> ConstructionMaterials { get; set; }
    }

    private sealed class BuildingRow
    {
        public int BuildingId { get; set; }
        [JsonProperty(Required = Required.Always)]
        public BuildingTypeEnum BuildingType { get; set; }
        [JsonProperty(Required = Required.Always)]
        public int Tier { get; set; }
        [JsonProperty(Required = Required.Always)]
        public string DisplayName { get; set; }
        public float Speed { get; set; }
        public int StorageCapacity { get; set; }
        public bool IsUnlockedByDefault { get; set; } = true;
        [JsonProperty("requiredResearch")]
        public string LegacyRequiredResearch { get; set; }
        public int RequiredResearchId { get; set; }
        [JsonProperty(Required = Required.Always)]
        public int[] Footprint { get; set; }
        public PowerRow Power { get; set; }
        public int DroneStationRangeInChunks { get; set; }
        public int DroneStationStorageCapacity { get; set; }
        public List<ConstructionMaterialRow> Materials { get; set; }
    }

    private sealed class ConstructionMaterialRow
    {
        public int BuildingId { get; set; }
        public int ItemId { get; set; }
        public ItemTypeEnum ItemType { get; set; }
        [JsonProperty(Required = Required.DisallowNull)]
        public int? Quantity { get; set; }
    }

    private sealed class PowerRow
    {
        public int SupplyRange { get; set; }
        public int ConnectionRange { get; set; }
        public float MaximumGeneration { get; set; }
        public float MaximumConsumption { get; set; }
        public float CoalEnergyPerItem { get; set; }
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
        if (!ValidateDefinitions(configs, materials, out var keys, out var buildingIds))
        {
            return false;
        }
        var runtimeIds = new HashSet<int>();
        foreach (var entry in entityManager.GetBuffer<BuildingRuntimeConfigElement>(entity, true))
        {
            if (!buildingIds.Contains(entry.BuildingId) || !runtimeIds.Add(entry.BuildingId))
            {
                Debug.LogError($"[BuildingConfigLoader] Pre-registered runtime configuration has invalid or duplicate buildingId {entry.BuildingId}.");
                return false;
            }
        }
        if (runtimeIds.Count != buildingIds.Count)
        {
            Debug.LogError("[BuildingConfigLoader] Pre-registered runtime configuration is missing a buildingId.");
            return false;
        }
        return ValidateRequiredTypes(keys);
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

        if (!ValidateDefinitions(configs, materials, out _, out _))
        {
            return Entity.Null;
        }
        return PublishValidatedConfig(entityManager, configs, materials);
    }

    /// <summary>TableBuilder 산출물 또는 직접 검증한 ECS 설정을 함께 게시한다.</summary>
    internal static Entity PublishValidatedConfig(
        EntityManager entityManager,
        List<BuildingConfigElement> configs,
        List<BuildingConstructionMaterialElement> materials)
    {
        // 통합 설정과 호환 버퍼를 같은 입력에서 게시한다. 이후 게임 Reader가 managed JSON 목록을 원본으로 다시 읽지 않게 한다.
        var entity = entityManager.CreateEntity(
            typeof(BuildingConfig),
            typeof(ResearchStatBonuses),
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
                runtimeBuffer.Add(new BuildingRuntimeConfigElement(configs[i].BuildingId, configs[i].Speed, configs[i].StorageCapacity));
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

    // 직접 전달되거나 사전 등록된 ECS 값은 TableBuilder를 거치지 않는다.
    private static bool ValidateDefinitions(List<BuildingConfigElement> configs, List<BuildingConstructionMaterialElement> materials,
        out HashSet<(BuildingTypeEnum Type, int Tier)> keys, out HashSet<int> ids)
    {
        keys = new HashSet<(BuildingTypeEnum Type, int Tier)>();
        ids = new HashSet<int>();
        foreach (var config in configs)
        {
            if (config.BuildingId <= 0 || !ids.Add(config.BuildingId))
            {
                Debug.LogError($"[BuildingConfigLoader] Invalid or duplicate buildingId {config.BuildingId}.");
                return false;
            }
            if (config.BuildingType <= BuildingTypeEnum.None || config.BuildingType >= BuildingTypeEnum.Count ||
                config.BuildingType == BuildingTypeEnum.ConstructionSite || config.Tier <= 0 ||
                string.IsNullOrWhiteSpace(config.DisplayName.ToString()) || !keys.Add((config.BuildingType, config.Tier)))
            {
                Debug.LogError("[BuildingConfigLoader] Invalid or duplicate building type/tier, or empty displayName.");
                return false;
            }
            if (!ValidateDefinition(config))
            {
                return false;
            }
        }
        if (materials != null)
        {
            var materialKeys = new HashSet<(int BuildingId, ItemTypeEnum ItemType)>();
            foreach (var material in materials)
            {
                if (!ids.Contains(material.BuildingId))
                {
                    Debug.LogError($"[BuildingConfigLoader] Construction material references undefined buildingId {material.BuildingId}.");
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
                // 한 건물 ID의 품목별 비용은 한 행으로 유지해 런타임 요구량 합산의 오버플로를 막는다.
                if (!materialKeys.Add((material.BuildingId, material.ItemType)))
                {
                    Debug.LogError($"[BuildingConfigLoader] Duplicate construction material '{material.ItemType}' for buildingId {material.BuildingId}.");
                    return false;
                }
            }
        }
        return true;
    }

    private static bool ValidateDefinition(in BuildingConfigElement config)
    {
        if (!ValidateFootprint(config.BuildingType, config.Footprint))
        {
            return false;
        }
        if (!ValidatePublishedPower(config))
        {
            return false;
        }
        bool station = DroneStationRangeUtility.IsStationType(config.BuildingType);
        // CoalGenerator의 용량은 바로 위 전력 역할 검증이 검사한다.
        bool storesItems = station || config.BuildingType == BuildingTypeEnum.Storage;
        if (storesItems && (config.StorageCapacity <= 0 || config.StorageCapacity > GameConstants.MaxStorageSlots))
        {
            Debug.LogError("[BuildingConfigLoader] Invalid storage capacity; expected 1..MaxStorageSlots.");
            return false;
        }
        if ((station && (config.DroneStationStorageCapacity <= 0 ||
                (long)config.StorageCapacity + config.DroneStationStorageCapacity > GameConstants.MaxStorageSlots)) ||
            (!station && config.DroneStationStorageCapacity != 0))
        {
            Debug.LogError("[BuildingConfigLoader] Invalid station dedicated/general storage capacity.");
            return false;
        }
        if (!DroneStationRangeUtility.IsStationType(config.BuildingType) && config.DroneStationRangeInChunks != 0)
        {
            Debug.LogError("[BuildingConfigLoader] Only MainFacility and DroneStation can define a drone station range.");
            return false;
        }
        return true;
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

    private static bool ValidatePublishedPower(in BuildingConfigElement config)
    {
        BuildingPowerConfig power = config.Power;
        BuildingTypeEnum type = config.BuildingType;
        int tier = config.Tier;
        if (power.SupplyRange < 0 || power.ConnectionRange < 0 ||
            !math.isfinite(power.MaximumGeneration) || !math.isfinite(power.MaximumConsumption) ||
            !math.isfinite(power.CoalEnergyPerItem) || power.MaximumGeneration < 0f ||
            power.MaximumConsumption < 0f || power.CoalEnergyPerItem < 0f)
        {
            return RejectPublishedPower(type, tier, "has invalid power values");
        }
        bool expectsPower = type is BuildingTypeEnum.PowerPole or BuildingTypeEnum.MainFacility or BuildingTypeEnum.CoalGenerator
            or BuildingTypeEnum.Miner or BuildingTypeEnum.Crafter or BuildingTypeEnum.ResearchBuilding;
        if (power.HasConfiguration != expectsPower)
        {
            return RejectPublishedPower(type, tier, "has missing or unexpected power configuration");
        }
        bool valid;
        switch (type)
        {
            case BuildingTypeEnum.PowerPole:
                valid = power.MaximumGeneration == 0f && power.MaximumConsumption == 0f && power.CoalEnergyPerItem == 0f;
                break;
            case BuildingTypeEnum.MainFacility:
                valid = power.SupplyRange == 0 && power.MaximumGeneration > 0f &&
                    power.MaximumConsumption == 0f && power.CoalEnergyPerItem == 0f;
                break;
            case BuildingTypeEnum.CoalGenerator:
                valid = power.SupplyRange == 0 && power.ConnectionRange == 0 && power.MaximumGeneration > 0f &&
                    power.MaximumConsumption == 0f && power.CoalEnergyPerItem > 0f &&
                    config.StorageCapacity > 0 && config.StorageCapacity <= GameConstants.MaxStorageSlots;
                break;
            case BuildingTypeEnum.Miner:
            case BuildingTypeEnum.Crafter:
            case BuildingTypeEnum.ResearchBuilding:
                valid = power.SupplyRange == 0 && power.ConnectionRange == 0 && power.MaximumGeneration == 0f &&
                    power.MaximumConsumption > 0f && power.CoalEnergyPerItem == 0f;
                break;
            default:
                valid = power.SupplyRange == 0 && power.ConnectionRange == 0 && power.MaximumGeneration == 0f &&
                    power.MaximumConsumption == 0f && power.CoalEnergyPerItem == 0f;
                break;
        }
        if (!valid)
        {
            return RejectPublishedPower(type, tier, "has power values or fuel slots inconsistent with its building role");
        }
        return true;
    }

    private static bool RejectPublishedPower(BuildingTypeEnum type, int tier, string reason)
    {
        Debug.LogError($"[BuildingConfigLoader] {type}/{tier} {reason}.");
        return false;
    }
}
