using Unity.Entities;

/// <summary>
/// 역할·목적: Command 뒤의 두 번째 phase로 확정 상태를 읽어 실행 가능성·생성/종료 의도·인계 후보를 계산한다.
/// 입력·출력: 각 Decision이 원본을 읽고 자기 결정 컴포넌트/버퍼를 작성한다. 결정과 영속 상태/요청을 구분한다.
/// 이용·정리: Reservation/Execution/StateApply가 결정을 소비하며 갱신·비활성화 시점은 해당 데이터 계약을 따른다.
/// 가시화: 앞선 EndCommand 생성/취소/철거 승인 상태는 읽을 수 있지만 공간 인덱스는 직전 Synchronization 결과다.
/// </summary>
[UpdateInGroup(typeof(GameSimulationGroup))]
[UpdateAfter(typeof(CommandGroup))]
public partial class DecisionGroup : ComponentSystemGroup
{
}
