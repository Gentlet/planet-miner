using Unity.Entities;

public struct StartingItemConfigElement : IBufferElementData
{
    public ItemTypeEnum itemType;
    public int quantity;
}
