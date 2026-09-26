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
        // Resources/Config/BuildingRuntimeConfig.json 로드 및 검증
        bool success = BuildingRuntimeConfigLoader.TryLoadConfigFromResources(
            BuildingRuntimeConfigLoader.DefaultResourcePath,
            out var elements);

        Assert.IsTrue(success, "Resources 설정 파일 로드 및 파싱이 성공해야 함");
        Assert.IsNotNull(elements);
        Assert.AreEqual(5, elements.Count, "Belt, Miner, Crafter, Storage, ResearchBuilding 총 5종이어야 함");

        // 각 건물별 스펙 값 검증
        bool foundBelt = false;
        bool foundMiner = false;
        bool foundCrafter = false;
        bool foundStorage = false;
        bool foundResearch = false;

        foreach (var el in elements)
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
        bool success = BuildingRuntimeConfigLoader.TryLoadConfigFromResources(
            BuildingRuntimeConfigLoader.DefaultResourcePath,
            out var elements);
        Assert.IsTrue(success);

        Entity configEntity = BuildingRuntimeConfigLoader.PublishConfig(_entityManager, elements);
        Assert.AreNotEqual(Entity.Null, configEntity);

        // 싱글톤 확인
        var query = _entityManager.CreateEntityQuery(typeof(BuildingRuntimeConfig), typeof(BuildingRuntimeConfigElement));
        Assert.AreEqual(1, query.CalculateEntityCount(), "정확히 1개의 싱글톤 엔티티가 존재해야 함");

        var buffer = _entityManager.GetBuffer<BuildingRuntimeConfigElement>(configEntity);
        Assert.AreEqual(5, buffer.Length);

        // Lookup Utility 조회 검증
        bool foundMiner = BuildingRuntimeConfigLookupUtility.TryGetConfig(buffer, BuildingTypeEnum.Miner, out var minerConfig);
        Assert.IsTrue(foundMiner);
        Assert.AreEqual(0.1f, minerConfig.Speed, 0.001f);

        bool foundStorage = BuildingRuntimeConfigLookupUtility.TryGetConfig(buffer, BuildingTypeEnum.Storage, out var storageConfig);
        Assert.IsTrue(foundStorage);
        Assert.AreEqual(10, storageConfig.StorageCapacity);

        // 미등록 건물 조회 시 false 반환 검증
        bool foundNone = BuildingRuntimeConfigLookupUtility.TryGetConfig(buffer, BuildingTypeEnum.PowerPole, out _);
        Assert.IsFalse(foundNone);
    }

    [Test]
    public void Test03_ValidationFailure_InvalidSpeed_DoesNotPublish()
    {
        LogAssert.Expect(LogType.Error, new Regex(".*invalid speed.*"));

        string invalidJson = @"{
            ""buildings"": [
                { ""buildingType"": ""Miner"", ""speed"": -0.5 }
            ]
        }";

        bool success = BuildingRuntimeConfigLoader.TryParseJson(invalidJson, out var elements);
        Assert.IsFalse(success, "음수 속도는 유효성 검사에서 실패해야 함");
        Assert.IsNull(elements, "검증 실패 시 null을 반환하여 부분 게시를 방지해야 함");
    }

    [Test]
    public void Test04_ValidationFailure_InvalidStorageCapacity_DoesNotPublish()
    {
        LogAssert.Expect(LogType.Error, new Regex(".*invalid storageCapacity.*"));

        // MaxStorageSlots(120)을 초과하는 500 슬롯 설정
        string invalidJson = @"{
            ""buildings"": [
                { ""buildingType"": ""Storage"", ""storageCapacity"": 500 }
            ]
        }";

        bool success = BuildingRuntimeConfigLoader.TryParseJson(invalidJson, out var elements);
        Assert.IsFalse(success, "MaxStorageSlots 초과 슬롯 수는 유효성 검사에서 실패해야 함");
        Assert.IsNull(elements);
    }

    [Test]
    public void Test05_ValidationFailure_DuplicateBuildingType_DoesNotPublish()
    {
        LogAssert.Expect(LogType.Error, new Regex(".*Duplicate buildingType.*"));

        string duplicateJson = @"{
            ""buildings"": [
                { ""buildingType"": ""Crafter"", ""speed"": 1.0 },
                { ""buildingType"": ""Crafter"", ""speed"": 2.0 }
            ]
        }";

        bool success = BuildingRuntimeConfigLoader.TryParseJson(duplicateJson, out var elements);
        Assert.IsFalse(success, "중복 건물 타입은 유효성 검사에서 실패해야 함");
        Assert.IsNull(elements);
    }

    [Test]
    public void Test06_BuildingRuntimeConfigLoadSystem_ExecutesOnceAndPublishes()
    {
        var systemHandle = _world.GetOrCreateSystem(typeof(BuildingRuntimeConfigLoadSystem));

        // 1회 Update 실행
        systemHandle.Update(_world.Unmanaged);

        // 싱글톤 게시 확인
        var query = _entityManager.CreateEntityQuery(typeof(BuildingRuntimeConfig), typeof(BuildingRuntimeConfigElement));
        Assert.AreEqual(1, query.CalculateEntityCount(), "시스템 실행 후 싱글톤 엔티티가 게시되어야 함");

        // 추가 Update 실행해도 중복 생성되지 않음을 확인
        systemHandle.Update(_world.Unmanaged);
        Assert.AreEqual(1, query.CalculateEntityCount(), "중복 실행 시에도 단일 싱글톤만 유지되어야 함");
    }
}
