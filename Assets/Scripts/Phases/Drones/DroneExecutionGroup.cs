using Unity.Entities;

/// <summary>
/// 확정된 작업·경로 의도를 실행하고 OrderFirst에서 드론 단계의 인계 계획을 준비한다.
/// 계획은 실물·슬롯 상한의 파생 데이터다. 생성·삭제는 최종 ECB에 기록하고 새 판단을 반복하지 않는다.
/// </summary>
[UpdateInGroup(typeof(DroneSimulationGroup))]
[UpdateAfter(typeof(DroneReservationGroup))]
public partial class DroneExecutionGroup : ComponentSystemGroup
{
}

