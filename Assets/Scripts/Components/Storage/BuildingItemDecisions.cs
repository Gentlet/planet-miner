using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: 벨트 끝 월드 아이템의 건물 입고 후보와 슬롯 예약 결과를 전달하는 enableable 판단이다.
/// 부착 엔티티: 건물이 아니라 입고를 시도하는 아이템 실물 엔티티다.
/// 생성: ItemLifecycleUtility가 비활성으로 준비하고 BuildingItemInputDecisionSystem(Decision)이 대상·입고 자격을 작성한다.
/// 이용: BuildingStorageInputReservationSystem(Reservation)이 경합을 해결하여 슬롯을 확정하고 BuildingItemStorageApplySystem(StateApply)이 보관 버퍼·위치와 Transfer 요청을 기록한다.
/// 제거: Apply 후 즉시 비활성화하며 컴포넌트는 실물 삭제까지 유지한다. TargetSlotIndex=-1은 미배정이다.
/// </summary>
public struct BuildingItemInputDecision : IComponentData, IEnableableComponent
{
    /// <summary>
    /// 입고를 시도할 대상 건물 엔티티.
    /// </summary>
    public Entity TargetBuilding;

    /// <summary>
    /// 입고 가능 여부 (필터 통과 및 건물 내 여유 공간 존재 시 true).
    /// </summary>
    public bool CanDeposit;

    /// <summary>
    /// 배정된 창고/건물 슬롯 번호 (0 ~ SlotCount - 1).
    /// Decision 단계에서는 기본값 -1(미배정)이며, ReservationPhase에서 경합 해결 후 최종 확정.
    /// </summary>
    public int TargetSlotIndex;

    public BuildingItemInputDecision(Entity targetBuilding, bool canDeposit = false, int targetSlotIndex = -1)
    {
        TargetBuilding = targetBuilding;
        CanDeposit = canDeposit;
        TargetSlotIndex = targetSlotIndex;
    }
}

/// <summary>
/// 역할·목적: 보관/생산 건물에서 이번 틱 벨트로 내보낼 실물 하나와 대상 벨트를 전달한다.
/// 부착 엔티티: StoredItemElement 또는 ProductItemElement를 가진 완공 건물 엔티티다.
/// 생성: BuildingLifecycleUtility가 비활성으로 준비하고 StorageItemOutputDecisionSystem/ProductItemOutputDecisionSystem(Decision)이 출고 후보를 작성한다.
/// 이용: BeltDestinationReservationSystem(Reservation)이 목적지 경합을 중재하고 BuildingItemStorageApplySystem(StateApply)이 실물 버퍼 제거·벨트 위치·Transfer 요청을 반영한다.
/// 제거: Apply 후 비활성화하며 컴포넌트는 건물 삭제까지 유지한다. 아이템 Owner 변경은 이어지는 ItemOwnershipApplySystem이 담당한다.
/// </summary>
public struct BuildingItemOutputDecision : IComponentData, IEnableableComponent
{
    /// <summary>
    /// 출고 가능 여부 (외향 벨트 존재 및 벨트 시작점 간격 확보 시 true).
    /// </summary>
    public bool CanOutput;

    /// <summary>
    /// 방출할 대상 아이템 엔티티 (건물 StoredItemElement 버퍼의 FIFO 0번 아이템 등).
    /// </summary>
    public Entity ItemToOutput;

    /// <summary>
    /// 아이템이 올려질 대상 외향 벨트의 그리드 좌표.
    /// </summary>
    public int2 TargetBeltPosition;

    public BuildingItemOutputDecision(bool canOutput, Entity itemToOutput, int2 targetBeltPosition)
    {
        CanOutput = canOutput;
        ItemToOutput = itemToOutput;
        TargetBeltPosition = targetBeltPosition;
    }
}
