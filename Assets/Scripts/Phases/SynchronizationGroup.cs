using Unity.Entities;

/// <summary>
/// Phase 6: 동기화 및 정리 단계.
/// 엔티티 위치와 공간 인덱스(ChunkMap) 동기화, 파생 데이터 갱신,
/// 만료된 예약 및 수명이 다한 임시 상태(Event/Request) 안전망 정리를 수행합니다.
/// </summary>
[UpdateInGroup(typeof(GameSimulationGroup))]
[UpdateAfter(typeof(StateApplyGroup))]
public partial class SynchronizationGroup : ComponentSystemGroup
{
}
