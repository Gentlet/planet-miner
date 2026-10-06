using Unity.Entities;

/// <summary>
/// 역할·목적: 건물 단계 마지막에서 물류·생산·실물/건물 수명주기·완공의 구조 변경을 확정한다.
/// 입력·생성자: 건물 Decision/Execution/StateApply가 같은 종료 경계에 기록한 ECB와 Producer Job이다.
/// 출력·이용: Job 완료 뒤 생성·삭제·태그 변경을 재생한다. 이어지는 드론 단계가 최신 Owner·버퍼·생존 상태를 읽는다.
/// 정리·가시화: 소비한 ECB를 해제한다. 공간 인덱스는 최종 Synchronization까지 다시 만들지 않는다.
/// </summary>
[UpdateInGroup(typeof(BuildingSimulationGroup), OrderLast = true)]
public partial class EndBuildingEntityCommandBufferSystem : EntityCommandBufferSystem
{
}
