using Unity.Entities;
using Unity.Mathematics;

public struct PowerConfig : IComponentData
{
}

public struct PowerPoleConfigElement : IBufferElementData
{
    public BuildingTypeEnum buildingType;
    public int2 supplyRange;
    public int2 connectionRange;
}

public struct PowerGeneratorConfigElement : IBufferElementData
{
    public PowerGeneratorTypeEnum generatorType;
    public float maximumGeneration;
}

public struct CoalGeneratorConfig : IComponentData
{
    public float coalEnergyPerItem;
    public int fuelStorageCapacity;
}

public struct PowerConsumerConfigElement : IBufferElementData
{
    public BuildingTypeEnum buildingType;
    public float maximumConsumption;
}
