using Unity.Entities;

/// <summary>
/// Phase 3: 공유 자원 예약 및 경합 방지 단계.
/// 여러 실행 주체가 동일 자원(아이템, 건설 부지, 수용 공간 등)을 동시에 사용하는 것을 방지합니다.
/// </summary>
[UpdateInGroup(typeof(GameSimulationGroup))]
[UpdateAfter(typeof(DecisionGroup))]
public partial class ReservationGroup : ComponentSystemGroup
{
}
