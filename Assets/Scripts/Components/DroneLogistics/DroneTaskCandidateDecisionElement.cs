using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: Decision의 배정 후보에 Reservation의 선택·예약 결과와 Publish의 공개 대기 기록을 함께 보관한다.
/// 부착 엔티티: 수행자·작업·배정과 별도인 World 단일 후보 관리 엔티티의 버퍼다.
/// 생성: DroneSchedulingUtility.GetOrCreateCandidates가 버퍼를 준비하고 Decision의 DroneTaskDecisionSystem이 신규·적재 재배정 후보를 작성한다.
/// 이용: Reservation의 ConstructionSupplyReservationSystem이 Quantity/Selected/CommittedQuantity를 확정한다.
/// StateApply의 DroneTaskAssignmentPublishSystem이 최종 재검사·롤백 후 배정과 개별 예약의 EndStateApply 공개를 기록하고 Published를 갱신한다.
/// 제거: 다음 Decision이 공개된 항목·예약 없는 항목을 제거한다. 미공개 CommittedQuantity는 다음 Reservation이 실제 예약을 해제할 때까지 보존한다.
/// 버퍼와 관리 엔티티는 유지한다. 미공개 예약이 남은 항목의 수명은 한 틱의 순수 판단 결과보다 길 수 있다.
/// CommittedQuantity는 아직 배정 revision의 개별 예약으로 인계되지 않은 현장 예약량이며 공개 전에는 이 버퍼가 예약 근거다.
/// Assignment가 있으면 같은 배정의 적재품 재배정 후보이며 NextAction은 공급·보관·방출 목적을 나타낸다.
/// </summary>
[InternalBufferCapacity(0)]
public struct DroneTaskCandidateDecisionElement : IBufferElementData
{
    public Entity Worker;
    public Entity Assignment; // Null은 빈 수행자의 신규 배정, 그 외에는 같은 배정의 적재품 재배정이다.
    public uint AssignmentRevision;
    public DroneActionKindEnum NextAction;
    public int2 DropPosition; // DropCargo가 선택한 현장 외부 셀. 일반 목적지는 Destination을 사용한다.
    public Entity Task;
    public Entity Source;
    public Entity Destination;
    public Entity RouteRequest;
    public ItemTypeEnum ItemType;
    public uint WorkerObservationRevision;
    public int Quantity;
    public int CommittedQuantity;
    public bool Selected;
    public bool Published; // Publish가 ECB에 기록했음을 표시한다. 실제 Assignment는 EndStateApply에 실체화된다.
}
