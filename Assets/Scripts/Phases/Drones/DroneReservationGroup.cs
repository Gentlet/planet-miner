using Unity.Entities;

/// <summary>
/// 이전 미공개 예약을 정산하고 이번 후보에서 배정과 현장 공급 수량을 선택한다.
/// 승인 결과는 공개 대기 버퍼로 인계한다. 공급원 재고·보관 공간을 영속 예약하지 않는다.
/// </summary>
[UpdateInGroup(typeof(DroneSimulationGroup))]
[UpdateAfter(typeof(DroneDecisionGroup))]
public partial class DroneReservationGroup : ComponentSystemGroup
{
}

