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

}
