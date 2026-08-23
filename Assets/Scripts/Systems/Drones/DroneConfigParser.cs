using System;
using Unity.Mathematics;
using UnityEngine;

internal static class DroneConfigParser
{
    public static DroneConfig? Parse(string json, string resourcePath)
    {
        DroneConfigFile configFile;

        try
        {
            configFile = JsonUtility.FromJson<DroneConfigFile>(json);
        }
        catch (Exception exception)
        {
            Debug.LogError(
                $"Failed to parse drone config file. Path : Resources/{resourcePath}, Error : {exception.Message}");
            return null;
        }

        if (configFile == null)
        {
            Debug.LogError(
                $"Drone config file is empty or invalid. Path : Resources/{resourcePath}");
            return null;
        }

        if (!DroneTaskPriorityUtility.IsValidNormalPriority(
                configFile.defaultTaskPriority))
        {
            Debug.LogError(
                $"Invalid default drone task priority. Value : {configFile.defaultTaskPriority}, " +
                $"Expected : {DroneTaskPriorityUtility.MinimumNormalPriority}-" +
                $"{DroneTaskPriorityUtility.MaximumNormalPriority}");
            return null;
        }

        if (configFile.stationStorageCapacity <= 0)
        {
            Debug.LogError(
                $"Invalid drone station storage capacity. Value : {configFile.stationStorageCapacity}, " +
                "Expected : greater than 0");
            return null;
        }

        if (configFile.mainStationStartingIronQuantity < 0)
        {
            Debug.LogError(
                $"Invalid main station starting iron quantity. Value : {configFile.mainStationStartingIronQuantity}, " +
                "Expected : 0 or greater");
            return null;
        }

        if (configFile.mainStationStartingCopperQuantity < 0)
        {
            Debug.LogError(
                $"Invalid main station starting copper quantity. Value : {configFile.mainStationStartingCopperQuantity}, " +
                "Expected : 0 or greater");
            return null;
        }

        if (configFile.mainStationStartingIronStickQuantity < 0)
        {
            Debug.LogError(
                $"Invalid main station starting iron stick quantity. Value : {configFile.mainStationStartingIronStickQuantity}, " +
                "Expected : 0 or greater");
            return null;
        }

        if (configFile.mainStationStartingCopperStickQuantity < 0)
        {
            Debug.LogError(
                $"Invalid main station starting copper stick quantity. Value : {configFile.mainStationStartingCopperStickQuantity}, " +
                "Expected : 0 or greater");
            return null;
        }

        if (configFile.mainStationStartingDroneQuantity < 0)
        {
            Debug.LogError(
                $"Invalid main station starting drone quantity. Value : {configFile.mainStationStartingDroneQuantity}, " +
                "Expected : 0 or greater");
            return null;
        }

        if (configFile.stationActivityRangeInChunksX < 0)
        {
            Debug.LogError(
                $"Invalid drone station X activity range. Value : {configFile.stationActivityRangeInChunksX}, " +
                "Expected : 0 or greater");
            return null;
        }

        if (configFile.stationActivityRangeInChunksY < 0)
        {
            Debug.LogError(
                $"Invalid drone station Y activity range. Value : {configFile.stationActivityRangeInChunksY}, " +
                "Expected : 0 or greater");
            return null;
        }

        if (configFile.carryingCapacity <= 0)
        {
            Debug.LogError(
                $"Invalid drone carrying capacity. Value : {configFile.carryingCapacity}, " +
                "Expected : greater than 0");
            return null;
        }

        if (configFile.movementSpeed <= 0f)
        {
            Debug.LogError(
                $"Invalid drone movement speed. Value : {configFile.movementSpeed}, " +
                "Expected : greater than 0");
            return null;
        }

        if (configFile.emergencyMovementSpeed <= 0f)
        {
            Debug.LogError(
                $"Invalid drone emergency movement speed. Value : {configFile.emergencyMovementSpeed}, " +
                "Expected : greater than 0");
            return null;
        }

        if (configFile.maximumBattery <= 0f)
        {
            Debug.LogError(
                $"Invalid drone maximum battery. Value : {configFile.maximumBattery}, " +
                "Expected : greater than 0");
            return null;
        }

        if (configFile.batteryConsumptionPerDistance <= 0f)
        {
            Debug.LogError(
                $"Invalid drone battery consumption. Value : {configFile.batteryConsumptionPerDistance}, " +
                "Expected : greater than 0");
            return null;
        }

        if (configFile.chargingSpeed <= 0f)
        {
            Debug.LogError(
                $"Invalid drone charging speed. Value : {configFile.chargingSpeed}, " +
                "Expected : greater than 0");
            return null;
        }

        if (configFile.chargingPowerConsumptionPerDrone <= 0f)
        {
            Debug.LogError(
                $"Invalid drone charging power consumption. Value : {configFile.chargingPowerConsumptionPerDrone}, " +
                "Expected : greater than 0");
            return null;
        }

        return new DroneConfig
        {
            defaultTaskPriority = configFile.defaultTaskPriority,
            stationStorageCapacity = configFile.stationStorageCapacity,
            mainStationStartingIronQuantity =
                configFile.mainStationStartingIronQuantity,
            mainStationStartingCopperQuantity =
                configFile.mainStationStartingCopperQuantity,
            mainStationStartingIronStickQuantity =
                configFile.mainStationStartingIronStickQuantity,
            mainStationStartingCopperStickQuantity =
                configFile.mainStationStartingCopperStickQuantity,
            mainStationStartingDroneQuantity =
                configFile.mainStationStartingDroneQuantity,
            stationActivityRangeInChunks = new int2(
                configFile.stationActivityRangeInChunksX,
                configFile.stationActivityRangeInChunksY),
            carryingCapacity = configFile.carryingCapacity,
            movementSpeed = configFile.movementSpeed,
            emergencyMovementSpeed = configFile.emergencyMovementSpeed,
            maximumBattery = configFile.maximumBattery,
            batteryConsumptionPerDistance =
                configFile.batteryConsumptionPerDistance,
            chargingSpeed = configFile.chargingSpeed,
            chargingPowerConsumptionPerDrone =
                configFile.chargingPowerConsumptionPerDrone
        };
    }
}
