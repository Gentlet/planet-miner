using Unity.Entities;

/// <summary>
/// 역할·목적: 최종 구조 변경 확정 뒤 현재 ECS 원본에서 공간 인덱스를 재구축한다.
/// 입력·출력: 벨트·건물·월드 실물·자원 상태를 각 SpatialSyncSystem이 읽어 자기 맵을 Clear 후 재등록한다.
/// 가시화: 재구축된 맵은 이후 Reader의 Fence 의존성을 통해 다음 틱 판단에 전달한다.
/// 정리: 개발 검증은 마지막에 수행한다. 요청/예약 소비는 각 도메인 소유자가 처리하며 이 그룹이 별도 안전망 삭제를 하지 않는다.
/// </summary>
[UpdateInGroup(typeof(GameSimulationGroup), OrderLast = true)]
[UpdateAfter(typeof(SimulationCommitGroup))]
public partial class SynchronizationGroup : ComponentSystemGroup
{
}
