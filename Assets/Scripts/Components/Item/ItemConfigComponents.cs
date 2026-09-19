using Unity.Entities;

/// <summary>
/// 글로벌 아이템 기본 설정 싱글톤 컴포넌트.
/// ItemConfig.json에서 로드된 DefaultMaxStack을 단일 기준(Single Source of Truth)으로 보유합니다.
/// </summary>
public struct ItemConfig : IComponentData
{
    public int DefaultMaxStack;

    public ItemConfig(int defaultMaxStack)
    {
        DefaultMaxStack = defaultMaxStack;
    }
}

/// <summary>
/// 아이템 종류별 최대 스택 수를 저장하는 버퍼 요소 (Unmanaged).
/// 버퍼 인덱스가 (int)ItemTypeEnum과 1:1로 매핑되어 O(1) 직접 조회가 보장됩니다.
/// </summary>
[InternalBufferCapacity((int)ItemTypeEnum.Count)]
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
/// ItemConfig 버퍼 고속 조회를 위한 확장 메서드.
/// Burst 및 멀티스레드 Job에서 호출 가능합니다.
/// </summary>
public static class ItemConfigExtensions
{
    /// <summary>
    /// DynamicBuffer에서 ItemType에 해당하는 MaxStack을 O(1) 인덱싱으로 반환합니다.
    /// 유효 범위를 벗어난 비정상 타입인 경우 ItemConfig.json에서 로드된 config.DefaultMaxStack을 반환합니다.
    /// </summary>
    public static int GetMaxStack(this in DynamicBuffer<ItemConfigElement> buffer, in ItemConfig config, ItemTypeEnum itemType)
    {
        int index = (int)itemType;
        if (index >= 0 && index < buffer.Length)
        {
            return buffer[index].MaxStack;
        }
        return config.DefaultMaxStack;
    }

    /// <summary>
    /// DynamicBuffer에서 ItemType에 해당하는 MaxStack을 O(1) 인덱싱으로 반환합니다.
    /// 범위 밖이거나 유효하지 않은 경우 명시적으로 전달된 fallbackMaxStack을 반환합니다.
    /// </summary>
    public static int GetMaxStack(this in DynamicBuffer<ItemConfigElement> buffer, ItemTypeEnum itemType, int fallbackMaxStack)
    {
        int index = (int)itemType;
        if (index >= 0 && index < buffer.Length)
        {
            return buffer[index].MaxStack;
        }
        return fallbackMaxStack;
    }

    /// <summary>
    /// DynamicBuffer에서 ItemType에 해당하는 MaxStack을 O(1) 인덱싱으로 반환합니다.
    /// 범위 밖인 경우 0을 반환합니다.
    /// </summary>
    public static int GetMaxStack(this in DynamicBuffer<ItemConfigElement> buffer, ItemTypeEnum itemType)
    {
        int index = (int)itemType;
        if (index >= 0 && index < buffer.Length)
        {
            return buffer[index].MaxStack;
        }
        return 0;
    }
}
