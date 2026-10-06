using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: Decision이 계산한 신규 배정·적재품 재배정 후보를 한 틱 동안 보관한다.
/// 부착 엔티티: 수행자·작업·배정과 별도인 World 단일 후보 관리 엔티티의 버퍼다.
/// 생성: DroneSchedulingUtility.GetOrCreateCandidates가 버퍼를 준비하고 Decision의 DroneTaskDecisionSystem이 신규·적재 재배정 후보를 작성한다.
/// 이용: Reservation의 ConstructionSupplyReservationSystem이 읽기 검사한 후보를 선택하여 별도 DroneTaskPendingPublicationElement에 복사한다.
/// 제거: 다음 Decision 시작에 Clear한다. 버퍼와 관리 엔티티는 유지한다.
/// Quantity는 판단 당시 수량 상한이다. 실제 선택 수량·미공개 예약·ECB 공개 기록은 이 버퍼에 쓰지 않는다.
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
}
