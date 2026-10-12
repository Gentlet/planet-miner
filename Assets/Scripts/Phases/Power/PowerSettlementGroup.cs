using Unity.Entities;

/// <summary>
/// 건물·드론 실행 뒤 실제 사용량과 연료 에너지를 정산할 경계. EndSimulation ECB와 공간 동기화보다 먼저 실행한다.
/// 현재는 빈 그룹이다. 석탄 실물은 앞선 Execution의 전환 때 기존 ItemLifecycle/EndBuilding으로 소비하고
/// 여기서는 실제 사용한 에너지만 차감하므로 Synchronization에 활성 Destroy 요청을 남기지 않는다.
/// </summary>
[UpdateInGroup(typeof(SimulationCommitGroup))]
[UpdateBefore(typeof(EndSimulationEntityCommandBufferSystem))]
public partial class PowerSettlementGroup : ComponentSystemGroup { }
