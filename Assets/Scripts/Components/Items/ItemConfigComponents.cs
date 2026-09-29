using Unity.Entities;

/// <summary>
/// 단일 아이템 종류별 정적 데이터 (Unmanaged).
/// </summary>
public struct ItemDataBlob
{
    public ItemTypeEnum ItemType;
    public int MaxStack;

    public ItemDataBlob(ItemTypeEnum itemType, int maxStack)
    {
        ItemType = itemType;
        MaxStack = maxStack;
    }
}

/// <summary>
/// 전역 아이템 레지스트리 불변 루트 Blob.
/// 인덱스가 (int)ItemTypeEnum과 1:1로 대응하여 O(1) 직접 조회가 보장.
/// </summary>
public struct ItemRegistryBlob
{
    public int DefaultMaxStack;
    public BlobArray<ItemDataBlob> Items;

    /// <summary>
    /// ItemType에 해당하는 MaxStack을 O(1) 인덱싱으로 반환.
    /// 범위 밖인 경우 DefaultMaxStack을 반환.
    /// </summary>
    public int GetMaxStack(ItemTypeEnum itemType)
    {
        int index = (int)itemType;
        if (index >= 0 && index < Items.Length)
        {
            return Items[index].MaxStack;
        }
        return DefaultMaxStack;
    }

    /// <summary>
    /// ItemType에 해당하는 MaxStack을 O(1) 인덱싱으로 반환.
    /// 범위 밖인 경우 전달된 fallbackMaxStack을 반환.
    /// </summary>
    public int GetMaxStack(ItemTypeEnum itemType, int fallbackMaxStack)
    {
        int index = (int)itemType;
        if (index >= 0 && index < Items.Length)
        {
            return Items[index].MaxStack;
        }
        return fallbackMaxStack;
    }
}

/// <summary>
/// 전역 아이템 레지스트리 싱글톤 컴포넌트.
/// RecipeRegistry와 대칭을 이루며, BlobAssetReference를 통해 Unmanaged 포인터 접근을 제공.
/// </summary>
public struct ItemRegistry : IComponentData
{
    public BlobAssetReference<ItemRegistryBlob> Value;

    public ItemRegistry(BlobAssetReference<ItemRegistryBlob> value)
    {
        Value = value;
    }
}
