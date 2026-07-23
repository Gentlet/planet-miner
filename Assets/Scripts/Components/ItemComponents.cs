using Unity.Entities;

public enum ItemTypeEnum : byte
{
    None,
    Iron_Ore,
    Copper_Ore,
    Coal,
    Stone,
    Iron,
    Count
}

public struct Item : IComponentData
{
    public ItemTypeEnum type;
}

public struct ItemCellChanged : IComponentData, IEnableableComponent
{
}

public struct StoredItem : IComponentData
{
    public Entity owner;
}

public struct StoredItemElement : IBufferElementData
{
    public Entity itemEntity;
    public ItemTypeEnum type;
}

public struct ItemPrefabElement : IBufferElementData
{
    public ItemTypeEnum type;
    public Entity prefab;
}
