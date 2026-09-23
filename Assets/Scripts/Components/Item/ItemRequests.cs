using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 아이템 스폰 요청 목적지 구분.
/// </summary>
public enum ItemSpawnDestination : byte
{
    World,      // 필드/바닥/그리드에 월드 아이템으로 스폰 (TargetOwner = Entity.Null)
    Storage,    // TargetOwner의 StoredItemElement(보관함/재료함)에 적재
    Product     // TargetOwner의 ProductItemElement(생산물 출력 대기 버퍼)에 적재
}

/// <summary>
/// [1. 역할]            : 새로운 아이템 엔티티 생성 요청 (독립 엔티티 방식 - 모델 A)
/// [2. Producer (생성자)] : 채굴기(Miner), 제작기(Crafter), 월드 스포너, 초기화 시스템 등
/// [3. Consumer (소비자)] : ItemLifecycleApplySystem (StateApplyGroup)
/// [4. Create Phase]    : DecisionGroup (또는 CommandGroup)
/// [5. Consume Phase]   : StateApplyGroup
/// [6. 수명주기 원칙]    : Consume-on-Apply (처리 완료 즉시 ecb.DestroyEntity로 요청 엔티티 파괴)
/// [7. 실패 정책]        : 부적절한 스폰 조건이거나 생성 실패 시 무시하고 폐기(Drop)
/// [8. 안전망 정책]      : 처리되지 못하고 타임아웃된 요청 엔티티는 WorldInvariantValidationSystem에서 감지
/// </summary>
public struct SpawnItemRequest : IRequestComponent
{
    public ItemTypeEnum ItemType;
    public int2 Position;                     // 월드 스폰 위치 (월드 아이템 기준)
    public Entity TargetOwner;                // 스폰 대상 소유 건물 (Storage/Product 기준)
    public ItemSpawnDestination Destination;  // 스폰 목적지 (World, Storage, Product)
    public int TargetSlotIndex;               // 건물 적재 시 대상 슬롯 번호 (기본: 0)

    public SpawnItemRequest(ItemTypeEnum itemType, int2 position)
    {
        ItemType = itemType;
        Position = position;
        TargetOwner = Entity.Null;
        Destination = ItemSpawnDestination.World;
        TargetSlotIndex = 0;
    }

    public SpawnItemRequest(ItemTypeEnum itemType, Entity targetOwner, ItemSpawnDestination destination, int targetSlotIndex = 0)
    {
        ItemType = itemType;
        Position = int2.zero;
        TargetOwner = targetOwner;
        Destination = destination;
        TargetSlotIndex = targetSlotIndex;
    }

    public SpawnItemRequest(ItemTypeEnum itemType, int2 position, Entity targetOwner, ItemSpawnDestination destination, int targetSlotIndex = 0)
    {
        ItemType = itemType;
        Position = position;
        TargetOwner = targetOwner;
        Destination = destination;
        TargetSlotIndex = targetSlotIndex;
    }
}

/// <summary>
/// 아이템 엔티티 파괴 요청 컴포넌트 (IEnableableRequest).
///
/// [소유 버퍼 계약]:
/// 보관/출력 아이템 파괴 시 Producer가 소유 버퍼(StoredItemElement/ProductItemElement)에서 먼저 제거(RemoveAt)한 뒤 활성화.
/// 버퍼 미제거 잔류 파괴 시 WorldInvariantValidationSystem에서 불변식 위반으로 감지.
///
/// [소비 및 수명주기]:
/// StateApplyGroup의 ItemLifecycleApplySystem에서 엔티티 파괴 후 소멸(Consume-on-Apply).
/// </summary>
public struct DestroyItemRequest : IEnableableRequest
{
}

/// <summary>
/// [1. 역할]            : 아이템 소유권 이전 요청 (대상 부착형 방식 - 모델 B)
///                      위치 이동 책임과 분리되어 순수하게 소유자(Owner) 변경만 전담.
/// [2. Producer (생성자)] : BuildingItemStorageApplySystem 등 소유권 변경을 요청하는 StateApply 시스템
/// [3. Consumer (소비자)] : ItemOwnershipApplySystem (StateApplyGroup)
/// [4. Create Phase]    : StateApplyGroup
/// [5. Consume Phase]   : StateApplyGroup
/// [6. 수명주기 원칙]    : Consume-on-Apply (처리 즉시 SetComponentEnabled(false)로 비활성화)
/// [7. 실패 정책]        : TargetOwner가 유효하지 않은 경우 무시하고 비활성화(Drop)
/// [8. 안전망 정책]      : 비정상 미처리 요청은 WorldInvariantValidationSystem에서 감지
/// </summary>
public struct TransferOwnershipRequest : IEnableableRequest
{
    public Entity TargetOwner; // 새로운 소유자 (Entity.Null이면 월드로 방출)

    public TransferOwnershipRequest(Entity targetOwner)
    {
        TargetOwner = targetOwner;
    }
}
