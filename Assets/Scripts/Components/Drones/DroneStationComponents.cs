using Unity.Entities;
using Unity.Mathematics;

public struct DroneStation : IComponentData
{
    public int2 activityRangeInChunks;
    public bool isMainStation;
}

public struct DroneStationNetwork : IComponentData
{
    public int networkId;
}

public struct StoredDrone : IComponentData
{
    public Entity stationEntity;
}

public struct StoredDroneElement : IBufferElementData
{
    public Entity droneEntity;
}
