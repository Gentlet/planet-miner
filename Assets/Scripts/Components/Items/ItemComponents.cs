using Unity.Entities;

/// <summary>
/// 역할·목적: 광물·재료·드론 품목의 식별 번호를 정의한다.
/// 부착 엔티티: 열거형 자체는 부착하지 않으며 ItemIdentity·보관/생산 버퍼·설정·요청에 값으로 포함한다.
/// 생성·이용: Authoring·설정 로더·생성 Producer가 선택하고 채굴·제작·입출고·드론 시스템이 같은 번호로 품목을 비교한다.
/// 제거: 값을 포함한 데이터의 수명을 따른다. enum 값이 존재한다는 것만으로 해당 품목의 생산·드론 생성 기능이 구현된 것은 아니다.
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
/// 역할·목적: 실물 아이템 하나의 품목 원본을 제공한다. 아이템 하나는 엔티티 하나로 유지한다.
/// 부착 엔티티: 베이킹된 아이템 프리팹과 그 프리팹에서 인스턴스화한 런타임 아이템 실물이다.
/// 생성: ItemAuthoring.Baker가 프리팹에 붙이고 ItemLifecycleUtility가 등록된 프리팹을 인스턴스화하면서 요청 품목으로 설정한다.
/// 이용: ItemLifecycleApplySystem(StateApply)의 생성·삭제, 채굴/제작·입출고·드론의 품목 검사, ItemSpatialSyncSystem(Synchronization)의 실물 조회가 읽는다.
/// 제거: 실물 엔티티 삭제 시 함께 제거한다. 소유자 이동으로 품목을 바꾸거나 다른 실물을 생성하지 않는다.
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
/// 역할·목적: 아이템의 현재 소유자 원본이다. Owner=Null은 월드 실물, 그 외는 해당 건물·현장·드론에 수납된 실물이다.
/// 부착 엔티티: 아이템 실물 엔티티이며 소유자의 StoredItemElement/ProductItemElement 참조와 함께 정합성을 유지한다.
/// 생성: ItemLifecycleUtility가 생성 목적지에 맞게 Owner를 아이템 생성 ECB에 기록한다.
/// 이용: ItemOwnershipApplySystem(StateApply)이 일반 Transfer와 공통 TryTransferItem 인계를 반영한다. ConstructionCancelCommandSystem(Command)·BuildingLifecycleApplySystem(StateApply)은 기존 실물 반환을 기록한다.
/// 입출고·제작·드론·완공 판단과 ItemSpatialSyncSystem(Synchronization)이 읽는다. 수납 실물의 GridPosition은 월드 점유 근거로 사용하지 않는다.
/// 제거: 실물 삭제 시 함께 제거한다. Owner 변경은 아이템 엔티티의 삭제·재생성이 아니다.
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
