using Unity.Entities;

/// <summary>
/// 아이템 종류 정의 열거형.
/// </summary>
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
    Drone
}

/// <summary>
/// 아이템 고유 식별 컴포넌트 (Unmanaged / Blittable).
/// 아이템의 논리적 종류(타입)를 나타냅니다.
/// </summary>
public struct ItemIdentity : IComponentData
{
    public ItemTypeEnum Type;

    public ItemIdentity(ItemTypeEnum type)
    {
        Type = type;
    }
}

/// <summary>
/// 아이템 소유권 상태 컴포넌트 (단일 원본, Single Source of Truth).
/// 
/// [상태 판별 규칙]
/// - Owner == Entity.Null : 월드 아이템 (World Item) - 바닥/벨트 위에 놓여있으며 GridPosition이 유효함
/// - Owner != Entity.Null : 수납된 아이템 (Stored Item) - 특정 건물/창고 내부에 보관 중이며 GridPosition은 무시됨
/// </summary>
public struct ItemOwnership : IComponentData
{
    public Entity Owner;

    public bool IsWorldItem => Owner == Entity.Null;
    public bool IsStored => Owner != Entity.Null;

    public ItemOwnership(Entity owner)
    {
        Owner = owner;
    }

    /// <summary>
    /// 소유자가 없는 월드 아이템 상태 인스턴스.
    /// </summary>
    public static ItemOwnership WorldItem => new ItemOwnership(Entity.Null);

    /// <summary>
    /// 특정 소유자에게 귀속된 수납 아이템 상태 인스턴스.
    /// </summary>
    public static ItemOwnership Stored(Entity owner) => new ItemOwnership(owner);
}
