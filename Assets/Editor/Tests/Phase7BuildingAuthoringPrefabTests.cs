using System.Text.RegularExpressions;
using NUnit.Framework;
using PlanetMiner.Config;
using PlanetMiner.Tests;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Task 7.3.1 건물 Authoring, 프리팹 DB 및 정의 연결 단위/통합 테스트.
/// </summary>
public class Phase7BuildingAuthoringPrefabTests : EcsWorldTestFixture
{
    [Test]
    public void Test01_BuildingConfigLoader_ParsesAndValidatesFootprintsAndMainFacility()
    {
        // 1. Resources/Config/BuildingConfig.json 로드
        bool success = BuildingConfigLoader.TryLoadConfigFromResources(
            BuildingConfigLoader.DefaultResourcePath,
            out var configs,
            out var materials);

        Assert.IsTrue(success, "BuildingConfig.json 로드가 성공해야 함");
        Assert.IsNotNull(configs);
        Assert.AreEqual(11, configs.Count, "Belt, Miner, Crafter, Storage, PowerPole, Splitter, Merger, CoalGenerator, DroneStation, ResearchBuilding, MainFacility 총 11종이어야 함");

        // 2. 각 건물의 Footprint 검증
        bool foundBelt = false;
        bool foundMiner = false;
        bool foundCrafter = false;
        bool foundStorage = false;
        bool foundResearch = false;
        bool foundMainFacility = false;

        foreach (var c in configs)
        {
            switch (c.BuildingType)
            {
                case BuildingTypeEnum.Belt:
                    foundBelt = true;
                    Assert.AreEqual(new int2(1, 1), c.Footprint);
                    break;
                case BuildingTypeEnum.Miner:
                    foundMiner = true;
                    Assert.AreEqual(new int2(2, 2), c.Footprint);
                    break;
                case BuildingTypeEnum.Crafter:
                    foundCrafter = true;
                    Assert.AreEqual(new int2(2, 2), c.Footprint);
                    break;
                case BuildingTypeEnum.Storage:
                    foundStorage = true;
                    Assert.AreEqual(new int2(1, 1), c.Footprint);
                    break;
                case BuildingTypeEnum.ResearchBuilding:
                    foundResearch = true;
                    Assert.AreEqual(new int2(3, 3), c.Footprint);
                    break;
                case BuildingTypeEnum.MainFacility:
                    foundMainFacility = true;
                    Assert.AreEqual(new int2(3, 3), c.Footprint);
                    Assert.AreEqual(50, c.StorageCapacity);
                    break;
            }
        }

        Assert.IsTrue(foundBelt);
        Assert.IsTrue(foundMiner);
        Assert.IsTrue(foundCrafter);
        Assert.IsTrue(foundStorage);
        Assert.IsTrue(foundResearch);
        Assert.IsTrue(foundMainFacility);
    }

    [Test]
    public void Test02_BuildingConfigLookupUtility_TryGetFootprint_ReturnsCorrectDimensions()
    {
        // 1. BuildingConfig 엔티티 및 버퍼 생성
        var configEntity = _entityManager.CreateEntity(typeof(BuildingConfig), typeof(BuildingConfigElement));
        var buffer = _entityManager.GetBuffer<BuildingConfigElement>(configEntity);

        buffer.Add(new BuildingConfigElement(BuildingTypeEnum.Belt, 10f, 0, true, default, new int2(1, 1)));
        buffer.Add(new BuildingConfigElement(BuildingTypeEnum.Miner, 0.1f, 0, true, default, new int2(2, 2)));
        buffer.Add(new BuildingConfigElement(BuildingTypeEnum.Crafter, 1.0f, 0, true, default, new int2(2, 2)));

        // 2. Footprint 조회 검증
        bool foundBelt = BuildingConfigLookupUtility.TryGetFootprint(buffer, BuildingTypeEnum.Belt, out var beltSize);
        Assert.IsTrue(foundBelt);
        Assert.AreEqual(new int2(1, 1), beltSize);

        bool foundMiner = BuildingConfigLookupUtility.TryGetFootprint(buffer, BuildingTypeEnum.Miner, out var minerSize);
        Assert.IsTrue(foundMiner);
        Assert.AreEqual(new int2(2, 2), minerSize);

        // 3. 미등록 건물 조회 시 false 반환 검증
        bool foundNone = BuildingConfigLookupUtility.TryGetFootprint(buffer, BuildingTypeEnum.PowerPole, out var noneSize);
        Assert.IsFalse(foundNone);
        Assert.AreEqual(int2.zero, noneSize);
        Assert.AreEqual(int2.zero, noneSize);
    }

    [Test]
    public void BuildingConfigLookup_UnlockStateAndMissingType_ArePreserved()
    {
        var configEntity = _entityManager.CreateEntity(typeof(BuildingConfigElement));
        var buffer = _entityManager.GetBuffer<BuildingConfigElement>(configEntity);
        buffer.Add(new BuildingConfigElement(BuildingTypeEnum.Belt, 10f, 0, true));
        buffer.Add(new BuildingConfigElement(BuildingTypeEnum.Miner, 0.1f, 0, false));

        Assert.IsTrue(BuildingConfigLookupUtility.IsBuildingUnlocked(buffer, BuildingTypeEnum.Belt));
        Assert.IsFalse(BuildingConfigLookupUtility.IsBuildingUnlocked(buffer, BuildingTypeEnum.Miner));
        Assert.IsFalse(BuildingConfigLookupUtility.IsBuildingUnlocked(buffer, BuildingTypeEnum.PowerPole));
        Assert.IsFalse(BuildingConfigLookupUtility.TryGetConfig(buffer, BuildingTypeEnum.PowerPole, out BuildingConfigElement missing));
        Assert.AreEqual(default(BuildingConfigElement), missing);

        Assert.IsTrue(BuildingConfigLookupUtility.SetBuildingUnlocked(ref buffer, BuildingTypeEnum.Miner, true));
        Assert.IsTrue(BuildingConfigLookupUtility.IsBuildingUnlocked(buffer, BuildingTypeEnum.Miner));
    }

    [Test]
    public void Test03_BuildingPrefabDatabase_RegistersStaticFootprintAndExcludesRuntimeState()
    {
        var prefabEntity = _entityManager.CreateEntity(typeof(Prefab));

        // DB 버퍼에 건물 프리팹 및 Footprint 규격 등록
        var dbEntity = _entityManager.CreateEntity(typeof(BuildingPrefabDatabase));
        var buffer = _entityManager.AddBuffer<BuildingPrefabElement>(dbEntity);
        buffer.Add(new BuildingPrefabElement(BuildingTypeEnum.Crafter, prefabEntity, new int2(2, 2)));

        // 검증: DB 버퍼에 Type, Prefab, FootprintSize가 정상 등록됨
        Assert.AreEqual(1, buffer.Length);
        Assert.AreEqual(BuildingTypeEnum.Crafter, buffer[0].Type);
        Assert.AreEqual(prefabEntity, buffer[0].Prefab);
        Assert.AreEqual(new int2(2, 2), buffer[0].FootprintSize);

        // 런타임 가변 상태는 프리팹 원본에 포함되지 않아야 함 (소유권 경계 원칙)
        Assert.IsFalse(_entityManager.HasComponent<GridPosition>(prefabEntity));
        Assert.IsFalse(_entityManager.HasComponent<Direction>(prefabEntity));
    }

    [Test]
    public void Test04_BuildingPrefabLookupUtility_FindsRegisteredBuildings()
    {
        var dbEntity = _entityManager.CreateEntity(typeof(BuildingPrefabDatabase));
        var buffer = _entityManager.AddBuffer<BuildingPrefabElement>(dbEntity);

        var beltMock = _entityManager.CreateEntity(typeof(Prefab));
        var minerMock = _entityManager.CreateEntity(typeof(Prefab));

        buffer.Add(new BuildingPrefabElement(BuildingTypeEnum.Belt, beltMock, new int2(1, 1)));
        buffer.Add(new BuildingPrefabElement(BuildingTypeEnum.Miner, minerMock, new int2(2, 2)));

        bool foundBelt = PrefabLookupUtility.TryGetBuildingPrefab(buffer, BuildingTypeEnum.Belt, out var beltPrefab, out var beltSize);
        Assert.IsTrue(foundBelt);
        Assert.AreEqual(beltMock, beltPrefab);
        Assert.AreEqual(new int2(1, 1), beltSize);

        bool foundMiner = PrefabLookupUtility.TryGetBuildingPrefab(buffer, BuildingTypeEnum.Miner, out var minerPrefab, out var minerSize);
        Assert.IsTrue(foundMiner);
        Assert.AreEqual(minerMock, minerPrefab);
        Assert.AreEqual(new int2(2, 2), minerSize);
    }

    [Test]
    public void Test05_BuildingPrefabLookupUtility_MissingOrNullPrefab_ReturnsFalse_StrictFail()
    {
        var dbEntity = _entityManager.CreateEntity(typeof(BuildingPrefabDatabase));
        var buffer = _entityManager.AddBuffer<BuildingPrefabElement>(dbEntity);

        // Entity.Null이 등록된 비정상 항목
        buffer.Add(new BuildingPrefabElement(BuildingTypeEnum.Storage, Entity.Null, new int2(1, 1)));

        // 1. 미등록 건물 조회
        bool foundCrafter = PrefabLookupUtility.TryGetBuildingPrefab(buffer, BuildingTypeEnum.Crafter, out var crafterPrefab, out var crafterSize);
        Assert.IsFalse(foundCrafter);
        Assert.AreEqual(Entity.Null, crafterPrefab);
        Assert.AreEqual(int2.zero, crafterSize);

        // 2. Entity.Null 등록 건물 조회
        bool foundStorage = PrefabLookupUtility.TryGetBuildingPrefab(buffer, BuildingTypeEnum.Storage, out var storagePrefab, out var storageSize);
        Assert.IsFalse(foundStorage);
        Assert.AreEqual(Entity.Null, storagePrefab);
    }

    [Test]
    public void Test06_PopulateFromResources_LoadsAllExistingBuildingPrefabs()
    {
        var go = new GameObject("TestBuildingPrefabAuthoring");
        var authoring = go.AddComponent<BuildingPrefabDatabaseAuthoring>();

        authoring.PopulateFromResources();

        // Resources/Prefabs/Building에 11개의 프리팹이 있으므로 11개가 파퓰레이트되어야 함
        Assert.AreEqual(11, authoring.Prefabs.Count);

        // 주요 건물 Footprint 확인
        foreach (var entry in authoring.Prefabs)
        {
            Assert.IsNotNull(entry.Prefab, $"{entry.Type} 프리팹 GameObject가 null이 아니어야 함");
            switch (entry.Type)
            {
                case BuildingTypeEnum.Belt:
                    Assert.AreEqual(new Vector2Int(1, 1), entry.Size);
                    break;
                case BuildingTypeEnum.Miner:
                    Assert.AreEqual(new Vector2Int(2, 2), entry.Size);
                    break;
                case BuildingTypeEnum.Crafter:
                    Assert.AreEqual(new Vector2Int(2, 2), entry.Size);
                    break;
                case BuildingTypeEnum.Storage:
                    Assert.AreEqual(new Vector2Int(1, 1), entry.Size);
                    break;
                case BuildingTypeEnum.ResearchBuilding:
                    Assert.AreEqual(new Vector2Int(3, 3), entry.Size);
                    break;
                case BuildingTypeEnum.MainFacility:
                    Assert.AreEqual(new Vector2Int(3, 3), entry.Size);
                    break;
            }
        }

        Object.DestroyImmediate(go);
    }

    [Test]
    public void Test07_BuildingConfigLoader_InvalidFootprint_FailsValidation()
    {
        LogAssert.Expect(LogType.Error, new Regex(".*invalid footprint.*"));

        string invalidJson = @"{
            ""buildings"": [
                { ""buildingType"": ""Miner"", ""speed"": 1.0, ""storageCapacity"": 0, ""isUnlockedByDefault"": true, ""footprint"": [0, 2] }
            ]
        }";

        bool success = BuildingConfigLoader.TryParseJson(invalidJson, out var configs, out var materials);
        Assert.IsFalse(success, "가로 또는 세로가 0 이하인 footprint는 유효성 검사에서 실패해야 함");
        Assert.IsNull(configs);
    }
}
