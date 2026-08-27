using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

[Serializable]
public class FloorGenerationConfigFile
{
    public uint worldSeed = 1;
    public int biomeRegionSizeInChunks = 3;
    public int transitionWidthInChunks = 2;
    public float boundaryNoiseScaleInCells = 24f;
    public float boundaryNoiseAmplitudeInCells = 6f;
    public float nearBiomePreferenceExponent = 0.5f;
    public List<FloorBiomeConfigData> biomes = new();
}

[Serializable]
public class FloorBiomeConfigData
{
    public string id;
    public float selectionWeight = 1f;
    public List<FloorVariantConfigData> floorVariants = new();
}

[Serializable]
public class FloorVariantConfigData
{
    public string spriteResourcePath;
    public float weight = 1f;
}

public readonly struct FloorTileSelection
{
    public FloorTileSelection(int biomeIndex, int variantIndex)
    {
        BiomeIndex = biomeIndex;
        VariantIndex = variantIndex;
    }

    public int BiomeIndex { get; }
    public int VariantIndex { get; }
}

public sealed class FloorGenerationSettings
{
    public FloorGenerationSettings(
        uint worldSeed,
        int biomeRegionSizeInChunks,
        int transitionWidthInChunks,
        float boundaryNoiseScaleInCells,
        float boundaryNoiseAmplitudeInCells,
        float nearBiomePreferenceExponent,
        IReadOnlyList<FloorBiomeConfigData> biomes)
    {
        WorldSeed = worldSeed == 0 ? 1u : worldSeed;
        BiomeRegionSizeInChunks = biomeRegionSizeInChunks;
        TransitionWidthInChunks = transitionWidthInChunks;
        BoundaryNoiseScaleInCells = boundaryNoiseScaleInCells;
        BoundaryNoiseAmplitudeInCells = boundaryNoiseAmplitudeInCells;
        NearBiomePreferenceExponent = nearBiomePreferenceExponent;
        Biomes = biomes;
    }

    public uint WorldSeed { get; }
    public int BiomeRegionSizeInChunks { get; }
    public int TransitionWidthInChunks { get; }
    public float BoundaryNoiseScaleInCells { get; }
    public float BoundaryNoiseAmplitudeInCells { get; }
    public float NearBiomePreferenceExponent { get; }
    public IReadOnlyList<FloorBiomeConfigData> Biomes { get; }
}

public static class FloorGenerationConfigLoader
{
    public static bool TryLoad(string resourcePath, out FloorGenerationSettings settings)
    {
        settings = null;

        TextAsset configAsset = Resources.Load<TextAsset>(resourcePath);

        if (configAsset == null)
        {
            Debug.LogError($"Floor generation config file not found. Path : Resources/{resourcePath}");
            return false;
        }

        FloorGenerationConfigFile config;

        try
        {
            config = JsonUtility.FromJson<FloorGenerationConfigFile>(configAsset.text);
        }
        catch (Exception exception)
        {
            Debug.LogError($"Failed to parse floor generation config. Path : Resources/{resourcePath}, Error : {exception.Message}");
            return false;
        }

        if (!TryCreateSettings(config, out settings, out string error))
        {
            Debug.LogError($"Invalid floor generation config. Path : Resources/{resourcePath}, Error : {error}");
            return false;
        }

        return true;
    }

    public static bool TryCreateSettings(
        FloorGenerationConfigFile config,
        out FloorGenerationSettings settings,
        out string error)
    {
        settings = null;
        error = null;

        if (config == null)
        {
            error = "Config is missing.";
            return false;
        }

        if (config.biomeRegionSizeInChunks <= 0)
        {
            error = "Biome region size must be greater than zero.";
            return false;
        }

        if (config.transitionWidthInChunks < 0 || config.transitionWidthInChunks > config.biomeRegionSizeInChunks)
        {
            error = "Transition width must be between zero and the biome region size.";
            return false;
        }

        if (config.boundaryNoiseScaleInCells <= 0f)
        {
            error = "Boundary noise scale must be greater than zero.";
            return false;
        }

        if (config.boundaryNoiseAmplitudeInCells < 0f)
        {
            error = "Boundary noise amplitude cannot be negative.";
            return false;
        }

        if (config.nearBiomePreferenceExponent <= 0f)
        {
            error = "Near-biome preference exponent must be greater than zero.";
            return false;
        }

        if (config.biomes == null || config.biomes.Count == 0)
        {
            error = "At least one biome is required.";
            return false;
        }

        float totalBiomeWeight = 0f;

        for (int biomeIndex = 0; biomeIndex < config.biomes.Count; biomeIndex++)
        {
            FloorBiomeConfigData biome = config.biomes[biomeIndex];

            if (biome == null || string.IsNullOrWhiteSpace(biome.id))
            {
                error = $"Biome at index {biomeIndex} has no id.";
                return false;
            }

            if (biome.selectionWeight <= 0f)
            {
                error = $"Biome '{biome.id}' must have a positive selection weight.";
                return false;
            }

            if (biome.floorVariants == null || biome.floorVariants.Count == 0)
            {
                error = $"Biome '{biome.id}' has no floor variants.";
                return false;
            }

            totalBiomeWeight += biome.selectionWeight;

            for (int variantIndex = 0; variantIndex < biome.floorVariants.Count; variantIndex++)
            {
                FloorVariantConfigData variant = biome.floorVariants[variantIndex];

                if (variant == null || string.IsNullOrWhiteSpace(variant.spriteResourcePath))
                {
                    error = $"Biome '{biome.id}' has a floor variant without a resource path.";
                    return false;
                }

                if (variant.weight <= 0f)
                {
                    error = $"Biome '{biome.id}' floor variant '{variant.spriteResourcePath}' must have a positive weight.";
                    return false;
                }
            }
        }

        if (totalBiomeWeight <= 0f)
        {
            error = "Total biome selection weight must be positive.";
            return false;
        }

        settings = new FloorGenerationSettings(
            config.worldSeed,
            config.biomeRegionSizeInChunks,
            config.transitionWidthInChunks,
            config.boundaryNoiseScaleInCells,
            config.boundaryNoiseAmplitudeInCells,
            config.nearBiomePreferenceExponent,
            config.biomes);
        return true;
    }
}

public static class FloorBiomeSampler
{
    private const uint biomeSalt = 0x4F1BBCDCu;
    private const uint transitionSalt = 0x0F2C7B4Du;
    private const uint variantSalt = 0xB5297A4Du;
    private const uint warpXSalt = 0x68E31DA4u;
    private const uint warpYSalt = 0x1B56C4E9u;

    public static FloorTileSelection SelectFloor(FloorGenerationSettings settings, int2 worldCell)
    {
        float2 warpedCell = worldCell + GetBoundaryWarp(settings, worldCell);
        float regionSizeInCells = settings.BiomeRegionSizeInChunks * GameConstants.chunkSize;
        float2 regionCoordinate = warpedCell / regionSizeInCells;
        int2 currentRegion = (int2)math.floor(regionCoordinate);
        int currentBiomeIndex = SelectBiomeIndex(settings, currentRegion);
        int selectedBiomeIndex = SelectTransitionBiomeIndex(
            settings,
            worldCell,
            regionCoordinate,
            currentRegion,
            currentBiomeIndex,
            regionSizeInCells);
        int variantIndex = SelectVariantIndex(settings, selectedBiomeIndex, worldCell);

        return new FloorTileSelection(selectedBiomeIndex, variantIndex);
    }

    private static int SelectTransitionBiomeIndex(
        FloorGenerationSettings settings,
        int2 worldCell,
        float2 regionCoordinate,
        int2 currentRegion,
        int currentBiomeIndex,
        float regionSizeInCells)
    {
        if (settings.TransitionWidthInChunks == 0)
            return currentBiomeIndex;

        float2 regionFraction = regionCoordinate - math.floor(regionCoordinate);
        float2 distanceToBoundary = math.min(regionFraction, 1f - regionFraction) * regionSizeInCells;
        bool isVerticalBoundary = distanceToBoundary.x <= distanceToBoundary.y;
        float boundaryDistance = isVerticalBoundary ? distanceToBoundary.x : distanceToBoundary.y;
        float halfTransitionWidthInCells = settings.TransitionWidthInChunks * GameConstants.chunkSize * 0.5f;

        if (boundaryDistance >= halfTransitionWidthInCells)
            return currentBiomeIndex;

        int2 neighboringRegion = currentRegion;

        if (isVerticalBoundary)
            neighboringRegion.x += regionFraction.x < 0.5f ? -1 : 1;
        else
            neighboringRegion.y += regionFraction.y < 0.5f ? -1 : 1;

        int neighboringBiomeIndex = SelectBiomeIndex(settings, neighboringRegion);

        if (neighboringBiomeIndex == currentBiomeIndex)
            return currentBiomeIndex;

        float distanceRatio = math.saturate(boundaryDistance / halfTransitionWidthInCells);
        float nearBiomePreference = math.pow(
            SmoothStep(distanceRatio),
            settings.NearBiomePreferenceExponent);
        float currentBiomeProbability = math.lerp(0.5f, 1f, nearBiomePreference);
        float selection = HashToUnitFloat(Hash(settings.WorldSeed, worldCell.x, worldCell.y, transitionSalt));

        return selection <= currentBiomeProbability ? currentBiomeIndex : neighboringBiomeIndex;
    }

    private static int SelectBiomeIndex(FloorGenerationSettings settings, int2 region)
    {
        float selection = HashToUnitFloat(Hash(settings.WorldSeed, region.x, region.y, biomeSalt));
        return SelectWeightedIndex(settings.Biomes, selection, biome => biome.selectionWeight);
    }

    private static int SelectVariantIndex(FloorGenerationSettings settings, int biomeIndex, int2 worldCell)
    {
        FloorBiomeConfigData biome = settings.Biomes[biomeIndex];
        float selection = HashToUnitFloat(Hash(settings.WorldSeed, worldCell.x, worldCell.y, variantSalt + (uint)biomeIndex));

        return SelectWeightedIndex(biome.floorVariants, selection, variant => variant.weight);
    }

    private static float2 GetBoundaryWarp(FloorGenerationSettings settings, int2 worldCell)
    {
        if (settings.BoundaryNoiseAmplitudeInCells <= 0f)
            return float2.zero;

        float2 samplePosition = (float2)worldCell / settings.BoundaryNoiseScaleInCells;
        float x = (SampleValueNoise(settings.WorldSeed, samplePosition, warpXSalt) * 2f - 1f) * settings.BoundaryNoiseAmplitudeInCells;
        float y = (SampleValueNoise(settings.WorldSeed, samplePosition, warpYSalt) * 2f - 1f) * settings.BoundaryNoiseAmplitudeInCells;

        return new float2(x, y);
    }

    private static float SampleValueNoise(uint worldSeed, float2 samplePosition, uint salt)
    {
        int2 minimum = (int2)math.floor(samplePosition);
        float2 fraction = samplePosition - minimum;
        float2 smoothFraction = new float2(SmoothStep(fraction.x), SmoothStep(fraction.y));
        float bottom = math.lerp(
            HashToUnitFloat(Hash(worldSeed, minimum.x, minimum.y, salt)),
            HashToUnitFloat(Hash(worldSeed, minimum.x + 1, minimum.y, salt)),
            smoothFraction.x);
        float top = math.lerp(
            HashToUnitFloat(Hash(worldSeed, minimum.x, minimum.y + 1, salt)),
            HashToUnitFloat(Hash(worldSeed, minimum.x + 1, minimum.y + 1, salt)),
            smoothFraction.x);

        return math.lerp(bottom, top, smoothFraction.y);
    }

    private static int SelectWeightedIndex<T>(IReadOnlyList<T> values, float selection, Func<T, float> getWeight)
    {
        float totalWeight = 0f;

        for (int i = 0; i < values.Count; i++)
            totalWeight += getWeight(values[i]);

        float threshold = selection * totalWeight;
        float accumulatedWeight = 0f;

        for (int i = 0; i < values.Count; i++)
        {
            accumulatedWeight += getWeight(values[i]);

            if (threshold < accumulatedWeight)
                return i;
        }

        return values.Count - 1;
    }

    private static uint Hash(uint worldSeed, int x, int y, uint salt)
    {
        uint hash = worldSeed == 0 ? 1u : worldSeed;
        hash ^= (uint)x * 0x9E3779B9u;
        hash ^= (uint)y * 0x85EBCA6Bu;
        hash ^= salt;
        hash ^= hash >> 16;
        hash *= 0x7FEB352Du;
        hash ^= hash >> 15;
        hash *= 0x846CA68Bu;
        hash ^= hash >> 16;

        return hash == 0 ? 1u : hash;
    }

    private static float HashToUnitFloat(uint hash)
    {
        return (hash & 0x00FFFFFFu) / 16777216f;
    }

    private static float SmoothStep(float value)
    {
        float clamped = math.saturate(value);
        return clamped * clamped * (3f - 2f * clamped);
    }
}
