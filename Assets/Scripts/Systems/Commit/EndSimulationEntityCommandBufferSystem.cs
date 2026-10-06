using Unity.Entities;

/// <summary>
/// 드론 작업·경로·배정·행동 결과 및 종료 정리의 ECB를 최종 재생한다.
/// Producer Job을 완료한 뒤 공개하므로 새 작업·배정은 다음 틱 판단/행동부터 사용한다.
/// 건물 구조 변경은 EndBuilding에서 이미 확정되며 이 재생 뒤 Synchronization이 최종 월드를 읽는다.
/// </summary>
[UpdateInGroup(typeof(SimulationCommitGroup), OrderLast = true)]
public partial class EndSimulationEntityCommandBufferSystem : EntityCommandBufferSystem
{
}

