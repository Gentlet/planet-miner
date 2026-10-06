using Unity.Entities;

/// <summary>
/// 역할·목적: 배정 하나가 확보한 현장 공급 수량을 식별하여 같은 예약을 중복 해제하지 않게 한다.
/// 부착 엔티티: DroneTaskAssignment가 있는 드론 하위 배정 엔티티. AssignmentRevision은 같은 배정의 Revision과 대응한다.
/// 생성: ConstructionSupplyReservationSystem(Reservation)이 현장 합계와 후보의 CommittedQuantity를 확보한 뒤,
/// DroneTaskAssignmentPublishSystem(StateApply)이 이 기록을 배정과 함께 EndStateApply에 공개한다. 재배정 시 기존 기록을 갱신한다.
/// 이용: Reservation은 무효 예약을 해제하고 DroneTaskLifecycleApplySystem(StateApply)은 수집 부족분/실제 공급분을 정산한다.
/// 현장 ReservedQuantity는 활성 개별 기록과 미공개 CommittedQuantity의 합계다. 공급원 재고/보관 공간 예약은 아니다.
/// 제거: RemainingQuantity=0이면 예약 없음/정산 완료다. Lifecycle이 참조와 예약 정리를 마친 배정 엔티티를 삭제할 때 함께 제거한다.
/// </summary>
public struct ConstructionSupplyReservation : IComponentData
{
    public Entity Site;
    public ItemTypeEnum ItemType;
    public uint AssignmentRevision;
    public int RemainingQuantity;
}
