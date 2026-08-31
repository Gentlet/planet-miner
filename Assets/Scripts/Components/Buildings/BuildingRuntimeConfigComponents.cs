using Unity.Entities;

public struct BuildingRuntimeConfig : IComponentData
{
}

public struct BuildingRuntimeConfigElement : IBufferElementData
{
    public BuildingTypeEnum buildingType;
    public float speed;
    public int storageCapacity;
}
