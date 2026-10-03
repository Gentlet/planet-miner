using Unity.Entities;

/// <summary>
/// 품목 번호와 같은 인덱스에 저장하는 정적 아이템 설정. 게시 후 World 종료까지 읽기 전용이다.
/// </summary>
[InternalBufferCapacity(0)]
public struct ItemConfigElement : IBufferElementData
{
    public ItemTypeEnum ItemType;
    public int MaxStack;

    public ItemConfigElement(ItemTypeEnum itemType, int maxStack)
    {
        ItemType = itemType;
        MaxStack = maxStack;
    }
}

/// <summary>
/// ItemConfigElement 버퍼를 소유하는 설정 엔티티. 메모리는 ECS World가 관리한다.
/// </summary>
public struct ItemRegistry : IComponentData
{
    public int DefaultMaxStack;

    public int GetMaxStack(DynamicBuffer<ItemConfigElement> items, ItemTypeEnum itemType)
    {
        return GetMaxStack(items, itemType, DefaultMaxStack);
    }

    public int GetMaxStack(DynamicBuffer<ItemConfigElement> items, ItemTypeEnum itemType, int fallbackMaxStack)
    {
        int index = (int)itemType;
        if (index >= 0 && index < items.Length)
        {
            return items[index].MaxStack;
        }

        return fallbackMaxStack;
    }
}