using System.Text.RegularExpressions;
using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// 역할·목적: 바닥 설정 게시·셀 선택 결정성·검증 실패에 대한 NUnit EditMode 회귀 검증.
/// 입력·검사: Resources 설정/좌표로 공동 게시·청크 경계 선택·잘못된 설정/스프라이트 거부를 검사한다. 실제 바닥 렌더링은 실행하지 않는다.
/// 수명: EcsWorldTestFixture가 각 사례의 독립 World를 준비하고 종료 시 해제한다.
/// </summary>
public class Phase4FloorBiomeGenerationTests : EcsWorldTestFixture
{
    private Entity PublishDefault()
    {
        // Resources 검증과 ECS 게시를 준비한다. 셀 선택은 실제 바닥 표시 결과와 구분한다.
        Assert.IsTrue(WorldGenerationConfigLoader.TryLoadConfigFromResources(
            WorldGenerationConfigLoader.DefaultResourcePath,
            out uint seed, out int initialSize, out var resources, out var floor));
        Assert.IsNotNull(floor);
        return WorldGenerationConfigLoader.PublishConfig(_entityManager, seed, initialSize, resources, floor);
    }

    [Test]
    public void WorldConfig_PublishesFloorOnTheSameEntityAndKeepsLegacyValues()
    {
        Entity entity = PublishDefault();
        var resource = _entityManager.GetComponentData<ResourceGenerationSettings>(entity);
        var floor = _entityManager.GetComponentData<FloorGenerationSettings>(entity);
        var biomes = _entityManager.GetBuffer<FloorBiomeElement>(entity);
        var variants = _entityManager.GetBuffer<FloorVariantElement>(entity);

        Assert.AreEqual(1u, resource.WorldSeed);
        Assert.AreEqual(3, floor.BiomeRegionSizeInChunks);
        Assert.AreEqual(2, floor.TransitionWidthInChunks);
        Assert.AreEqual(24f, floor.BoundaryNoiseScaleInCells);
        Assert.AreEqual(6f, floor.BoundaryNoiseAmplitudeInCells);
        Assert.AreEqual(0.5f, floor.NearBiomePreferenceExponent);
        Assert.AreEqual(3, floor.TransitionVariantCount);
        Assert.AreEqual(2, biomes.Length);
        Assert.AreEqual("Grass", biomes[0].Id.ToString());
        Assert.AreEqual("Dirt", biomes[1].Id.ToString());
        Assert.AreEqual(9, variants.Length);
        Assert.AreEqual("Sprites/Floor/GroundTransition01", variants[0].SpriteResourcePath.ToString());
        Assert.AreEqual("Sprites/Floor/GroundDirt03", variants[8].SpriteResourcePath.ToString());
    }

    [Test]
    public void CellSelection_IsDeterministicAcrossChunkBordersAndIndependentOfOccupancy()
    {
        Entity entity = PublishDefault();
        uint seed = _entityManager.GetComponentData<ResourceGenerationSettings>(entity).WorldSeed;
        var floor = _entityManager.GetComponentData<FloorGenerationSettings>(entity);
        var biomes = _entityManager.GetBuffer<FloorBiomeElement>(entity);
        var variants = _entityManager.GetBuffer<FloorVariantElement>(entity);

        int2[] cells = { new int2(15, 0), new int2(16, 0), new int2(-1, -16), new int2(-16, -17), new int2(48, 1) };
        var first = new FloorTileSelection[cells.Length];
        for (int i = 0; i < cells.Length; i++)
            first[i] = FloorBiomeSampler.SelectFloor(seed, floor, biomes, variants, cells[i]);

        // Occupancy has no input to the sampler; creating a resource on one selected cell cannot change its floor.
        Entity resource = _entityManager.CreateEntity(typeof(GridPosition), typeof(ResourceNode));
        _entityManager.SetComponentData(resource, new GridPosition { Value = cells[1] });
        biomes = _entityManager.GetBuffer<FloorBiomeElement>(entity);
        variants = _entityManager.GetBuffer<FloorVariantElement>(entity);
        for (int i = cells.Length - 1; i >= 0; i--)
        {
            FloorTileSelection next = FloorBiomeSampler.SelectFloor(seed, floor, biomes, variants, cells[i]);
            Assert.AreEqual(first[i].BiomeIndex, next.BiomeIndex);
            Assert.AreEqual(first[i].VariantIndex, next.VariantIndex);
            Assert.AreEqual(first[i].UsesTransitionVariant, next.UsesTransitionVariant);
            FloorVariantElement variant = FloorBiomeSampler.GetVariant(floor, biomes, variants, next);
            Assert.IsNotNull(Resources.Load<Sprite>(variant.SpriteResourcePath.ToString()));
        }
    }

    [Test]
    public void InvalidFloorSettingsOrMissingSprite_FailWithoutPartialPublication()
    {
        string json = Resources.Load<TextAsset>(WorldGenerationConfigLoader.DefaultResourcePath).text;
        string[] invalid =
        {
            json.Replace("\"transitionWidthInChunks\": 2", "\"transitionWidthInChunks\": 4"),
            json.Replace("Sprites/Floor/GroundGrass", "Sprites/Floor/DoesNotExist")
        };
        foreach (string candidate in invalid)
        {
            LogAssert.Expect(LogType.Error, new Regex(".*(Invalid floor generation parameters|Invalid floor variant or missing Sprite).*"));
            Assert.IsFalse(WorldGenerationConfigLoader.TryParseAndValidateJson(
                candidate, out uint seed, out int size, out var resources, out var floor));
            Assert.AreEqual(0u, seed);
            Assert.IsNull(resources);
            Assert.IsNull(floor);
        }
        Assert.AreEqual(0, _entityManager.CreateEntityQuery(typeof(ResourceGenerationSettings)).CalculateEntityCount());
    }
}
