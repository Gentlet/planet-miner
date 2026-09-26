using System.Text.RegularExpressions;
using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Entities;
using UnityEngine;
using UnityEngine.TestTools;

public class Phase4WorldGenerationConfigTests : EcsWorldTestFixture
{
    [Test]
    public void Test01_LoadFromResources_ParsesAndValidatesAllResourceTypes()
    {
        // Resources/Config/WorldGenerationConfig.json 로드 및 검증
        bool success = WorldGenerationConfigLoader.TryLoadConfigFromResources(
            WorldGenerationConfigLoader.DefaultResourcePath,
            out uint worldSeed,
            out var elements);

        Assert.IsTrue(success, "Resources 설정 파일 로드 및 파싱이 성공해야 함");
        Assert.AreEqual(1u, worldSeed, "기본 worldSeed는 1이어야 함");
        Assert.IsNotNull(elements);
        Assert.AreEqual(4, elements.Count, "Iron_Ore, Copper_Ore, Coal, Stone 총 4종이어야 함");

        bool foundIron = false;
        bool foundCopper = false;
        bool foundCoal = false;
        bool foundStone = false;

        foreach (var el in elements)
        {
            switch (el.ResourceType)
            {
                case ItemTypeEnum.Iron_Ore:
                    foundIron = true;
                    Assert.AreEqual(0.35f, el.Weight, 0.001f);
                    Assert.AreEqual(100, el.MinAmount);
                    Assert.AreEqual(300, el.MaxAmount);
                    break;
                case ItemTypeEnum.Copper_Ore:
                    foundCopper = true;
                    Assert.AreEqual(0.30f, el.Weight, 0.001f);
                    break;
                case ItemTypeEnum.Coal:
                    foundCoal = true;
                    Assert.AreEqual(0.25f, el.Weight, 0.001f);
                    Assert.AreEqual(120, el.MinAmount);
                    Assert.AreEqual(350, el.MaxAmount);
                    break;
                case ItemTypeEnum.Stone:
                    foundStone = true;
                    Assert.AreEqual(0.30f, el.Weight, 0.001f);
                    Assert.AreEqual(80, el.MinAmount);
                    Assert.AreEqual(250, el.MaxAmount);
                    break;
            }
        }

        Assert.IsTrue(foundIron, "Iron_Ore 설정이 존재해야 함");
        Assert.IsTrue(foundCopper, "Copper_Ore 설정이 존재해야 함");
        Assert.IsTrue(foundCoal, "Coal 설정이 존재해야 함");
        Assert.IsTrue(foundStone, "Stone 설정이 존재해야 함");
    }

    [Test]
    public void Test02_PublishConfig_CreatesSingletonEntityAndBuffer()
    {
        // 설정 로드 후 ECS 월드에 게시
        bool success = WorldGenerationConfigLoader.TryLoadConfigFromResources(
            WorldGenerationConfigLoader.DefaultResourcePath,
            out uint worldSeed,
            out var elements);
        Assert.IsTrue(success);

        Entity configEntity = WorldGenerationConfigLoader.PublishConfig(_entityManager, worldSeed, elements);
        Assert.AreNotEqual(Entity.Null, configEntity);

        // 싱글톤 확인
        var query = _entityManager.CreateEntityQuery(typeof(ResourceGenerationSettings), typeof(ResourceGenerationConfigElement));
        Assert.AreEqual(1, query.CalculateEntityCount(), "정확히 1개의 싱글톤 엔티티가 존재해야 함");

        var settings = _entityManager.GetComponentData<ResourceGenerationSettings>(configEntity);
        Assert.AreEqual(1u, settings.WorldSeed);

        var buffer = _entityManager.GetBuffer<ResourceGenerationConfigElement>(configEntity);
        Assert.AreEqual(4, buffer.Length);

        // Lookup Utility 조회 검증
        bool foundIron = ResourceGenerationConfigLookupUtility.TryGetConfig(buffer, ItemTypeEnum.Iron_Ore, out var ironConfig);
        Assert.IsTrue(foundIron);
        Assert.AreEqual(0.35f, ironConfig.Weight, 0.001f);

        bool foundCoal = ResourceGenerationConfigLookupUtility.TryGetConfig(buffer, ItemTypeEnum.Coal, out var coalConfig);
        Assert.IsTrue(foundCoal);
        Assert.AreEqual(120, coalConfig.MinAmount);

        // 미등록 품목(예: Drone) 조회 시 false 반환 검증
        bool foundDrone = ResourceGenerationConfigLookupUtility.TryGetConfig(buffer, ItemTypeEnum.Drone, out _);
        Assert.IsFalse(foundDrone);
    }

    [Test]
    public void Test03_ValidationFailure_ZeroWorldSeed_DoesNotPublish()
    {
        LogAssert.Expect(LogType.Error, new Regex(".*WorldSeed cannot be 0.*"));

        string invalidJson = @"{
            ""worldSeed"": 0,
            ""configs"": [
                {
                    ""type"": ""Iron_Ore"",
                    ""weight"": 0.35,
                    ""minPatchRadius"": 1,
                    ""maxPatchRadius"": 2,
                    ""cellFillChance"": 0.1,
                    ""minAmount"": 100,
                    ""maxAmount"": 300
                }
            ]
        }";

        bool success = WorldGenerationConfigLoader.TryParseAndValidateJson(invalidJson, out uint seed, out var elements);
        Assert.IsFalse(success, "시드 0은 유효성 검사에서 실패해야 함");
        Assert.IsNull(elements, "검증 실패 시 null을 반환하여 부분 게시를 방지해야 함");
    }

    [Test]
    public void Test04_ValidationFailure_InvalidResourceRanges_DoesNotPublish()
    {
        LogAssert.Expect(LogType.Error, new Regex(".*Invalid amount.*"));

        // minAmount(500) > maxAmount(200) 설정
        string invalidJson = @"{
            ""worldSeed"": 1,
            ""configs"": [
                {
                    ""type"": ""Iron_Ore"",
                    ""weight"": 0.35,
                    ""minPatchRadius"": 1,
                    ""maxPatchRadius"": 2,
                    ""cellFillChance"": 0.1,
                    ""minAmount"": 500,
                    ""maxAmount"": 200
                }
            ]
        }";

        bool success = WorldGenerationConfigLoader.TryParseAndValidateJson(invalidJson, out uint seed, out var elements);
        Assert.IsFalse(success, "minAmount > maxAmount는 유효성 검사에서 실패해야 함");
        Assert.IsNull(elements);
    }

    [Test]
    public void Test05_ValidationFailure_DuplicateResourceType_DoesNotPublish()
    {
        LogAssert.Expect(LogType.Error, new Regex(".*Duplicate config definition.*"));

        string duplicateJson = @"{
            ""worldSeed"": 1,
            ""configs"": [
                {
                    ""type"": ""Iron_Ore"",
                    ""weight"": 0.35,
                    ""minPatchRadius"": 1,
                    ""maxPatchRadius"": 2,
                    ""cellFillChance"": 0.1,
                    ""minAmount"": 100,
                    ""maxAmount"": 300
                },
                {
                    ""type"": ""Iron_Ore"",
                    ""weight"": 0.50,
                    ""minPatchRadius"": 1,
                    ""maxPatchRadius"": 2,
                    ""cellFillChance"": 0.1,
                    ""minAmount"": 100,
                    ""maxAmount"": 300
                }
            ]
        }";

        bool success = WorldGenerationConfigLoader.TryParseAndValidateJson(duplicateJson, out uint seed, out var elements);
        Assert.IsFalse(success, "중복 자원 타입은 유효성 검사에서 실패해야 함");
        Assert.IsNull(elements);
    }

    [Test]
    public void Test06_WorldGenerationConfigLoadSystem_ExecutesOnceAndPublishes()
    {
        var systemHandle = _world.GetOrCreateSystem(typeof(WorldGenerationConfigLoadSystem));

        // 1회 Update 실행
        systemHandle.Update(_world.Unmanaged);

        // 싱글톤 게시 확인
        var query = _entityManager.CreateEntityQuery(typeof(ResourceGenerationSettings), typeof(ResourceGenerationConfigElement));
        Assert.AreEqual(1, query.CalculateEntityCount(), "시스템 실행 후 싱글톤 엔티티가 게시되어야 함");

        // 추가 Update 실행해도 중복 생성되지 않음을 확인
        systemHandle.Update(_world.Unmanaged);
        Assert.AreEqual(1, query.CalculateEntityCount(), "중복 실행 시에도 단일 싱글톤만 유지되어야 함");
    }
}
