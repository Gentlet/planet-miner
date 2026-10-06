using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: 신규 실물의 월드·일반 보관·생산품 목적지를 구분한다.
/// 부착 엔티티: 열거형 자체는 부착하지 않고 SpawnItemRequest.Destination에 포함한다.
/// 생성·이용: 요청 Producer가 선택하고 ItemSpawnAdmissionDecisionSystem(Decision)과 ItemLifecycleApplySystem(StateApply)이 목적지별 승인·Owner·버퍼를 결정한다.
/// 제거: 이를 담은 요청 엔티티가 EndStateApply에서 삭제되면 함께 사라진다.
/// </summary>
public enum ItemSpawnDestination : byte
{
    World,      // 필드/바닥/그리드에 월드 아이템으로 스폰 (TargetOwner = Entity.Null)
    Storage,    // TargetOwner의 StoredItemElement(보관함/재료함)에 적재
    Product     // TargetOwner의 ProductItemElement(생산물 출력 대기 버퍼)에 적재
}

/// <summary>
/// 역할·목적: 지정 품목의 새로운 아이템 실물 하나를 월드 또는 소유 버퍼에 생성하는 일회성 요청이다.
/// 부착 엔티티: 새로 만들 실물과 별도인 요청 엔티티다. 채굴/제작 생산은 이 요청 대신 ProductResult를 사용한다.
/// 생성: 외부 Producer가 Admission 전에 실체화하는 계약이며 현재 제품 코드의 외부 입력 Producer는 없다.
/// 이용: ItemSpawnAdmissionDecisionSystem(Decision)이 철거 예정 입고·활성 현장 내부 World 스폰을 비활성화한다. ItemLifecycleApplySystem(StateApply)이 목적지 버퍼/현장 위치를 검사하고 등록 프리팹으로 생성한다.
/// 제거: 거부·생성 처리한 요청은 EndStateApply에서 삭제한다. 프리팹 누락은 대체 생성 없이 SimulationFatalError를 기록하며 요청을 재시도하지 않는다.
/// 나중에 생성하는 Producer는 이미 승인된 철거/현장 금지 계약을 스스로 준수해야 한다. Position은 월드 셀, TargetSlotIndex는 목적지 보관/출력 슬롯이다.
/// </summary>
public struct SpawnItemRequest : IRequestComponent, IEnableableComponent
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
/// 역할·목적: 기존 아이템 실물의 삭제를 요청하는 enableable 태그다. 별도의 삭제 요청 엔티티를 만들지 않는다.
/// 부착 엔티티: 삭제할 아이템 실물 엔티티다.
/// 생성: ItemLifecycleUtility가 비활성으로 준비한다. CrafterExecutionSystem(Execution) 등 소비 Producer가 소유 버퍼에서 실물 참조를 먼저 제거한 뒤 활성화한다.
/// 이용: ItemLifecycleApplySystem(StateApply)이 활성 실물을 삭제한다. 인계·반환·완공 검사는 활성 Destroy 실물을 유효 재고/차단/반환으로 세지 않는다.
/// 제거: 실제 실물과 컴포넌트를 EndStateApply에 함께 삭제한다. 소유 버퍼 정리는 요청 Consumer가 대신 수행하지 않는다.
/// </summary>
public struct DestroyItemRequest : IEnableableRequest
{
}

/// <summary>
/// 역할·목적: 기존 아이템의 Owner와 렌더 상태를 일반 입출고 경계에서 변경하도록 요청한다. 소유 버퍼·위치 이동과 구분한다.
/// 부착 엔티티: 소유권이 바뀔 아이템 실물 엔티티다.
/// 생성: ItemLifecycleUtility가 비활성으로 준비하고 BuildingItemStorageApplySystem(StateApply)이 버퍼·위치 변경 후 TargetOwner를 기록하여 활성화한다.
/// 이용: ItemOwnershipApplySystem(StateApply)이 유효한 대상이면 Owner·렌더 ECB를 반영하고 활성 Destroy/없는 대상은 변경 없이 요청만 소비한다.
/// 제거: 처리 후 즉시 비활성화하며 컴포넌트는 실물 삭제까지 유지한다. BuildingDemolitionCommandSystem(Command)은 승인 건물의 이전 Transfer를 비활성화한다.
/// 드론의 공통 실물 인계 API는 새 Transfer 요청을 발행하지 않고 버퍼·Owner·위치·렌더를 함께 반영한다. TargetOwner=Null은 월드 소유다.
/// </summary>
public struct TransferOwnershipRequest : IEnableableRequest
{
    public Entity TargetOwner; // 새로운 소유자 (Entity.Null이면 월드로 방출)

    public TransferOwnershipRequest(Entity targetOwner)
    {
        TargetOwner = targetOwner;
    }
}
