using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Entities;
using UnityEngine;
using UnityEngine.TestTools;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

public class DroneConfigurationTests : EcsWorldTestFixture
{
    [TestCase(BuildingTypeEnum.Storage, false)]
    [TestCase(BuildingTypeEnum.Storage, true)]
    [TestCase(BuildingTypeEnum.CoalGenerator, false)]
    [TestCase(BuildingTypeEnum.CoalGenerator, true)]
    [TestCase(BuildingTypeEnum.DroneStation, false)]
    [TestCase(BuildingTypeEnum.DroneStation, true)]
    public void GeneralStorageCapacity_RejectsCoercedStringOrFraction(BuildingTypeEnum type, bool fraction)
    {
        var json = JObject.Parse(Resources.Load<TextAsset>(BuildingConfigLoader.DefaultResourcePath).text);
        foreach (JObject entry in (JArray)json["buildings"])
        {
            if (entry["buildingType"].Value<string>() == type.ToString())
            {
                entry["storageCapacity"] = fraction ? new JValue(20.5) : new JValue("20");
            }
        }
        LogAssert.Expect(LogType.Error, new Regex("BuildingConfigLoader.*storageCapacity"));
        Assert.IsFalse(BuildingConfigLoader.TryParseJson(json.ToString(), out var configs, out var materials));
        Assert.IsNull(configs);
        Assert.IsNull(materials);
    }

    [TestCase(BuildingTypeEnum.Storage, 0)]
    [TestCase(BuildingTypeEnum.CoalGenerator, 0)]
    [TestCase(BuildingTypeEnum.Storage, 121)]
    [TestCase(BuildingTypeEnum.DroneStation, 0)]
    [TestCase(BuildingTypeEnum.MainFacility, 0)]
    public void InvalidGeneralCapacity_IsRejectedAtPublishAndPreregistration(BuildingTypeEnum type, int invalid)
    {
        Assert.IsTrue(BuildingConfigLoader.TryLoadConfigFromResources(BuildingConfigLoader.DefaultResourcePath, out var configs, out var materials));
        int index = configs.FindIndex(row => row.BuildingType == type);
        var original = configs[index];
        var bad = original;
        bad.StorageCapacity = invalid;
        configs[index] = bad;
        var invalidMessage = new Regex(type == BuildingTypeEnum.CoalGenerator
            ? "BuildingConfigLoader.*fuel slots" : "BuildingConfigLoader.*storage capacity");
        LogAssert.Expect(LogType.Error, invalidMessage);
        Assert.AreEqual(Entity.Null, BuildingConfigLoader.PublishConfig(_entityManager, configs, materials));
        using var query = _entityManager.CreateEntityQuery(typeof(BuildingConfig));
        Assert.AreEqual(0, query.CalculateEntityCount());
        configs[index] = original;
        Entity entity = BuildingConfigLoader.PublishConfig(_entityManager, configs, materials);
        var buffer = _entityManager.GetBuffer<BuildingConfigElement>(entity);
        buffer[index] = bad;
        LogAssert.Expect(LogType.Error, invalidMessage);
        LogAssert.Expect(LogType.Error, new Regex("BuildingConfigInitSystem.*Game simulation stopped"));
        _world.GetOrCreateSystemManaged<BuildingConfigInitSystem>();
        Assert.IsTrue(SimulationFailureUtility.HasFatalError(_entityManager));
        Assert.AreEqual(entity, query.GetSingletonEntity());
        Assert.AreEqual(invalid, _entityManager.GetBuffer<BuildingConfigElement>(entity)[index].StorageCapacity);
    }

    [TestCase("missing")]
    [TestCase("fraction")]
    [TestCase("overflow")]
    [TestCase("unrelated")]
    public void InvalidStationCapacity_RejectsWholeBuildingConfig(string fault)
    {
        var json = JObject.Parse(Resources.Load<TextAsset>(BuildingConfigLoader.DefaultResourcePath).text);
        var entries = (JArray)json["buildings"];
        JObject station = null;
        foreach (JObject entry in entries)
        {
            if (entry["buildingType"].Value<string>() == "DroneStation") station = entry;
        }
        if (fault == "missing") station.Remove("droneStationStorageCapacity");
        if (fault == "fraction") station["droneStationStorageCapacity"] = 1.5;
        if (fault == "overflow") station["droneStationStorageCapacity"] = 120;
        if (fault == "unrelated") entries[0]["droneStationStorageCapacity"] = 5;
        LogAssert.Expect(LogType.Error, new Regex("BuildingConfigLoader.*(capacity|Capacity)"));
        Assert.IsFalse(BuildingConfigLoader.TryParseJson(json.ToString(), out var configs, out var materials));
        Assert.IsNull(configs);
        Assert.IsNull(materials);
    }

    [Test]
    public void DroneConfigPublishesOnce_AndPreservesExistingCapacity()
    {
        Entity capacity = _entityManager.CreateEntity(typeof(DroneCapacityState));
        _entityManager.SetComponentData(capacity, new DroneCapacityState { CarryingCapacity = 8 });
        var system = _world.GetOrCreateSystemManaged<DroneConfigInitializationSystem>();
        system.Update();
        Assert.IsFalse(system.Enabled);
        Assert.AreEqual(8, _entityManager.GetComponentData<DroneCapacityState>(capacity).CarryingCapacity);
        using var batteries = _entityManager.CreateEntityQuery(typeof(DroneBatteryConfig));
        Assert.AreEqual(100f, batteries.GetSingleton<DroneBatteryConfig>().MaximumCharge);
        using var movement=_entityManager.CreateEntityQuery(typeof(DroneMovementConfig));
        var settings=movement.GetSingleton<DroneMovementConfig>();
        Assert.AreEqual(4f,settings.Speed);
        Assert.AreEqual(0.5f,settings.EmergencySpeed);
        Assert.AreEqual(1f,settings.EnergyPerDistance);
        using var charging = _entityManager.CreateEntityQuery(typeof(DroneChargingConfig));
        Assert.AreEqual(10f, charging.GetSingleton<DroneChargingConfig>().ChargePerSecond);
        Assert.AreEqual(5f, charging.GetSingleton<DroneChargingConfig>().PowerPerDrone);
        Assert.IsTrue(DroneConfigInitializationSystem.Initialize(_entityManager, "invalid ignored after valid publication"));
        using var capacities = _entityManager.CreateEntityQuery(typeof(DroneCapacityState));
        Assert.AreEqual(1, capacities.CalculateEntityCount());
    }

    [TestCase("movementSpeed", "missing")]
    [TestCase("movementSpeed", "zero")]
    [TestCase("movementSpeed", "string")]
    [TestCase("emergencyMovementSpeed", "missing")]
    [TestCase("emergencyMovementSpeed", "zero")]
    [TestCase("emergencyMovementSpeed", "string")]
    [TestCase("batteryConsumptionPerDistance", "missing")]
    [TestCase("batteryConsumptionPerDistance", "zero")]
    [TestCase("batteryConsumptionPerDistance", "string")]
    [TestCase("chargingSpeed", "missing")]
    [TestCase("chargingSpeed", "zero")]
    [TestCase("chargingSpeed", "string")]
    [TestCase("chargingPowerConsumptionPerDrone", "missing")]
    [TestCase("chargingPowerConsumptionPerDrone", "zero")]
    [TestCase("chargingPowerConsumptionPerDrone", "string")]
    public void InvalidMovementOrChargingInput_DoesNotPublishPartialConfiguration(string field, string fault)
    {
        var json = JObject.Parse(Resources.Load<TextAsset>(DroneConfigInitializationSystem.ResourcePath).text);
        if (fault == "missing")
        {
            json.Remove(field);
        }
        if (fault == "zero")
        {
            json[field] = 0;
        }
        if (fault == "string")
        {
            json[field] = "1";
        }
        LogAssert.Expect(LogType.Error, new Regex("DroneConfigInitializationSystem"));
        Assert.IsFalse(DroneConfigInitializationSystem.Initialize(_entityManager, json.ToString()));
        using var capacities = _entityManager.CreateEntityQuery(typeof(DroneCapacityState));
        using var batteries = _entityManager.CreateEntityQuery(typeof(DroneBatteryConfig));
        using var movements = _entityManager.CreateEntityQuery(typeof(DroneMovementConfig));
        using var charging = _entityManager.CreateEntityQuery(typeof(DroneChargingConfig));
        Assert.AreEqual(0, capacities.CalculateEntityCount());
        Assert.AreEqual(0, batteries.CalculateEntityCount());
        Assert.AreEqual(0, movements.CalculateEntityCount());
        Assert.AreEqual(0, charging.CalculateEntityCount());
        Assert.IsTrue(SimulationFailureUtility.HasFatalError(_entityManager));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void InvalidExistingCharging_StopsWithoutReplacingPublishedState(bool duplicate)
    {
        Assert.IsTrue(DroneConfigInitializationSystem.Initialize(_entityManager));
        using var query = _entityManager.CreateEntityQuery(typeof(DroneChargingConfig));
        Entity original = query.GetSingletonEntity();
        var config = query.GetSingleton<DroneChargingConfig>();
        if (duplicate)
        {
            _entityManager.SetComponentData(_entityManager.CreateEntity(typeof(DroneChargingConfig)), config);
        }
        else
        {
            config.PowerPerDrone = 0f;
            _entityManager.SetComponentData(original, config);
        }
        LogAssert.Expect(LogType.Error, new Regex("DroneConfigInitializationSystem"));
        Assert.IsFalse(DroneConfigInitializationSystem.Initialize(_entityManager));
        Assert.AreEqual(config.PowerPerDrone, _entityManager.GetComponentData<DroneChargingConfig>(original).PowerPerDrone);
        Assert.AreEqual(duplicate ? 2 : 1, query.CalculateEntityCount());
        Assert.IsTrue(SimulationFailureUtility.HasFatalError(_entityManager));
    }

    [TestCase("{}")]
    [TestCase("invalid")]
    [TestCase("{\"carryingCapacity\":0,\"maximumBattery\":100}")]
    [TestCase("{\"carryingCapacity\":1.5,\"maximumBattery\":100}")]
    [TestCase("{\"carryingCapacity\":3,\"maximumBattery\":-1}")]
    [TestCase("{\"carryingCapacity\":3,\"maximumBattery\":\"100\"}")]
    public void InvalidConfig_PublishesFatalWithoutPartialState(string json)
    {
        LogAssert.Expect(LogType.Error, new Regex("DroneConfigInitializationSystem"));
        Assert.IsFalse(DroneConfigInitializationSystem.Initialize(_entityManager, json));
        using var capacities = _entityManager.CreateEntityQuery(typeof(DroneCapacityState));
        using var batteries = _entityManager.CreateEntityQuery(typeof(DroneBatteryConfig));
        using var fatal = _entityManager.CreateEntityQuery(typeof(SimulationFatalError));
        Assert.AreEqual(0, capacities.CalculateEntityCount());
        Assert.AreEqual(0, batteries.CalculateEntityCount());
        Assert.AreEqual(1, fatal.CalculateEntityCount());
    }

    [TestCase(false)]
    [TestCase(true)]
    public void InvalidExistingCapacity_StopsWithoutReplacingOriginal(bool duplicate)
    {
        Entity original = _entityManager.CreateEntity(typeof(DroneCapacityState));
        _entityManager.SetComponentData(original, new DroneCapacityState { CarryingCapacity = duplicate ? 7 : 0 });
        if (duplicate) _entityManager.CreateEntity(typeof(DroneCapacityState));
        LogAssert.Expect(LogType.Error, new Regex("DroneConfigInitializationSystem"));
        Assert.IsFalse(DroneConfigInitializationSystem.Initialize(_entityManager));
        Assert.AreEqual(duplicate ? 7 : 0, _entityManager.GetComponentData<DroneCapacityState>(original).CarryingCapacity);
        using var batteries = _entityManager.CreateEntityQuery(typeof(DroneBatteryConfig));
        Assert.AreEqual(0, batteries.CalculateEntityCount());
    }
}
