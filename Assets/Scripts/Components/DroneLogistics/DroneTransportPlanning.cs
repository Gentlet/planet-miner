using Unity.Entities;

/// <summary>
/// 일반 반입/반출의 일회성 목표량. 요청 승인 뒤 Execution이 작업과 같은 EndSimulation ECB에 최초 게시한다.
/// 중복 요청으로 합산/변경하지 않는다. 완료량과 확보량은 별도 Progress가 소유한다.
/// </summary>
public struct DroneTransportRequirement : IComponentData
{
    public int Quantity;
}

/// <summary>
/// 작업별 실제 납품 완료량과 미공개 승인/진행 중 확보량이다. 생성은 Execution, 확보는 Reservation이 맡는다.
/// 납품 소유자는 실제 보관 성공분만 정산하며 픽업은 완료량을 늘리지 않는다. 재고/공간 예약이 아니다.
/// </summary>
public struct DroneTransportProgress : IComponentData
{
    public int DeliveredQuantity;
    public int ReservedQuantity;
}

/// <summary>
/// 한 승인 또는 후속 배정이 소유하는 목표량 확보 기록이다. Task 하나에만 귀속하며 잔량을 한 번만 반환한다.
/// 후속 배차는 Take로 미공개 기록에서 인계받아 배정과 같은 EndSimulation에 부착한다.
/// </summary>
public struct DroneTransportReservation : IComponentData
{
    public Entity Task;
    public int RemainingQuantity;
}

/// <summary>
/// Reservation이 선택한 작업 내 수량과 반환 근거다. 후보 singleton에 보관하며 다음 Reservation이 미인계분을 반환한다.
/// 작업 간 선택/배차는 후속 소비자가 맡는다. 같은 수행자의 서로 다른 작업 계획은 아직 배차 대안이다.
/// </summary>
[InternalBufferCapacity(0)]
public struct DroneTransportPendingReservationElement : IBufferElementData
{
    public DroneTaskCandidateDecisionElement Candidate;
    public DroneTransportReservation Reservation;
}

/// <summary>
/// Decision이 매 틱 재작성하는 일반 운반 계획. 현재 재고/공간/네트워크와 평가 완료 경로를 반영한다.
/// 기존 배정 후보와 분리하며 Reservation이 작업 내 목표량만 중재한다. 계획 자체는 실물을 변경하지 않는다.
/// 후보 singleton과 함께 생성되며 다음 Decision에서 Clear한다. 자체 ECB 공개나 영속 예약은 없다.
/// </summary>
[InternalBufferCapacity(0)]
public struct DroneTransportPlanDecisionElement : IBufferElementData
{
    public DroneTaskCandidateDecisionElement Candidate;
}
