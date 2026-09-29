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
