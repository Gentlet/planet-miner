using Unity.Entities;
using Unity.Mathematics;

public struct DroneConfig : IComponentData
{
    public int defaultTaskPriority;
    public int stationStorageCapacity;
    public int2 stationActivityRangeInChunks;
    public int carryingCapacity;
    public float movementSpeed;
    public float emergencyMovementSpeed;
    public float maximumBattery;
    public float batteryConsumptionPerDistance;
    public float chargingSpeed;
    public float chargingPowerConsumptionPerDrone;
}
