using Unity.Entities;

/// <summary>
/// 건물 종료 시 확정된 상태로 작업·경로 의도, 배정 후보와 행동 자격을 계산한다.
/// 원본 소유권·배정·예약을 변경하지 않으며 공개 대기 예약 기록의 정산도 수행하지 않는다.
/// </summary>
[UpdateInGroup(typeof(DroneSimulationGroup))]
public partial class DroneDecisionGroup : ComponentSystemGroup
{
}

