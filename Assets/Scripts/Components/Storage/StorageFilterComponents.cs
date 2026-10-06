using Unity.Entities;

/// <summary>
/// 역할·목적: 전체 허용·Whitelist·Blacklist의 품목 필터 해석 방식을 구분한다.
/// 부착 엔티티: 열거형 자체는 부착하지 않고 StorageFilter.Mode 값으로 사용한다.
/// 생성·이용: BuildingLifecycleUtility/CrafterRecipeCommandSystem이 설정하며 입고·드론 보관 검사에서 StorageFilter.IsItemAllowed가 해석한다.
/// 제거: StorageFilter 갱신·소유자 삭제의 수명을 따른다.
/// </summary>
public enum StorageFilterMode : byte
{
    AllowAll,   // 모든 아이템 허용
    Whitelist,  // FilterMask에 지정된 아이템만 허용
    Blacklist   // FilterMask에 지정된 아이템 차단
}

/// <summary>
/// 역할·목적: 0~127 품목 번호의 허용/차단 비트를 저장하는 128비트 값 형식이다.
/// 부착 엔티티: 독립 ECS 컴포넌트가 아니며 StorageFilter.Mask에 포함한다.
/// 생성·이용: CrafterRecipeCommandSystem(Command)이 레시피 입력 품목 비트를 설정하고 StorageFilter.IsItemAllowed가 읽는다. 범위 밖 번호는 IsSet=false다.
/// 제거: Clear는 값의 비트만 0으로 되돌린다. 값을 포함한 필터의 수명을 따른다.
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
/// 역할·목적: 보관 건물이 수용할 품목을 Mode와 128비트 Mask로 제한한다.
/// 부착 엔티티: 품목 필터가 필요한 보관/제작 건물 엔티티다. 일반 보관은 AllowAll을 사용하며 컴포넌트가 없는 검사 경로도 허용한다.
/// 생성: BuildingLifecycleUtility가 일반 보관 필터 또는 Crafter 빈 Whitelist를 붙인다.
/// 이용: BuildingItemInputDecisionSystem(Decision), BuildingStorageInputReservationSystem(Reservation), 드론 보관 Utility가 허용 품목을 검사한다. CrafterRecipeCommandSystem(Command)이 레시피 변경 때 갱신한다.
/// 제거: 레시피 변경으로 값이 재작성되며 컴포넌트는 건물 삭제 시 함께 제거한다.
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
