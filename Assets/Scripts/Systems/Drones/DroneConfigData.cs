using System;

[Serializable]
internal sealed class DroneConfigFile
{
    public int defaultTaskPriority;
    public int stationStorageCapacity;
    public int mainStationStartingIronQuantity;
    public int mainStationStartingCopperQuantity;
    public int mainStationStartingIronStickQuantity;
    public int mainStationStartingCopperStickQuantity;
    public int mainStationStartingDroneQuantity;
    public int stationActivityRangeInChunksX;
    public int stationActivityRangeInChunksY;
    public int carryingCapacity;
    public float movementSpeed;
    public float emergencyMovementSpeed;
    public float maximumBattery;
    public float batteryConsumptionPerDistance;
    public float chargingSpeed;
    public float chargingPowerConsumptionPerDrone;
}
