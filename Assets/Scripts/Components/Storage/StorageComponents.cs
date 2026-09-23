using Unity.Entities;

/// <summary>
/// 창고의 필터링 모드를 정의하는 열거형.
/// </summary>
public enum StorageFilterMode : byte
{
    AllowAll,   // 모든 아이템 허용
    Whitelist,  // FilterMask에 지정된 아이템만 허용
    Blacklist   // FilterMask에 지정된 아이템 차단
}

/// <summary>
/// 128종 이상의 아이템을 지원하는 고정 크기 unmanaged 비트셋.
/// 향후 비트 크기 확장 시 내부 구현만 확장하여 호환성을 유지.
/// </summary>
public struct FixedBitSet
{
    public ulong Part0; // 0 ~ 63번 아이템
    public ulong Part1; // 64 ~ 127번 아이템

    public const int Capacity = 128;

    /// <summary>
    /// 해당 인덱스의 비트가 켜져 있는지 확인.
    /// </summary>
    public bool IsSet(byte index)
    {
        if (index < 64)
            return (Part0 & (1UL << index)) != 0;
        if (index < 128)
            return (Part1 & (1UL << (index - 64))) != 0;
        return false;
    }

    /// <summary>
    /// 해당 인덱스의 비트를 설정하거나 해제.
    /// </summary>
    public void Set(byte index, bool value)
    {
        if (index < 64)
        {
            if (value) Part0 |= (1UL << index);
            else Part0 &= ~(1UL << index);
        }
        else if (index < 128)
        {
            if (value) Part1 |= (1UL << (index - 64));
            else Part1 &= ~(1UL << (index - 64));
        }
    }

    /// <summary>
    /// 모든 비트를 0으로 초기화.
    /// </summary>
    public void Clear()
    {
        Part0 = 0;
        Part1 = 0;
    }
}

/// <summary>
/// 창고나 건물에서 수용할 아이템을 제한하는 필터 컴포넌트.
/// 필터링이 필요 없는 일반 창고는 이 컴포넌트를 부착하지 않거나 AllowAll로 둡니다.
/// </summary>
public struct StorageFilter : IComponentData
{
    public StorageFilterMode Mode;
    public FixedBitSet Mask;

    public StorageFilter(StorageFilterMode mode)
    {
        Mode = mode;
        Mask = default;
    }

    /// <summary>
    /// 지정된 아이템이 현재 필터 규칙에 의해 허용되는지 판정.
    /// </summary>
    public bool IsItemAllowed(ItemTypeEnum itemType)
    {
        return Mode switch
        {
            StorageFilterMode.AllowAll => true,
            StorageFilterMode.Whitelist => Mask.IsSet((byte)itemType),
            StorageFilterMode.Blacklist => !Mask.IsSet((byte)itemType),
            _ => true
        };
    }
}

/// <summary>
/// 창고의 보관 슬롯 용량을 정의하는 핵심 컴포넌트.
/// 
/// [책임]
/// - 창고가 보유한 총 슬롯 칸수(SlotCount)를 정의.
/// - 슬롯당 최대 스택 수는 아이템별 고유 설정에 따라 결정.
/// </summary>
public struct Storage : IComponentData
{
    public int SlotCount;

    public Storage(int slotCount)
    {
        SlotCount = slotCount;
    }
}

/// <summary>
/// 창고나 건물에 보관된 개별 아이템 엔티티와 슬롯 정보를 담는 버퍼 요소.
/// </summary>
[InternalBufferCapacity(16)]
public struct StoredItemElement : IBufferElementData
{
    public Entity ItemEntity;     // 보관된 아이템 엔티티
    public ItemTypeEnum ItemType; // 아이템 종류 (O(1) 캐싱)
    public int SlotIndex;         // 소속된 슬롯 번호 (0 ~ SlotCount - 1)

    public StoredItemElement(Entity itemEntity, ItemTypeEnum itemType, int slotIndex)
    {
        ItemEntity = itemEntity;
        ItemType = itemType;
        SlotIndex = slotIndex;
    }
}

/// <summary>
/// 채굴기, 제작기 등 생산 건물의 출력 대기 버퍼 요소 (Unmanaged).
/// 완성품과 부산물을 일반 보관/재료 버퍼(StoredItemElement)와 물리적으로 분리.
/// </summary>
[InternalBufferCapacity(8)]
public struct ProductItemElement : IBufferElementData
{
    public Entity ItemEntity;     // 생산된 아이템 엔티티
    public ItemTypeEnum ItemType; // 아이템 종류 (O(1) 캐싱)
    public int SlotIndex;         // 소속된 슬롯 번호 (Crafter의 경우 0=주완성품, 1=부산품)

    public ProductItemElement(Entity itemEntity, ItemTypeEnum itemType, int slotIndex = 0)
    {
        ItemEntity = itemEntity;
        ItemType = itemType;
        SlotIndex = slotIndex;
    }
}
