using System.Text.RegularExpressions;
using NUnit.Framework;
using PlanetMiner.Config;
using PlanetMiner.Tests;
using Unity.Entities;
using UnityEngine;
using UnityEngine.TestTools;

public class Phase3BuildingRuntimeConfigTests : EcsWorldTestFixture
{
    [Test]
    public void Test01_LoadFromResources_ParsesAndValidatesAllBuildingTypes()
    {
        // Resources/Config/BuildingConfig.json 로드 및 검증
        bool success = BuildingConfigLoader.TryLoadConfigFromResources(
            BuildingConfigLoader.DefaultResourcePath,
            out var configs,
            out var materials);

        Assert.IsTrue(success, "Resources 설정 파일 로드 및 파싱이 성공해야 함");
        Assert.IsNotNull(configs);
        Assert.AreEqual(10, configs.Count, "통합 설정은 총 10종의 건물 설정을 포함해야 함");

        // 각 건물별 스펙 값 검증
        bool foundBelt = false;
        bool foundMiner = false;
        bool foundCrafter = false;
        bool foundStorage = false;
        bool foundResearch = false;

        foreach (var el in configs)
        {
            switch (el.BuildingType)
            {
                case BuildingTypeEnum.Belt:
                    foundBelt = true;
                    Assert.AreEqual(10.0f, el.Speed, 0.001f);
                    break;
                case BuildingTypeEnum.Miner:
                    foundMiner = true;
                    Assert.AreEqual(0.1f, el.Speed, 0.001f);
                    break;
                case BuildingTypeEnum.Crafter:
                    foundCrafter = true;
                    Assert.AreEqual(1.0f, el.Speed, 0.001f);
                    break;
                case BuildingTypeEnum.Storage:
                    foundStorage = true;
                    Assert.AreEqual(10, el.StorageCapacity);
                    break;
                case BuildingTypeEnum.ResearchBuilding:
                    foundResearch = true;
                    Assert.AreEqual(1.0f, el.Speed, 0.001f);
                    break;
            }
        }

        Assert.IsTrue(foundBelt, "Belt 설정이 존재해야 함");
        Assert.IsTrue(foundMiner, "Miner 설정이 존재해야 함");
        Assert.IsTrue(foundCrafter, "Crafter 설정이 존재해야 함");
        Assert.IsTrue(foundStorage, "Storage 설정이 존재해야 함");
        Assert.IsTrue(foundResearch, "ResearchBuilding 설정이 존재해야 함");
    }

    [Test]
    public void Test02_PublishConfig_CreatesSingletonEntityAndBuffer()
    {
        // 설정 로드 후 ECS 월드에 게시
        bool success = BuildingConfigLoader.TryLoadConfigFromResources(
            BuildingConfigLoader.DefaultResourcePath,
            out var configs,
            out var materials);
        Assert.IsTrue(success);

        Entity configEntity = BuildingConfigLoader.PublishConfig(_entityManager, configs, materials);
        Assert.AreNotEqual(Entity.Null, configEntity);

        // 하위 호환성 싱글톤 및 버퍼 확인
        var query = _entityManager.CreateEntityQuery(typeof(BuildingRuntimeConfig), typeof(BuildingRuntimeConfigElement));
        Assert.AreEqual(1, query.CalculateEntityCount(), "정확히 1개의 싱글톤 엔티티가 존재해야 함");

        var buffer = _entityManager.GetBuffer<BuildingRuntimeConfigElement>(configEntity);
        Assert.AreEqual(10, buffer.Length);

        // Lookup Utility 조회 검증
        bool foundMiner = BuildingRuntimeConfigLookupUtility.TryGetConfig(buffer, BuildingTypeEnum.Miner, out var minerConfig);
        Assert.IsTrue(foundMiner);
        Assert.AreEqual(0.1f, minerConfig.Speed, 0.001f);

        bool foundStorage = BuildingRuntimeConfigLookupUtility.TryGetConfig(buffer, BuildingTypeEnum.Storage, out var storageConfig);
        Assert.IsTrue(foundStorage);
        Assert.AreEqual(10, storageConfig.StorageCapacity);

        // 미등록 건물(ConstructionSite) 조회 시 false 반환 검증
        bool foundNone = BuildingRuntimeConfigLookupUtility.TryGetConfig(buffer, BuildingTypeEnum.ConstructionSite, out _);
        Assert.IsFalse(foundNone);
    }

    [Test]
    public void Test03_ValidationFailure_InvalidSpeed_DoesNotPublish()
    {
        LogAssert.Expect(LogType.Error, new Regex(".*invalid speed.*"));

        string invalidJson = @"{
            ""buildings"": [
                { ""buildingType"": ""Miner"", ""speed"": -0.5, ""storageCapacity"": 0, ""isUnlockedByDefault"": true }
            ]
        }";

        bool success = BuildingConfigLoader.TryParseJson(invalidJson, out var configs, out var materials);
        Assert.IsFalse(success, "음수 속도는 유효성 검사에서 실패해야 함");
        Assert.IsNull(configs, "검증 실패 시 null을 반환하여 부분 게시를 방지해야 함");
    }

    [Test]
    public void Test04_ValidationFailure_InvalidStorageCapacity_DoesNotPublish()
    {
        LogAssert.Expect(LogType.Error, new Regex(".*invalid storageCapacity.*"));

        // MaxStorageSlots(120)을 초과하는 500 슬롯 설정
        string invalidJson = @"{
            ""buildings"": [
                { ""buildingType"": ""Storage"", ""speed"": 1.0, ""storageCapacity"": 500, ""isUnlockedByDefault"": true }
            ]
        }";

        bool success = BuildingConfigLoader.TryParseJson(invalidJson, out var configs, out var materials);
        Assert.IsFalse(success, "MaxStorageSlots 초과 슬롯 수는 유효성 검사에서 실패해야 함");
        Assert.IsNull(configs);
    }

    [Test]
    public void Test05_ValidationFailure_DuplicateBuildingType_DoesNotPublish()
    {
        LogAssert.Expect(LogType.Error, new Regex(".*Duplicate buildingType.*"));

        string duplicateJson = @"{
            ""buildings"": [
                { ""buildingType"": ""Crafter"", ""speed"": 1.0, ""storageCapacity"": 4, ""isUnlockedByDefault"": true },
                { ""buildingType"": ""Crafter"", ""speed"": 2.0, ""storageCapacity"": 4, ""isUnlockedByDefault"": true }
            ]
        }";

        bool success = BuildingConfigLoader.TryParseJson(duplicateJson, out var configs, out var materials);
        Assert.IsFalse(success, "중복 건물 타입은 유효성 검사에서 실패해야 함");
        Assert.IsNull(configs);
    }

    [Test]
    public void Test06_BuildingConfigInitSystem_ExecutesOnceAndPublishes()
    {
        var initSystem = _world.GetOrCreateSystemManaged<BuildingConfigInitSystem>();

        // OnCreate 시점에 이미 LoadAndPublish가 실행되어 싱글톤 게시됨
        var query = _entityManager.CreateEntityQuery(typeof(BuildingConfig), typeof(BuildingConfigElement));
        Assert.AreEqual(1, query.CalculateEntityCount(), "시스템 생성 후 싱글톤 엔티티가 게시되어야 함");

        var runtimeQuery = _entityManager.CreateEntityQuery(typeof(BuildingRuntimeConfig), typeof(BuildingRuntimeConfigElement));
        Assert.AreEqual(1, runtimeQuery.CalculateEntityCount(), "하위 호환성 싱글톤 엔티티가 게시되어야 함");

        // 추가 Update 실행해도 중복 생성되지 않음을 확인
        initSystem.Update();
        Assert.AreEqual(1, query.CalculateEntityCount(), "중복 실행 시에도 단일 싱글톤만 유지되어야 함");
    }
}
