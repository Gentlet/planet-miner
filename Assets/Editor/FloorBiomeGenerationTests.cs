using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

public class FloorBiomeGenerationTests
{
    [Test]
    public void SameSeedAndCellAlwaysSelectTheSameFloor()
    {
        FloorGenerationSettings settings = CreateSettings(12345u, 2);
        int2 worldCell = new int2(-37, 88);

        FloorTileSelection first = FloorBiomeSampler.SelectFloor(settings, worldCell);
        FloorTileSelection second = FloorBiomeSampler.SelectFloor(settings, worldCell);

        Assert.That(second.BiomeIndex, Is.EqualTo(first.BiomeIndex));
        Assert.That(second.VariantIndex, Is.EqualTo(first.VariantIndex));
    }

    [Test]
    public void VariantSelectionUsesConfiguredWeights()
    {
        FloorGenerationSettings settings = CreateSettings(12345u, 2);
        var selectedVariants = new HashSet<int>();

        for (int y = -32; y <= 32; y++)
        {
            for (int x = -32; x <= 32; x++)
            {
                FloorTileSelection selection = FloorBiomeSampler.SelectFloor(settings, new int2(x, y));

                if (selection.BiomeIndex == 0)
                    selectedVariants.Add(selection.VariantIndex);
            }
        }

        Assert.That(selectedVariants, Does.Contain(0));
        Assert.That(selectedVariants, Does.Contain(1));
    }

    [Test]
    public void TransitionVariantsAppearWhereDifferentBiomesMeet()
    {
        FloorGenerationSettings settings = CreateSettings(12345u, 2);
        bool foundTransitionVariant = false;

        for (int y = -128; y <= 128 && !foundTransitionVariant; y++)
        {
            for (int x = -128; x <= 128; x++)
            {
                FloorTileSelection selection = FloorBiomeSampler.SelectFloor(settings, new int2(x, y));

                if (!selection.UsesTransitionVariant)
                    continue;

                foundTransitionVariant = true;
                break;
            }
        }

        Assert.That(foundTransitionVariant, Is.True);
    }

    [Test]
    public void InvalidTransitionWidthIsRejected()
    {
        var config = new FloorGenerationConfigFile
        {
            biomeRegionSizeInChunks = 3,
            transitionWidthInChunks = 4,
            boundaryNoiseScaleInCells = 24f,
            boundaryNoiseAmplitudeInCells = 6f,
            nearBiomePreferenceExponent = 0.5f,
            biomes = CreateBiomeConfigs(1)
        };

        bool created = FloorGenerationConfigLoader.TryCreateSettings(config, out _, out string error);

        Assert.That(created, Is.False);
        Assert.That(error, Does.Contain("Transition width"));
    }

    [Test]
    public void InvalidNearBiomePreferenceExponentIsRejected()
    {
        var config = new FloorGenerationConfigFile
        {
            biomeRegionSizeInChunks = 3,
            transitionWidthInChunks = 2,
            boundaryNoiseScaleInCells = 24f,
            boundaryNoiseAmplitudeInCells = 6f,
            nearBiomePreferenceExponent = 0f,
            biomes = CreateBiomeConfigs(1)
        };

        bool created = FloorGenerationConfigLoader.TryCreateSettings(config, out _, out string error);

        Assert.That(created, Is.False);
        Assert.That(error, Does.Contain("preference exponent"));
    }

    [Test]
    public void InitialConfigLoadsAndReferencesFloorSprites()
    {
        bool loaded = FloorGenerationConfigLoader.TryLoad("Config/FloorGenerationConfig", out FloorGenerationSettings settings);

        Assert.That(loaded, Is.True);
        Assert.That(settings.Biomes.Count, Is.EqualTo(2));
        Assert.That(settings.TransitionFloorVariants.Count, Is.EqualTo(3));

        AssertVariantsReferenceSprites(settings.TransitionFloorVariants);

        for (int biomeIndex = 0; biomeIndex < settings.Biomes.Count; biomeIndex++)
            AssertVariantsReferenceSprites(settings.Biomes[biomeIndex].floorVariants);
    }

    private static FloorGenerationSettings CreateSettings(uint worldSeed, int transitionWidthInChunks)
    {
        var config = new FloorGenerationConfigFile
        {
            worldSeed = worldSeed,
            biomeRegionSizeInChunks = 3,
            transitionWidthInChunks = transitionWidthInChunks,
            boundaryNoiseScaleInCells = 24f,
            boundaryNoiseAmplitudeInCells = 6f,
            nearBiomePreferenceExponent = 0.5f,
            transitionFloorVariants = CreateTransitionVariants(),
            biomes = CreateBiomeConfigs(2)
        };

        bool created = FloorGenerationConfigLoader.TryCreateSettings(config, out FloorGenerationSettings settings, out string error);
        Assert.That(created, Is.True, error);
        return settings;
    }

    private static List<FloorBiomeConfigData> CreateBiomeConfigs(int variantCount)
    {
        var grassVariants = new List<FloorVariantConfigData>
        {
            new() { spriteResourcePath = "GroundGrass", weight = 1f }
        };

        if (variantCount > 1)
            grassVariants.Add(new FloorVariantConfigData { spriteResourcePath = "GroundGrassVariant", weight = 1f });

        return new List<FloorBiomeConfigData>
        {
            new()
            {
                id = "Grass",
                selectionWeight = 1f,
                floorVariants = grassVariants
            },
            new()
            {
                id = "Dirt",
                selectionWeight = 1f,
                floorVariants = new List<FloorVariantConfigData>
                {
                    new() { spriteResourcePath = "GroundDirt", weight = 1f }
                }
            }
        };
    }

    private static List<FloorVariantConfigData> CreateTransitionVariants()
    {
        return new List<FloorVariantConfigData>
        {
            new() { spriteResourcePath = "GroundTransition01", weight = 1f },
            new() { spriteResourcePath = "GroundTransition02", weight = 1f },
            new() { spriteResourcePath = "GroundTransition03", weight = 1f }
        };
    }

    private static void AssertVariantsReferenceSprites(IReadOnlyList<FloorVariantConfigData> variants)
    {
        for (int variantIndex = 0; variantIndex < variants.Count; variantIndex++)
        {
            FloorVariantConfigData variant = variants[variantIndex];
            Sprite[] sprites = Resources.LoadAll<Sprite>(variant.spriteResourcePath);

            Assert.That(sprites, Is.Not.Empty, $"Missing floor sprite at Resources/{variant.spriteResourcePath}");
        }
    }
}
