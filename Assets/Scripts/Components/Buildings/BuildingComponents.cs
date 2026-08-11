using Unity.Entities;
using Unity.Mathematics;

public enum BuildingTypeEnum : byte
{
    Belt,
    Miner,
    Crafter,
    Splitter,
    Merger,
    Storage,
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
    public ItemTypeEnum selectedItemType;
}

public struct BuildingDestroyRequest : IComponentData
{
    public int2 gridPosition;
}

public struct BuildingPrefabElement : IBufferElementData
{
    public BuildingTypeEnum type;
    public Entity prefab;
    public int2 size;
}

public struct BuildingOutputCursor : IComponentData
{
    public int nextIndex;
}

public struct ProducedItemElement : IBufferElementData, IItemStorageElement
{
    public Entity itemEntity;
    public ItemTypeEnum type;

    public Entity ItemEntity => itemEntity;
}
