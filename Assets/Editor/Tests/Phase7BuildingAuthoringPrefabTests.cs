using System.Text.RegularExpressions;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using PlanetMiner.Config;
using PlanetMiner.Tests;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// 역할·목적: 건물 설정 검증·해금 조회·Authoring 목록에 대한 NUnit EditMode 회귀 검증.
/// 입력·검사: Loader/lookup과 임시 GameObject로 설정/Resources 참조를 확인하고 사례에서 GameObject를 정리한다. 실제 SubScene 베이킹/건물 가동은 실행하지 않는다.
/// 수명: EcsWorldTestFixture가 각 사례의 독립 World를 준비하고 종료 시 해제한다.
/// </summary>
public class Phase7BuildingAuthoringPrefabTests : EcsWorldTestFixture
{
    [TestCase(null)]
    [TestCase("[]")]
    [TestCase("[0, 1]")]
    [TestCase("[-1, 1]")]
    [TestCase("[1, 1, 1]")]
    [TestCase("[1.5, 1]")]
    [TestCase("[\"2\", 1]")]
    [TestCase("[2147483648, 1]")]
    public void InvalidOrMissingFootprint_RejectsJsonWithoutPublishingLists(string footprintJson)
    {
        string json = Resources.Load<TextAsset>(BuildingConfigLoader.DefaultResourcePath).text;
        var footprintField = new Regex("\"footprint\"\\s*:\\s*\\[[^\\]]*\\],");
        string replacement = footprintJson == null ? string.Empty : "\"footprint\": " + footprintJson + ",";
        json = footprintField.Replace(json, replacement, 1);

        LogAssert.Expect(LogType.Error, new Regex("BuildingConfigLoader.*footprint"));
        Assert.IsFalse(BuildingConfigLoader.TryParseJson(json, out var configs, out var materials));
        Assert.IsNull(configs);
        Assert.IsNull(materials);
    }

    [TestCase("footprint")]
    [TestCase("tier")]
    [TestCase("displayName")]
    [TestCase("duplicate")]
    public void InvalidPreregisteredDefinition_StopsSimulationAtInit_AndPreservesSource(string field)
    {
        Assert.IsTrue(BuildingConfigLoader.TryLoadConfigFromResources(
            BuildingConfigLoader.DefaultResourcePath, out var configs, out var materials));
        Entity configEntity = BuildingConfigLoader.PublishConfig(_entityManager, configs, materials);
        var buffer = _entityManager.GetBuffer<BuildingConfigElement>(configEntity);
        var invalid = buffer[0];
        if (field == "footprint") invalid.Footprint = int2.zero;
        if (field == "tier") invalid.Tier = 0;
        if (field == "displayName") invalid.DisplayName = default;
        buffer[0] = invalid;
        if (field == "duplicate") buffer.Add(invalid);

        LogAssert.Expect(LogType.Error, new Regex("BuildingConfigLoader.*"));
        LogAssert.Expect(LogType.Error, new Regex("BuildingConfigInitSystem.*Game simulation stopped"));
        _world.GetOrCreateSystemManaged<BuildingConfigInitSystem>();

        Assert.IsTrue(SimulationFailureUtility.HasFatalError(_entityManager));
        Assert.IsTrue(_entityManager.Exists(configEntity));
        var preserved = _entityManager.GetBuffer<BuildingConfigElement>(configEntity)[0];
        Assert.AreEqual(invalid.Tier, preserved.Tier);
        Assert.AreEqual(invalid.DisplayName, preserved.DisplayName);
        Assert.AreEqual(invalid.Footprint, preserved.Footprint);
    }

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
            Assert.AreEqual(1, c.Tier);
            Assert.AreEqual(c.BuildingType.ToString(), c.DisplayName.ToString());
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
        buffer.Add(new BuildingConfigElement(BuildingTypeEnum.Belt, 1, "Belt", 10f, 0, true, footprint: new int2(1, 1)));
        buffer.Add(new BuildingConfigElement(BuildingTypeEnum.Miner, 1, "Miner", 0.1f, 0, false, footprint: new int2(2, 2)));

        Assert.IsTrue(BuildingConfigLookupUtility.IsBuildingUnlocked(buffer, BuildingTypeEnum.Belt, 1));
        Assert.IsFalse(BuildingConfigLookupUtility.IsBuildingUnlocked(buffer, BuildingTypeEnum.Miner, 1));
        Assert.IsFalse(BuildingConfigLookupUtility.IsBuildingUnlocked(buffer, BuildingTypeEnum.PowerPole, 1));
        Assert.IsFalse(BuildingConfigLookupUtility.TryGetConfig(buffer, BuildingTypeEnum.PowerPole, 1, out BuildingConfigElement missing));
        Assert.AreEqual(default(BuildingConfigElement), missing);

        Assert.IsTrue(BuildingConfigLookupUtility.SetBuildingUnlocked(ref buffer, BuildingTypeEnum.Miner, 1, true));
        Assert.IsTrue(BuildingConfigLookupUtility.IsBuildingUnlocked(buffer, BuildingTypeEnum.Miner, 1));
    }

    [TestCase("tier", null)]
    [TestCase("tier", "0")]
    [TestCase("tier", "-1")]
    [TestCase("tier", "1.5")]
    [TestCase("tier", "\"1\"")]
    [TestCase("tier", "2147483648")]
    [TestCase("displayName", null)]
    [TestCase("displayName", "\"   \"")]
    [TestCase("displayName", "123")]
    public void InvalidDefinitionMetadata_RejectsJsonWithoutPublishingLists(string field, string value)
    {
        var json = JObject.Parse(Resources.Load<TextAsset>(BuildingConfigLoader.DefaultResourcePath).text);
        var entry = (JObject)json["buildings"][0];
        if (value == null) entry.Remove(field);
        else entry[field] = JToken.Parse(value);
        LogAssert.Expect(LogType.Error, new Regex("BuildingConfigLoader.*"));
        Assert.IsFalse(BuildingConfigLoader.TryParseJson(json.ToString(), out var configs, out var materials));
        Assert.IsNull(configs);
        Assert.IsNull(materials);
    }

    [Test]
    public void DuplicateDefinition_RejectsJsonWithoutPublishingLists()
    {
        var json = JObject.Parse(Resources.Load<TextAsset>(BuildingConfigLoader.DefaultResourcePath).text);
        var rows = (JArray)json["buildings"];
        rows.Add(rows[0].DeepClone());
        LogAssert.Expect(LogType.Error, new Regex("BuildingConfigLoader.*Duplicate building type/tier"));
        Assert.IsFalse(BuildingConfigLoader.TryParseJson(json.ToString(), out var configs, out var materials));
        Assert.IsNull(configs);
        Assert.IsNull(materials);
    }
}
