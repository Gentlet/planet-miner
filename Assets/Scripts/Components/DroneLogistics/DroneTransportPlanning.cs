using Unity.Entities;

/// <summary>
/// 일반 반입/반출의 일회성 목표량. 요청 승인 뒤 Execution이 작업과 같은 EndSimulation ECB에 최초 게시한다.
/// 중복 요청으로 합산/변경하지 않는다. 실제 성공량 정산과 잔량 소비는 후속 운반 소유자가 연결한다.
/// </summary>
public struct DroneTransportRequirement : IComponentData
{
    public int Quantity;
}

/// <summary>
/// Decision이 매 틱 재작성하는 일반 운반 계획. 현재 재고/공간/네트워크와 평가 완료 경로를 반영한다.
/// 기존 배정 후보와 분리하여 후속 운반 Reservation 연결 전에는 배정/예약/실물 변경을 발생시키지 않는다.
/// 후보 singleton과 함께 생성되며 다음 Decision에서 Clear한다. 자체 ECB 공개나 영속 예약은 없다.
/// </summary>
[InternalBufferCapacity(0)]
public struct DroneTransportPlanDecisionElement : IBufferElementData
{
    public DroneTaskCandidateDecisionElement Candidate;
}
