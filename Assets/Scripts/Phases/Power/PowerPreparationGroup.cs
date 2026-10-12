using Unity.Entities;

/// <summary>
/// 건물 예약 마지막에서 프레임 데이터 초기화 → 생산·충전 수요 준비 → 토폴로지·연료 가용량과 배분 계획을 준비할 경계.
/// 각 도메인은 같은 World 시간과 GameConstants의 DeltaTime 상한을 사용하며 전력 전용 시계를 만들지 않는다.
/// 실제 공급은 다음 PowerSupplyExecutionGroup에서 연료로 뒷받침된 뒤 한 번 게시한다.
/// 현재는 Task 8.1의 실행 계약을 고정한 빈 그룹이며 실제 Writer는 Task 8.2~8.6 및 9.14/10A.3에서 연결한다.
/// </summary>
[UpdateInGroup(typeof(BuildingReservationGroup), OrderLast = true)]
public partial class PowerPreparationGroup : ComponentSystemGroup { }
