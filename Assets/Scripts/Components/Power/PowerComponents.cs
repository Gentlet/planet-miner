using Unity.Entities;
using Unity.Mathematics;

public enum PowerGeneratorTypeEnum : byte
{
    MainFacility,
    CoalGenerator,
    Count
}

public struct PowerPole : IComponentData
{
    public int stableId;
}

public struct PowerGenerator : IComponentData
{
    public PowerGeneratorTypeEnum type;
    public float maximumGeneration;
    public float currentGeneration;
}

public struct PowerConsumer : IComponentData
{
    public float maximumConsumption;
    public float currentConsumption;
    public float supplyRatio;
}

public struct MainFacility : IComponentData
{
}

public struct PowerGridConnection : IComponentData
{
    public Entity powerPoleEntity;
    public Entity powerGridEntity;
}

public struct PowerGrid : IComponentData
{
    public int stableId;
}

public struct PowerGridState : IComponentData
{
    public float availableGeneration;
    public float maximumDemand;
    public float actualConsumption;
    public float sparePower;
    public float supplyRatio;
    public int connectedBuildingCount;
}

public struct CoalGenerator : IComponentData
{
    public float remainingFuelEnergy;
}

public readonly struct PowerPoleTopologyData
{
    public PowerPoleTopologyData(
        Entity entity,
        int stableId,
        int2 center,
        int2 supplyRange,
        int2 connectionRange)
    {
        Entity = entity;
        StableId = stableId;
        Center = center;
        SupplyRange = supplyRange;
        ConnectionRange = connectionRange;
    }

    public Entity Entity { get; }
    public int StableId { get; }
    public int2 Center { get; }
    public int2 SupplyRange { get; }
    public int2 ConnectionRange { get; }
}
