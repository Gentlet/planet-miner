using Unity.Entities;

/// <summary>
/// 역할/목적: 안전 방출품과 건물 철거 반환/환급품/정지된 벨트 실물의 한정 회수 필요와 출처를 표시한다.
/// 부착 엔티티: 수행자/작업이 아니라 반환/방출된 아이템 실물이다.
/// 생성: Drone Lifecycle은 안전 방출 성공분을 EndSimulation에, Building Lifecycle은 철거 반환/환급분과 철거 벨트의 정지 실물을 EndBuilding에 기록한다.
/// 이용: Task Decision이 표시의 출처로 한정 회수 의도를 만들고 기존 배정/픽업/보관 경로가 처리한다.
/// 제거: Lifecycle이 해당 실물의 회수 인계 성공 시 EndSimulation에 제거한다. 실물 자체가 삭제되면 함께 사라진다.
/// 이 표시도 없고 활성 현장 footprint 내부에도 없는 일반 월드 아이템은 자동 회수 대상이 아니다.
/// </summary>
public struct DroneRecoveryPending : IComponentData
{
    // 기존 무인자 표시는 DroneDrop으로 해석한다. 철거 반환은 출처를 명시한다.
    public DroneRecoveryReasonEnum Reason;
}
