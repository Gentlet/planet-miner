using Unity.Entities;

/// <summary>
/// 역할·목적: 드론이 공급·보관할 곳을 찾지 못해 월드에 내려놓은 실물에 회수가 필요함을 표시한다.
/// 부착 엔티티: 수행자·작업이 아니라 방출된 기존 아이템 실물 엔티티다.
/// 생성: StateApply의 DroneTaskLifecycleApplySystem이 현장 외부의 유효 DropCargo 인계 성공 시 EndSimulation에 부착한다.
/// 이용: Decision의 DroneTaskDecisionSystem이 DroneDrop 회수 작업 생성 의도를 만들고 DroneSchedulingUtility가 회수 작업의 필요성을 검사한다.
/// 제거: Lifecycle이 해당 실물의 회수 인계 성공 시 EndSimulation에 제거한다. 실물 자체가 삭제되면 함께 사라진다.
/// 이 표시도 없고 활성 현장 footprint 내부에도 없는 일반 월드 아이템은 자동 회수 대상이 아니다.
/// </summary>
public struct DroneRecoveryPending : IComponentData
{
}
