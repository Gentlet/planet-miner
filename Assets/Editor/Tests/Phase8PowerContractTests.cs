using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using PlanetMiner.Config;
using PlanetMiner.Tests;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>Task 8.1의 통합 설정/원자적 게시/초기화 실패와 순수 범위 계약을 검증한다. 실제 망·연료·생산 실행은 포함하지 않는다.</summary>
public class Phase8PowerContractTests : EcsWorldTestFixture
{
    [Test]
    public void UnifiedConfig_PublishesTierPowerAndOneCoalSlot()
    {
        Assert.IsTrue(BuildingConfigLoader.TryLoadConfigFromResources(
            BuildingConfigLoader.DefaultResourcePath, out var configs, out var materials));
        Entity entity = BuildingConfigLoader.PublishConfig(_entityManager, configs, materials);
        Assert.AreNotEqual(Entity.Null, entity);
        var buffer = _entityManager.GetBuffer<BuildingConfigElement>(entity);
        Assert.IsTrue(BuildingConfigLookupUtility.TryGetConfig(buffer, BuildingTypeEnum.CoalGenerator, 1, out var coal));
        Assert.AreEqual(1, coal.StorageCapacity);
        Assert.AreEqual(100f, coal.Power.MaximumGeneration);
        Assert.AreEqual(600f, coal.Power.CoalEnergyPerItem);
        Assert.IsTrue(BuildingConfigLookupUtility.TryGetConfig(buffer, BuildingTypeEnum.PowerPole, 1, out var pole));
        Assert.AreEqual(5, pole.Power.SupplyRange);
        Assert.AreEqual(8, pole.Power.ConnectionRange);
        Assert.IsTrue(BuildingConfigLookupUtility.TryGetConfig(buffer, BuildingTypeEnum.MainFacility, 1, out var main));
        Assert.AreEqual(0, main.Power.SupplyRange);
        Assert.AreEqual(8, main.Power.ConnectionRange);
        Assert.AreEqual(40f, main.Power.MaximumGeneration);
        Assert.IsTrue(BuildingConfigLookupUtility.TryGetConfig(buffer, BuildingTypeEnum.Miner, 1, out var miner));
        Assert.AreEqual(10f, miner.Power.MaximumConsumption);
        Assert.IsTrue(BuildingConfigLookupUtility.TryGetConfig(buffer, BuildingTypeEnum.Crafter, 1, out var crafter));
        Assert.AreEqual(20f, crafter.Power.MaximumConsumption);
        Assert.IsTrue(BuildingConfigLookupUtility.TryGetConfig(buffer, BuildingTypeEnum.ResearchBuilding, 1, out var research));
        Assert.AreEqual(20f, research.Power.MaximumConsumption);
        Assert.IsTrue(BuildingConfigLoader.ValidatePublishedConfig(_entityManager, entity));
    }

    [Test]
    public void DistinctTierPower_IsNotInheritedOrReplacedByTierOne()
    {
        JObject json = LoadJson();
        var secondTier = (JObject)FindEntry(json, "Miner").DeepClone();
        secondTier["tier"] = 2;
        secondTier["power"]["maximumConsumption"] = 7;
        ((JArray)json["buildings"]).Add(secondTier);
        Assert.IsTrue(BuildingConfigLoader.TryParseJson(json.ToString(), out var configs, out var materials));
        Entity entity = BuildingConfigLoader.PublishConfig(_entityManager, configs, materials);
        var buffer = _entityManager.GetBuffer<BuildingConfigElement>(entity);
        Assert.IsTrue(BuildingConfigLookupUtility.TryGetConfig(buffer, BuildingTypeEnum.Miner, 2, out var tierTwo));
        Assert.AreEqual(7f, tierTwo.Power.MaximumConsumption);
        Assert.IsTrue(BuildingConfigLookupUtility.TryGetConfig(buffer, BuildingTypeEnum.Miner, 1, out var tierOne));
        Assert.AreEqual(10f, tierOne.Power.MaximumConsumption);
        Assert.IsFalse(BuildingConfigLookupUtility.TryGetConfig(buffer, BuildingTypeEnum.Miner, 3, out _));
    }

    [TestCase("missingObject")]
    [TestCase("wrongObject")]
    [TestCase("missingField")]
    [TestCase("stringNumber")]
    [TestCase("zeroConsumption")]
    [TestCase("zeroGeneration")]
    [TestCase("negativeEnergy")]
    [TestCase("nonfiniteEnergy")]
    [TestCase("negativeRange")]
    [TestCase("fractionalRange")]
    [TestCase("rangeOverflow")]
    [TestCase("legacyAxis")]
    [TestCase("legacyFuelCapacity")]
    [TestCase("unexpectedRole")]
    [TestCase("mainSupplyRange")]
    [TestCase("zeroFuelSlots")]
    [TestCase("fractionalFuelSlots")]
    [TestCase("fuelSlotOverflow")]
    [TestCase("negativeCoalSpeed")]
    public void InvalidPowerJson_RejectsAllOutputLists(string fault)
    {
        JObject json = LoadJson();
        JObject miner = FindEntry(json, "Miner");
        JObject coal = FindEntry(json, "CoalGenerator");
        JObject pole = FindEntry(json, "PowerPole");
        switch (fault)
        {
            case "missingObject": miner.Remove("power"); break;
            case "wrongObject": miner["power"] = new JArray(); break;
            case "missingField": ((JObject)miner["power"]).Remove("maximumConsumption"); break;
            case "stringNumber": miner["power"]["maximumConsumption"] = "10"; break;
            case "zeroConsumption": miner["power"]["maximumConsumption"] = 0; break;
            case "zeroGeneration": coal["power"]["maximumGeneration"] = 0; break;
            case "negativeEnergy": coal["power"]["coalEnergyPerItem"] = -1; break;
            case "nonfiniteEnergy": coal["power"]["coalEnergyPerItem"] = new JValue(double.PositiveInfinity); break;
            case "negativeRange": pole["power"]["supplyRange"] = -1; break;
            case "fractionalRange": pole["power"]["supplyRange"] = 5.5; break;
            case "rangeOverflow": pole["power"]["connectionRange"] = (long)int.MaxValue + 1; break;
            case "legacyAxis": pole["power"]["supplyRangeX"] = 5; break;
            case "legacyFuelCapacity": coal["power"]["fuelStorageCapacity"] = 10; break;
            case "unexpectedRole": FindEntry(json, "Storage")["power"] = new JObject(); break;
            case "mainSupplyRange": FindEntry(json, "MainFacility")["power"]["supplyRange"] = 1; break;
            case "zeroFuelSlots": coal["storageCapacity"] = 0; break;
            case "fractionalFuelSlots": coal["storageCapacity"] = 1.5; break;
            case "fuelSlotOverflow": coal["storageCapacity"] = GameConstants.MaxStorageSlots + 1; break;
            case "negativeCoalSpeed": coal["speed"] = -1; break;
        }
        LogAssert.Expect(LogType.Error, new Regex("BuildingConfigLoader.*"));
        Assert.IsFalse(BuildingConfigLoader.TryParseJson(json.ToString(), out var configs, out var materials));
        Assert.IsNull(configs);
        Assert.IsNull(materials);
    }

    [TestCase("missingPower")]
    [TestCase("nonfiniteGeneration")]
    [TestCase("zeroFuelSlots")]
    public void InvalidPublicPower_RejectsWithoutCreatingConfigEntity(string fault)
    {
        var config = new BuildingConfigElement(BuildingTypeEnum.CoalGenerator, 1, "CoalGenerator", 0f, 1, true,
            footprint: new int2(3, 3), power: new BuildingPowerConfig(maximumGeneration: 100f, coalEnergyPerItem: 600f));
        if (fault == "missingPower") config.Power = default;
        if (fault == "nonfiniteGeneration") config.Power.MaximumGeneration = float.NaN;
        if (fault == "zeroFuelSlots") config.StorageCapacity = 0;
        LogAssert.Expect(LogType.Error, new Regex("BuildingConfigLoader.*"));
        Assert.AreEqual(Entity.Null, BuildingConfigLoader.PublishConfig(_entityManager,
            new List<BuildingConfigElement> { config }, new List<BuildingConstructionMaterialElement>()));
        using var query = _entityManager.CreateEntityQuery(typeof(BuildingConfig));
        Assert.AreEqual(0, query.CalculateEntityCount());
    }

    [Test]
    public void InvalidPreregisteredPower_InitStopsAndPreservesSource()
    {
        Assert.IsTrue(BuildingConfigLoader.TryLoadConfigFromResources(
            BuildingConfigLoader.DefaultResourcePath, out var configs, out var materials));
        Entity entity = BuildingConfigLoader.PublishConfig(_entityManager, configs, materials);
        var buffer = _entityManager.GetBuffer<BuildingConfigElement>(entity);
        int index = configs.FindIndex(config => config.BuildingType == BuildingTypeEnum.CoalGenerator);
        var invalid = buffer[index];
        invalid.Power.CoalEnergyPerItem = 0f;
        buffer[index] = invalid;
        LogAssert.Expect(LogType.Error, new Regex("BuildingConfigLoader.*"));
        LogAssert.Expect(LogType.Error, new Regex("BuildingConfigInitSystem.*Game simulation stopped"));
        _world.GetOrCreateSystemManaged<BuildingConfigInitSystem>();
        Assert.IsTrue(SimulationFailureUtility.HasFatalError(_entityManager));
        Assert.IsTrue(_entityManager.Exists(entity));
        Assert.AreEqual(0f, _entityManager.GetBuffer<BuildingConfigElement>(entity)[index].Power.CoalEnergyPerItem);
    }

    [Test]
    public void SquareRange_IncludesBoundaryAndUsesEitherPoleRange()
    {
        Assert.IsTrue(PowerRangeUtility.Contains(int2.zero, 5, new int2(5, 5)));
        Assert.IsFalse(PowerRangeUtility.Contains(int2.zero, 5, new int2(6, 0)));
        Assert.IsTrue(PowerRangeUtility.Contains(int2.zero, 0, int2.zero));
        Assert.IsFalse(PowerRangeUtility.Contains(int2.zero, 0, new int2(1, 0)));
        Assert.IsTrue(PowerRangeUtility.AreCentersConnected(int2.zero, 1, new int2(6, 0), 8));
        Assert.IsFalse(PowerRangeUtility.AreCentersConnected(int2.zero, 1, new int2(9, 0), 8));
        Assert.IsFalse(PowerRangeUtility.Contains(new int2(int.MaxValue, 0), int.MaxValue, new int2(int.MinValue, 0)));
    }

    private static JObject LoadJson()
    {
        var asset = Resources.Load<TextAsset>(BuildingConfigLoader.DefaultResourcePath);
        Assert.IsNotNull(asset);
        return JObject.Parse(asset.text);
    }

    private static JObject FindEntry(JObject json, string type)
    {
        foreach (JObject entry in (JArray)json["buildings"])
        {
            if (entry["buildingType"].Value<string>() == type) return entry;
        }
        Assert.Fail($"Missing fixture building {type}.");
        return null;
    }
}
