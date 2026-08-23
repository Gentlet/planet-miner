using Unity.Entities;
using Unity.Mathematics;

public enum ItemTypeEnum : byte
{
    None,
    Iron_Ore,
    Copper_Ore,
    Coal,
    Stone,
    Iron,
    Copper,
    Iron_Stick,
    Copper_Stick,
    Drone,
    Count
}

public struct Item : IComponentData
{
    public ItemTypeEnum type;
}

public struct ItemCellChanged : IComponentData, IEnableableComponent
{
}

public struct ItemSpawnRequest : IComponentData
{
    public Entity owner;
    public ItemTypeEnum itemType;
}

public struct StartingItemSpawnRequest : IComponentData
{
    public Entity owner;
    public ItemTypeEnum itemType;
}

public struct WorldItemSpawnRequest : IComponentData
{
    public ItemTypeEnum itemType;
    public int2 gridPosition;
    public bool createRecoveryTask;
}

public struct StoredItem : IComponentData
{
    public Entity owner;
}

public interface IItemStorageElement
{
    Entity ItemEntity { get; }
}

public struct StoredItemElement : IBufferElementData, IItemStorageElement
{
    public Entity itemEntity;
    public ItemTypeEnum type;

    public Entity ItemEntity => itemEntity;
}

public struct ItemPrefabElement : IBufferElementData
{
    public ItemTypeEnum type;
    public Entity prefab;
}
