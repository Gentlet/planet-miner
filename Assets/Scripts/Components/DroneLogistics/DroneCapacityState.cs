using Unity.Entities;

/// <summary>
/// 역할·목적: 모든 드론의 현재 공통 최대 적재량을 소유한다. 수행자마다 동일 값을 복제하지 않는다.
/// 부착 엔티티: 드론·배정과 별도인 World 단일 능력 상태 엔티티다.
/// 생성: 초기 능력 게시 Writer는 아직 미구현이며 현재 제품 코드가 임의 기본 상태를 생성하지 않는다.
/// 이용: Decision의 DroneTaskDecisionSystem, Reservation의 ConstructionSupplyReservationSystem, StateApply의 DroneTaskAssignmentPublishSystem이 신규 배정에 읽는다.
/// Execution의 DroneTaskExecutionSystem도 경로 명령 적용 검사에 읽는다. 부재/0 이하는 빈 수행자의 신규 배정을 허용하지 않는다.
/// 제거: 개별 작업·배정 종료로 소비하지 않는 공통 상태다. 게시·연구 변경·제거의 Writer 연결은 후속이다.
/// CarryingCapacity는 한 품목의 최대 실물 개수이며 Storage.SlotCount/품목 MaxStack과 독립적이다.
/// 값이 바뀌어도 기존 AssignedQuantity·개별 예약·적재품을 소급 수정하지 않는다.
/// </summary>
public struct DroneCapacityState : IComponentData
{
    public int CarryingCapacity;
}
