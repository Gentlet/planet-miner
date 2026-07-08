using Unity.Entities;
using Unity.Mathematics;

public enum BuildingTypeEnum : byte
{
    Belt,
    Miner,
    Craftor,
    Count
}

public struct BuildingType : IComponentData
{
    public BuildingTypeEnum type;
}

public struct BuildingSpawnRequest : IComponentData
{
    public BuildingTypeEnum type;
    public int2 gridPosition;
    public DirectionEnum dir; 
}

public struct BuildingDestroyRequest : IComponentData
{
    public int2 gridPosition;
}

public struct BuildingPrefabElement : IBufferElementData
{
    public BuildingTypeEnum type;
    public Entity prefab;
}
