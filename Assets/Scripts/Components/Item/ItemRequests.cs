using Unity.Entities;
using Unity.Mathematics;

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
    public int2 Position;       // 월드 스폰 위치 (월드 아이템 기준)
    public Entity TargetOwner;  // Entity.Null이면 월드 스폰, 특정 건물이면 보관 아이템으로 스폰
    public int TargetSlotIndex; // 건물 적재 시 대상 슬롯 번호 (기본: 0)

    public SpawnItemRequest(ItemTypeEnum itemType, int2 position)
    {
        ItemType = itemType;
        Position = position;
        TargetOwner = Entity.Null;
        TargetSlotIndex = 0;
    }

    public SpawnItemRequest(ItemTypeEnum itemType, Entity targetOwner, int targetSlotIndex = 0)
    {
        ItemType = itemType;
        Position = int2.zero;
        TargetOwner = targetOwner;
        TargetSlotIndex = targetSlotIndex;
    }

    public SpawnItemRequest(ItemTypeEnum itemType, int2 position, Entity targetOwner, int targetSlotIndex = 0)
    {
        ItemType = itemType;
        Position = position;
        TargetOwner = targetOwner;
        TargetSlotIndex = targetSlotIndex;
    }
}

/// <summary>
/// [1. 역할]            : 아이템 엔티티 파괴 및 소모 마킹 요청 (대상 부착형 방식 - 모델 B)
/// [2. Producer (생성자)] : 제작기(Crafter 재료 소모), 폐기 시스템 등
/// [3. Consumer (소비자)] : ItemLifecycleApplySystem (StateApplyGroup)
/// [4. Create Phase]    : DecisionGroup
/// [5. Consume Phase]   : StateApplyGroup
/// [6. 수명주기 원칙]    : Consume-on-Apply (처리 즉시 ecb.DestroyEntity(itemEntity))
/// [7. 실패 정책]        : 이미 유효하지 않은 엔티티인 경우 SetComponentEnabled(false)로 비활성화
/// [8. 안전망 정책]      : 파괴되지 않고 잔존하는 활성 컴포넌트는 WorldInvariantValidationSystem에서 감지
/// </summary>
public struct DestroyItemRequest : IEnableableRequest
{
}

/// <summary>
/// [1. 역할]            : 아이템 소유권 이전 요청 (대상 부착형 방식 - 모델 B)
///                      위치 이동 책임과 분리되어 순수하게 소유자(Owner) 변경만 전담.
/// [2. Producer (생성자)] : StorageInput/Output, DroneCargoTransfer 등
/// [3. Consumer (소비자)] : ItemOwnershipApplySystem (StateApplyGroup)
/// [4. Create Phase]    : DecisionGroup
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
