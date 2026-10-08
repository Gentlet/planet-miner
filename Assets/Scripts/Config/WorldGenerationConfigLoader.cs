using System;
using System.Collections.Generic;
using System.Text;
using Unity.Entities;
using Unity.Collections;
using UnityEngine;

/// <summary>
/// 역할·목적: Resources의 월드/자원/바닥 JSON을 검증해 시드·초기 청크 범위·가중치/변형 목록을 ECS에 게시한다.
/// 입력·출력: JSON DTO를 검증된 managed 목록/바닥 정의로 바꾸며 오류는 로그와 false로 반환한다. 게시 함수는 새 설정 엔티티와 버퍼를 만든다.
/// 이용: WorldGenerationConfigLoadSystem(Initialization)이 호출하고 청크·자원 생성/바닥 선택은 ECS 상태를 읽는다.
/// 수명·소유권: DTO/목록은 게시 전 입력이다. 게시 시점·중복/World 수명은 호출자가 소유하며 런타임 공간/실물은 로더가 만들지 않는다.
/// </summary>
public static class WorldGenerationConfigLoader
{
    public const string DefaultResourcePath = "Config/WorldGenerationConfig";

    /// <summary>월드 시드·초기 청크 크기·자원/바닥 입력의 최상위 JSON DTO. ECS에 부착하는 상태가 아니다.</summary>
    [Serializable]
    public class WorldGenerationConfigFile
    {
        public uint worldSeed;
        public int initialChunkSize;
        public List<ResourceGenerationConfigData> configs;
        public FloorGenerationConfigData floor;
    }

    /// <summary>자원별 광맥 확률/반경/셀 채움/잔량 범위의 파싱 입력. 검증 후 ResourceGenerationConfigElement로 바꾼다.</summary>
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

    /// <summary>바이옴 구역·경계 노이즈/전이 폭·기본/전이 변형 목록의 JSON 입력. 실제 셀 선택/렌더 객체는 포함하지 않는다.</summary>
    [Serializable]
    public class FloorGenerationConfigData
    {
        public int biomeRegionSizeInChunks;
        public int transitionWidthInChunks;
        public float boundaryNoiseScaleInCells;
        public float boundaryNoiseAmplitudeInCells;
        public float nearBiomePreferenceExponent;
        public List<FloorVariantConfigData> transitionFloorVariants;
        public List<FloorBiomeConfigData> biomes;
    }

    /// <summary>바이옴 ID·가중치·바닥 변형의 파싱 입력. 게시 시 평탄한 변형 목록의 시작/길이로 변환한다.</summary>
    [Serializable]
    public class FloorBiomeConfigData
    {
        public string id;
        public float selectionWeight;
        public List<FloorVariantConfigData> floorVariants;
    }

    /// <summary>바닥 Sprite Resources 경로와 가중치 DTO. Sprite 로드/실제 렌더링이 구현됐다는 뜻은 아니다.</summary>
    [Serializable]
    public class FloorVariantConfigData
    {
        public string spriteResourcePath;
        public float weight;
    }

    /// <summary>검증 후 게시 전의 바닥 설정·바이옴·변형 목록. managed 값이며 PublishConfig가 World 버퍼로 복사한다.</summary>
    public sealed class FloorGenerationDefinition
    {
        public FloorGenerationSettings Settings;
        public List<FloorBiomeElement> Biomes;
        public List<FloorVariantElement> Variants;
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
        return TryLoadConfigFromResources(resourcePath, out worldSeed, out initialChunkSize, out elements, out _);
    }

    public static bool TryLoadConfigFromResources(
        string resourcePath,
        out uint worldSeed,
        out int initialChunkSize,
        out List<ResourceGenerationConfigElement> elements,
        out FloorGenerationDefinition floor)
    {
        worldSeed = 0;
        initialChunkSize = 0;
        elements = null;
        floor = null;

        try
        {
            TextAsset asset = Resources.Load<TextAsset>(resourcePath);
            if (asset == null)
            {
                Debug.LogError($"[WorldGenerationConfigLoader] Config asset not found at Resources/{resourcePath}");
                return false;
            }

            return TryParseAndValidateJson(asset.text, out worldSeed, out initialChunkSize, out elements, out floor);
        }
        catch (Exception exception)
        {
            worldSeed = 0;
            initialChunkSize = 0;
            elements = null;
            floor = null;
            Debug.LogError($"[WorldGenerationConfigLoader] Failed to read config at '{resourcePath}': {exception.Message}");
            return false;
        }
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
        return TryParseAndValidateJson(jsonText, out worldSeed, out initialChunkSize, out elements, out _);
    }

    public static bool TryParseAndValidateJson(
        string jsonText,
        out uint worldSeed,
        out int initialChunkSize,
        out List<ResourceGenerationConfigElement> elements,
        out FloorGenerationDefinition floor)
    {
        worldSeed = 0;
        initialChunkSize = 0;
        elements = null;
        floor = null;

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

        if (!TryValidateFloor(configFile.floor, out FloorGenerationDefinition validatedFloor))
        {
            return false;
        }

        worldSeed = configFile.worldSeed;
        initialChunkSize = configFile.initialChunkSize > 0 ? configFile.initialChunkSize : 3;
        elements = validatedList;
        floor = validatedFloor;
        return true;
    }

    private static bool TryValidateFloor(FloorGenerationConfigData config, out FloorGenerationDefinition floor)
    {
        floor = null;
        if (config == null || config.biomeRegionSizeInChunks <= 0 ||
            config.transitionWidthInChunks < 0 || config.transitionWidthInChunks > config.biomeRegionSizeInChunks ||
            !IsPositiveFinite(config.boundaryNoiseScaleInCells) ||
            !IsNonNegativeFinite(config.boundaryNoiseAmplitudeInCells) ||
            !IsPositiveFinite(config.nearBiomePreferenceExponent))
        {
            Debug.LogError("[WorldGenerationConfigLoader] Invalid floor generation parameters.");
            return false;
        }

        if (config.biomes == null || config.biomes.Count == 0 ||
            config.transitionFloorVariants == null || config.transitionFloorVariants.Count == 0)
        {
            Debug.LogError("[WorldGenerationConfigLoader] Floor biomes and transition variants are required.");
            return false;
        }

        var variants = new List<FloorVariantElement>();
        if (!TryAddVariants(config.transitionFloorVariants, "Transition", variants))
        {
            return false;
        }

        var biomes = new List<FloorBiomeElement>(config.biomes.Count);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        float totalWeight = 0f;
        foreach (FloorBiomeConfigData biome in config.biomes)
        {
            if (biome == null || string.IsNullOrWhiteSpace(biome.id) ||
                !ids.Add(biome.id) || !IsPositiveFinite(biome.selectionWeight) ||
                Encoding.UTF8.GetByteCount(biome.id) > FixedString64Bytes.UTF8MaxLengthInBytes)
            {
                Debug.LogError("[WorldGenerationConfigLoader] Invalid or duplicate floor biome id/weight.");
                return false;
            }

            int start = variants.Count;
            if (!TryAddVariants(biome.floorVariants, biome.id, variants))
            {
                return false;
            }

            biomes.Add(new FloorBiomeElement
            {
                Id = biome.id,
                SelectionWeight = biome.selectionWeight,
                VariantStart = start,
                VariantCount = variants.Count - start
            });
            totalWeight += biome.selectionWeight;
        }

        if (!IsPositiveFinite(totalWeight))
        {
            Debug.LogError("[WorldGenerationConfigLoader] Floor biome total weight is invalid.");
            return false;
        }

        floor = new FloorGenerationDefinition
        {
            Settings = new FloorGenerationSettings
            {
                BiomeRegionSizeInChunks = config.biomeRegionSizeInChunks,
                TransitionWidthInChunks = config.transitionWidthInChunks,
                BoundaryNoiseScaleInCells = config.boundaryNoiseScaleInCells,
                BoundaryNoiseAmplitudeInCells = config.boundaryNoiseAmplitudeInCells,
                NearBiomePreferenceExponent = config.nearBiomePreferenceExponent,
                TransitionVariantCount = config.transitionFloorVariants.Count
            },
            Biomes = biomes,
            Variants = variants
        };
        return true;
    }

    private static bool TryAddVariants(List<FloorVariantConfigData> source, string owner, List<FloorVariantElement> target)
    {
        if (source == null || source.Count == 0)
        {
            Debug.LogError($"[WorldGenerationConfigLoader] {owner} has no floor variants.");
            return false;
        }

        float totalWeight = 0f;
        foreach (FloorVariantConfigData variant in source)
        {
            if (variant == null || string.IsNullOrWhiteSpace(variant.spriteResourcePath) ||
                !IsPositiveFinite(variant.weight) ||
                Encoding.UTF8.GetByteCount(variant.spriteResourcePath) > FixedString128Bytes.UTF8MaxLengthInBytes ||
                Resources.Load<Sprite>(variant.spriteResourcePath) == null)
            {
                Debug.LogError($"[WorldGenerationConfigLoader] Invalid floor variant or missing Sprite for {owner}: '{variant?.spriteResourcePath}'.");
                return false;
            }

            totalWeight += variant.weight;
            target.Add(new FloorVariantElement { SpriteResourcePath = variant.spriteResourcePath, Weight = variant.weight });
        }

        if (!IsPositiveFinite(totalWeight))
        {
            Debug.LogError($"[WorldGenerationConfigLoader] {owner} floor variant total weight is invalid.");
            return false;
        }
        return true;
    }

    private static bool IsPositiveFinite(float value) => value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool IsNonNegativeFinite(float value) => value >= 0f && !float.IsNaN(value) && !float.IsInfinity(value);

    /// <summary>제품 초기화는 자원과 바닥을 같은 게시 엔티티에서 요구한다. 자원 전용 게시 API는 격리 구성용이다.</summary>
    public static bool HasCompleteConfig(EntityManager entityManager, Entity entity)
    {
        if (!entityManager.HasComponent<FloorGenerationSettings>(entity) ||
            !entityManager.HasBuffer<ResourceGenerationConfigElement>(entity) ||
            !entityManager.HasBuffer<FloorBiomeElement>(entity) ||
            !entityManager.HasBuffer<FloorVariantElement>(entity))
        {
            return false;
        }
        return entityManager.GetBuffer<ResourceGenerationConfigElement>(entity, true).Length > 0 &&
               entityManager.GetBuffer<FloorBiomeElement>(entity, true).Length > 0 &&
               entityManager.GetBuffer<FloorVariantElement>(entity, true).Length > 0;
    }

    /// <summary>명시적 자원 전용 게시 API. 전체 제품 초기화는 바닥을 포함한 오버로드를 사용한다.</summary>
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
        return PublishConfig(entityManager, worldSeed, initialChunkSize, elements, null);
    }

    public static Entity PublishConfig(
        EntityManager entityManager,
        uint worldSeed,
        int initialChunkSize,
        List<ResourceGenerationConfigElement> elements,
        FloorGenerationDefinition floor)
    {
        Entity entity = entityManager.CreateEntity(typeof(ResourceGenerationSettings));
        try
        {
            entityManager.SetComponentData(entity, new ResourceGenerationSettings(worldSeed, initialChunkSize));
            DynamicBuffer<ResourceGenerationConfigElement> buffer = entityManager.AddBuffer<ResourceGenerationConfigElement>(entity);
            for (int i = 0; i < elements.Count; i++)
            {
                buffer.Add(elements[i]);
            }

            // 자원 전용 명시적 게시도 허용하지만 제품 Init은 바닥을 포함한 완전한 구성을 요구한다.
            if (floor != null)
            {
                entityManager.AddComponentData(entity, floor.Settings);
                DynamicBuffer<FloorBiomeElement> biomes = entityManager.AddBuffer<FloorBiomeElement>(entity);
                foreach (FloorBiomeElement biome in floor.Biomes) biomes.Add(biome);
                DynamicBuffer<FloorVariantElement> variants = entityManager.AddBuffer<FloorVariantElement>(entity);
                foreach (FloorVariantElement variant in floor.Variants) variants.Add(variant);
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
