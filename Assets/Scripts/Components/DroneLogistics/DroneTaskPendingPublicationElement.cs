using Unity.Entities;

/// <summary>
/// 역할·목적: Reservation이 선택한 배정의 스냅샷과 공개 전 현장 예약을 Publish/다음 틱 정산까지 보관한다.
/// 부착 엔티티: DroneTaskCandidateDecisionElement와 같은 World 단일 후보 관리 엔티티의 버퍼다.
/// 생성: ConstructionSupplyReservationSystem이 순수 Decision 후보를 복사하고 선택 수량·실제 확보량을 기록한다.
/// 이용: DroneTaskAssignmentPublishSystem이 최신 상태를 재검사해 축소/무효 예약을 되돌리고 배정을 최종 ECB에 기록한다.
/// CommittedQuantity는 아직 개별 배정 예약으로 인계되지 않은 현장 예약량이다.
/// PublicationQueued는 ECB 기록 완료이며 실제 배정 공개는 최종 ECB 재생 시점이다.
/// 제거: 다음 Reservation이 공개 기록은 제거하고 미공개 예약은 현장 합계에서 해제한 뒤 Clear한다.
/// Decision은 이 기록을 보존하고 ProjectedRemaining 계산에서 다음 Reservation의 해제 예정량만 읽는다.
/// </summary>
[InternalBufferCapacity(0)]
public struct DroneTaskPendingPublicationElement : IBufferElementData
{
    public DroneTaskCandidateDecisionElement Candidate;
    public int Quantity;
    public int CommittedQuantity;
    public bool PublicationQueued;
}
