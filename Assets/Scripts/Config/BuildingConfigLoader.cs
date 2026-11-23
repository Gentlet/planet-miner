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
    private const int MaxFuelTypes = 63;
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
        var table = JsonConvert.DeserializeObject<BuildingDefinitions>(json, new JsonSerializerSettings
        {
            MissingMemberHandling = MissingMemberHandling.Error,
            Converters = { new StringEnumConverter { AllowIntegerValues = false } }
        });
        if (table == null)
        {
            throw new JsonSerializationException("Missing building table.");
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
            var config = DecodeBuilding(row, itemIds);
            if (!ValidateDefinition(config, out var reason))
            {
                throw new JsonSerializationException($"Building {row.BuildingId}: {reason}");
            }
            configs.Add(config);

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
                        throw new JsonSerializationException($"Null material for building {row.BuildingId}.");
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

        if (requireMaterialItemIds && !HasOneBootstrap(configs))
        {
            throw new JsonSerializationException("Exactly one BootstrapOnly building is required.");
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

    private static BuildingConfigElement DecodeBuilding(BuildingRow row, IReadOnlyDictionary<int, ItemTypeEnum> itemIds)
    {
        if (row.RequiredResearchId != 0 && !ConfigIdUtility.IsInCategory(row.RequiredResearchId, ConfigIdUtility.ResearchCategory))
        {
            throw new JsonSerializationException($"Building {row.BuildingId} requires a numeric requiredResearchId in category 140 or 0.");
        }
        if (row.Footprint.Length != 2)
        {
            throw new JsonSerializationException($"Building {row.BuildingId} footprint must contain two dimensions.");
        }

        var features = row.Features;
        var config = new BuildingConfigElement
        {
            BuildingId = row.BuildingId,
            Tier = row.Tier,
            DisplayName = new FixedString128Bytes(row.DisplayName),
            IsUnlocked = row.IsUnlockedByDefault,
            RequiredResearch = row.RequiredResearchId,
            Footprint = new int2(row.Footprint[0], row.Footprint[1]),
            BeltTransport = DecodeSpeed(features.BeltTransport),
            Mining = DecodeSpeed(features.Mining),
            Crafting = DecodeSpeed(features.Crafting),
            Research = DecodeSpeed(features.Research),
            DroneCharging = features.DroneCharging != null
        };
        if (features.ItemStorage is ItemStorageRow storage)
        {
            config.ItemStorage = new BuildingItemStorageConfig { IsPresent = true, Slots = storage.Slots, FilterEditable = storage.FilterEditable };
        }
        if (features.DroneStation is StationRow station)
            config.DroneStation = new BuildingDroneStationConfig
            {
                IsPresent = true, RangeInChunks = math.max(0, station.RangeInChunks), DroneSlots = station.DroneSlots
            };
        if (features.BeltRouting is RoutingRow routing)
        {
            config.BeltRouting = routing.Mode switch
            {
                "Split" => BeltRoutingMode.Split,
                "Merge" => BeltRoutingMode.Merge,
                _ => throw new JsonSerializationException($"Building {row.BuildingId} has an invalid routing mode.")
            };
        }
        if (features.Placement is PlacementRow placement)
        {
            config.Placement.BootstrapOnly = placement.Mode switch
            {
                "BootstrapOnly" => true,
                "Normal" => false,
                _ => throw new JsonSerializationException($"Building {row.BuildingId} has an invalid placement mode.")
            };
            config.Placement.Indestructible = placement.Indestructible;
        }
        if (features.PowerGrid is GridRow grid)
        {
            config.Power.Grid = new BuildingPowerGridConfig { IsPresent = true, SupplyRange = grid.SupplyRange, ConnectionRange = grid.ConnectionRange };
        }
        if (features.WorkPower is WorkPowerRow work)
        {
            config.Power.Work = new BuildingWorkPowerConfig { IsPresent = true, MaximumConsumption = work.MaximumConsumption };
        }
        if (features.PowerGeneration is GenerationRow generation)
        {
            config.Power.Generation.IsPresent = true;
            config.Power.Generation.MaximumGeneration = generation.MaximumGeneration;
            if (generation.FuelStorage is FuelStorageRow fuelStorage)
            {
                config.Power.Generation.FuelStorage = DecodeFuelStorage(fuelStorage, row.BuildingId, itemIds);
            }
        }
        return config;
    }

    private static BuildingSpeedConfig DecodeSpeed(SpeedRow row)
    {
        return row == null ? default : new BuildingSpeedConfig(row.Speed);
    }

    private static BuildingFuelStorageConfig DecodeFuelStorage(FuelStorageRow row, int buildingId,
        IReadOnlyDictionary<int, ItemTypeEnum> itemIds)
    {
        var storage = new BuildingFuelStorageConfig { IsPresent = true, Slots = row.Slots, BeltOutput = row.BeltOutput };
        foreach (var fuel in row.Fuels)
        {
            if (fuel == null)
            {
                throw new JsonSerializationException($"Building {buildingId} has a null fuel row.");
            }
            if (storage.Fuels.Length == MaxFuelTypes)
            {
                throw new JsonSerializationException($"Building {buildingId} exceeds {MaxFuelTypes} fuel rows.");
            }
            storage.Fuels.Add(new BuildingFuelConfig(ItemConfigInitSystem.ResolveItemId(fuel.ItemId, itemIds), fuel.EnergyPerItem));
        }
        return storage;
    }

    private sealed class BuildingDefinitions
    {
        [JsonProperty("buildingDefinitions")]
        public List<BuildingRow> Buildings { get; set; }
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
        public FeaturesRow Features { get; set; }
        [JsonProperty(Required = Required.Always)]
        public int Tier { get; set; }
        [JsonProperty(Required = Required.Always)]
        public string DisplayName { get; set; }
        public string TextureResourcePath { get; set; }
        public bool IsUnlockedByDefault { get; set; } = true;
        public int RequiredResearchId { get; set; }
        [JsonProperty(Required = Required.Always)]
        public int[] Footprint { get; set; }
        public List<ConstructionMaterialRow> Materials { get; set; }
    }

    private sealed class FeaturesRow
    {
        public SpeedRow BeltTransport { get; set; }
        public SpeedRow Mining { get; set; }
        public SpeedRow Crafting { get; set; }
        public SpeedRow Research { get; set; }
        public RoutingRow BeltRouting { get; set; }
        public ItemStorageRow ItemStorage { get; set; }
        public GridRow PowerGrid { get; set; }
        public WorkPowerRow WorkPower { get; set; }
        public GenerationRow PowerGeneration { get; set; }
        public StationRow DroneStation { get; set; }
        public ChargingRow DroneCharging { get; set; }
        public PlacementRow Placement { get; set; }
    }

    private sealed class SpeedRow
    {
        [JsonProperty(Required = Required.Always)]
        public float Speed { get; set; }
    }

    private sealed class RoutingRow
    {
        [JsonProperty(Required = Required.Always)]
        public string Mode { get; set; }
    }

    private sealed class ItemStorageRow
    {
        [JsonProperty(Required = Required.Always)]
        public int Slots { get; set; }
        [JsonProperty(Required = Required.Always)]
        public bool FilterEditable { get; set; }
    }

    private sealed class GridRow
    {
        [JsonProperty(Required = Required.Always)]
        public int SupplyRange { get; set; }
        [JsonProperty(Required = Required.Always)]
        public int ConnectionRange { get; set; }
    }

    private sealed class WorkPowerRow
    {
        [JsonProperty(Required = Required.Always)]
        public float MaximumConsumption { get; set; }
    }

    private sealed class GenerationRow
    {
        [JsonProperty(Required = Required.Always)]
        public float MaximumGeneration { get; set; }
        public FuelStorageRow FuelStorage { get; set; }
    }

    private sealed class FuelStorageRow
    {
        [JsonProperty(Required = Required.Always)]
        public int Slots { get; set; }
        [JsonProperty(Required = Required.Always)]
        public bool BeltOutput { get; set; }
        [JsonProperty(Required = Required.Always)]
        public List<FuelRow> Fuels { get; set; }
    }

    private sealed class FuelRow
    {
        [JsonProperty(Required = Required.Always)]
        public int ItemId { get; set; }
        [JsonProperty(Required = Required.Always)]
        public float EnergyPerItem { get; set; }
    }

    private sealed class StationRow
    {
        [JsonProperty(Required = Required.Always)]
        public int RangeInChunks { get; set; }
        [JsonProperty(Required = Required.Always)]
        public int DroneSlots { get; set; }
    }

    private sealed class ChargingRow { }

    private sealed class PlacementRow
    {
        [JsonProperty(Required = Required.Always)]
        public string Mode { get; set; }
        [JsonProperty(Required = Required.Always)]
        public bool Indestructible { get; set; }
    }

    private sealed class ConstructionMaterialRow
    {
        public int BuildingId { get; set; }
        public int ItemId { get; set; }
        public ItemTypeEnum ItemType { get; set; }
        [JsonProperty(Required = Required.DisallowNull)]
        public int? Quantity { get; set; }
    }

    /// <summary>사전 등록된 ECS 설정도 동일한 정의와 재료 계약을 검사한다.</summary>
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
        if (!ValidateDefinitions(configs, materials, out var buildingIds))
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

        if (!ValidateDefinitions(configs, materials, out _))
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
                var config = configs[i];
                float speed = config.BeltTransport.Speed + config.Mining.Speed + config.Crafting.Speed + config.Research.Speed;
                runtimeBuffer.Add(new BuildingRuntimeConfigElement(config.BuildingId, speed, config.ItemStorage.Slots));
            }

            if (materials != null)
            {
                for (int i = 0; i < materials.Count; i++)
                {
                    materialBuffer.Add(materials[i]);
                }
            }
            RuntimeEntityDebugNames.SetConfig(entityManager, entity, "Config / Buildings");
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
        out HashSet<int> ids)
    {
        ids = new HashSet<int>();
        foreach (var config in configs)
        {
            if (config.BuildingId <= 0 || !ids.Add(config.BuildingId))
            {
                Debug.LogError($"[BuildingConfigLoader] Invalid or duplicate buildingId {config.BuildingId}.");
                return false;
            }
            if (!ValidateDefinition(config, out var reason))
            {
                Debug.LogError($"[BuildingConfigLoader] Building {config.BuildingId}: {reason}");
                return false;
            }
        }
        if (!HasOneBootstrap(configs))
        {
            Debug.LogError("[BuildingConfigLoader] Exactly one BootstrapOnly building is required.");
            return false;
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

    private static bool HasOneBootstrap(List<BuildingConfigElement> configs)
    {
        return configs.FindAll(config => config.Placement.BootstrapOnly).Count == 1;
    }

    private static bool ValidateDefinition(in BuildingConfigElement config, out string reason)
    {
        bool station = config.DroneStation.IsPresent;
        bool storage = config.ItemStorage.IsPresent;
        var power = config.Power;
        var fuels = power.Generation.FuelStorage;
        int workerCount = (config.Mining.IsPresent ? 1 : 0) + (config.Crafting.IsPresent ? 1 : 0) +
            (config.Research.IsPresent ? 1 : 0);
        bool worker = workerCount != 0;
        bool belt = config.BeltTransport.IsPresent;
        bool routing = config.BeltRouting != BeltRoutingMode.None;
        bool dynamicInput = config.Crafting.IsPresent || config.Research.IsPresent;
        if (config.Footprint.x <= 0 || config.Footprint.y <= 0 || config.Tier <= 0 ||
            string.IsNullOrWhiteSpace(config.DisplayName.ToString()))
        {
            reason = "Invalid footprint, tier or displayName.";
            return false;
        }
        if ((!worker && !belt && !routing && !storage && !station && !power.Grid.IsPresent && !power.Generation.IsPresent) ||
            workerCount > 1 || worker != power.Work.IsPresent ||
            worker && (belt || config.DroneCharging) ||
            belt && routing || config.BeltRouting > BeltRoutingMode.Merge ||
            config.ItemStorage.FilterEditable && !storage ||
            config.DroneCharging && !station ||
            dynamicInput && (storage || station || fuels.IsPresent))
        {
            reason = "Feature dependencies or shared input ownership are invalid.";
            return false;
        }
        if (config.Placement.BootstrapOnly && (!storage || !station || config.Tier != 1 || !config.Placement.Indestructible))
        {
            reason = "BootstrapOnly requires tier 1, ItemStorage, DroneStation and indestructible placement.";
            return false;
        }
        if (!IsValidSpeed(config.BeltTransport) || !IsValidSpeed(config.Mining) ||
            !IsValidSpeed(config.Crafting) || !IsValidSpeed(config.Research) ||
            config.BeltTransport.Speed > GameConstants.MaxBeltSpeed)
        {
            reason = "Invalid speed for the declared feature.";
            return false;
        }
        if (storage != (config.ItemStorage.Slots > 0) || station != (config.DroneStation.DroneSlots > 0) ||
            fuels.IsPresent != (fuels.Slots > 0) || config.ItemStorage.Slots < 0 || fuels.Slots < 0 ||
            config.DroneStation.DroneSlots < 0 || config.DroneStation.RangeInChunks < 0 ||
            (!station && config.DroneStation.RangeInChunks != 0) ||
            (long)config.ItemStorage.Slots + config.DroneStation.DroneSlots + fuels.Slots > GameConstants.MaxStorageSlots)
        {
            reason = "Invalid general, drone, or fuel slot capacity or station range.";
            return false;
        }
        if ((!power.Grid.IsPresent && (power.Grid.SupplyRange != 0 || power.Grid.ConnectionRange != 0)) ||
            power.Grid.SupplyRange < 0 || power.Grid.ConnectionRange < 0 ||
            !math.isfinite(power.Generation.MaximumGeneration) || !math.isfinite(power.Work.MaximumConsumption) ||
            (power.Generation.IsPresent ? power.Generation.MaximumGeneration <= 0f : power.Generation.MaximumGeneration != 0f) ||
            (power.Work.IsPresent ? power.Work.MaximumConsumption <= 0f : power.Work.MaximumConsumption != 0f))
        {
            reason = "Invalid power settings for the declared features.";
            return false;
        }
        if (fuels.IsPresent && !power.Generation.IsPresent || fuels.BeltOutput && !fuels.IsPresent ||
            fuels.IsPresent != (fuels.Fuels.Length > 0) || fuels.Fuels.Length > MaxFuelTypes)
        {
            reason = "Fuel storage or allowed fuel rows do not match the generator settings.";
            return false;
        }

        var itemTypes = new HashSet<ItemTypeEnum>();
        foreach (var fuel in fuels.Fuels)
        {
            if (fuel.ItemType is ItemTypeEnum.None or ItemTypeEnum.Drone ||
                (int)fuel.ItemType >= FixedBitSet.Capacity || !Enum.IsDefined(typeof(ItemTypeEnum), fuel.ItemType) ||
                !itemTypes.Add(fuel.ItemType) ||
                !math.isfinite(fuel.EnergyPerItem) || fuel.EnergyPerItem <= 0f)
            {
                reason = $"Invalid or duplicate energy settings for fuel {fuel.ItemType}.";
                return false;
            }
        }
        reason = null;
        return true;
    }
    private static bool IsValidSpeed(in BuildingSpeedConfig config)
    {
        return math.isfinite(config.Speed) && (config.IsPresent ? config.Speed > 0f : config.Speed == 0f);
    }

}
