using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: 현장·품목별 공급과 월드 실물별 회수의 상위 작업을 구분한다.
/// 부착 엔티티: 열거형 자체는 부착하지 않으며 DroneLogisticsTask.Kind·생성 의도에 포함한다.
/// 생성·이용: DroneTaskDecisionSystem(Decision)이 종류별 필요를 판단하고 DroneTaskExecutionSystem(Execution)이 작업을 생성한다. Reservation은 공급/회수 선두를 따로 비교한다.
/// 제거: 값을 담은 컴포넌트·판단·결과의 갱신/삭제 수명을 따른다. 별도 엔티티 수명을 갖지 않는다.
/// </summary>
public enum DroneLogisticsTaskKindEnum : byte
{
    None,
    ConstructionSupply,
    WorldItemRecovery,
    Count
}

/// <summary>
/// 역할·목적: 상위 작업의 Open/Closed 상태를 구분한다.
/// 부착 엔티티: 열거형 자체는 부착하지 않으며 DroneLogisticsTask.State에 포함한다.
/// 생성·이용: DroneTaskExecutionSystem(Execution)이 Open 작업을 기록하고 DroneTaskLifecycleApplySystem(StateApply)이 무효화 의도로 Closed를 반영한다.
/// 제거: 값을 담은 컴포넌트·판단·결과의 갱신/삭제 수명을 따른다. 별도 엔티티 수명을 갖지 않는다.
/// </summary>
public enum DroneLogisticsTaskStateEnum : byte
{
    None,
    Open,
    Closed,
    Count
}

/// <summary>
/// 역할·목적: 월드 실물 회수 필요가 현장 바닥 정리인지 드론 방출인지 구분한다.
/// 부착 엔티티: 열거형 자체는 부착하지 않으며 DroneLogisticsTask.RecoveryReason·생성 의도에 포함한다.
/// 생성·이용: DroneTaskDecisionSystem(Decision)이 현장 footprint/DroneRecoveryPending으로 선택하고 관리 측 유효성 검사가 해당 근거의 현재성을 확인한다.
/// 제거: 값을 담은 컴포넌트·판단·결과의 갱신/삭제 수명을 따른다. 별도 엔티티 수명을 갖지 않는다.
/// </summary>
public enum DroneRecoveryReasonEnum : byte
{
    None,
    SiteClearance,
    DroneDrop,
    Count
}

/// <summary>
/// 역할·목적: 배정의 이동·수집/공급 대기·재배정·완료/취소 상태를 구분한다.
/// 부착 엔티티: 열거형 자체는 부착하지 않으며 DroneTaskAssignment.State에 포함한다.
/// 생성·이용: DroneTaskAssignmentPublishSystem(StateApply)이 초기/재배정 상태를 기록하고 DroneTaskLifecycleApplySystem이 행동 결과·무효화로 갱신한다. 실제 이동 수행부는 후속이다.
/// 제거: 값을 담은 컴포넌트·판단·결과의 갱신/삭제 수명을 따른다. 별도 엔티티 수명을 갖지 않는다.
/// </summary>
public enum DroneTaskAssignmentStateEnum : byte
{
    None,
    AwaitingRoute,
    MovingToSource,
    AwaitingCollection,
    MovingToDestination,
    AwaitingDelivery,
    Retargeting,
    Completed,
    Cancelled,
    Count
}

/// <summary>
/// 역할·목적: 현장·품목별 공급 요구 또는 월드 실물별 회수 필요를 여러 드론의 개별 배정과 분리하여 관리하는 상위 작업이다.
/// 부착 엔티티: 현장·아이템·수행자·배정과 별도인 상위 작업 엔티티다. Target은 공급 현장 또는 회수할 실물이다.
/// 생성: Decision의 DroneTaskDecisionSystem이 의도를 작성하고 Execution의 DroneTaskExecutionSystem이 순번을 발급하여 EndSimulation에 생성한다.
/// 이용: 다음 틱부터 Decision의 DroneTaskDecisionSystem이 후보를 판단하고 Reservation의 ConstructionSupplyReservationSystem이 생성 순서·PlacementStamp에 따른 선두를 선택한다.
/// StateApply의 DroneTaskAssignmentPublishSystem은 배정에 작업을 연결하고 DroneTaskLifecycleApplySystem은 전달받은 무효화 의도로 Closed를 반영한다.
/// 제거: Lifecycle이 Closed인 작업에 연결 배정이 없음을 확인한 뒤 EndSimulation에 작업 엔티티 삭제를 기록한다.
/// CreationSequence는 PlacementStamp와 별개인 양수 작업 생성 순번이며 RecoveryReason은 회수 필요의 근거다.
/// </summary>
public struct DroneLogisticsTask : IComponentData
{
    public DroneLogisticsTaskKindEnum Kind;
    public DroneLogisticsTaskStateEnum State;
    public Entity Target;
    public ItemTypeEnum ItemType;
    public ulong CreationSequence;
    public DroneRecoveryReasonEnum RecoveryReason;
}

/// <summary>
/// 역할·목적: 수행자에게 배정한 개별 수집·공급·보관·방출의 대상·수량·행동 상태를 관리한다.
/// 부착 엔티티: 현장·상위 작업·수행자와 별도인 하위 배정 엔티티다. ConstructionSupplyReservation도 같은 엔티티에 붙는다.
/// 생성: StateApply의 DroneTaskAssignmentPublishSystem이 승인 후보를 Revision=1/MovingToSource로 EndSimulation에 생성하고 수행자를 연결한다.
/// 이용: DroneTaskDecisionSystem(Decision), ConstructionSupplyReservationSystem(Reservation), DroneTaskAssignmentPublishSystem(StateApply)이 배정 유효성과 적재 재배정 후보를 검사한다.
/// DroneItemTransferDecisionSystem(Decision)·DroneItemTransferExecutionSystem(Execution)은 행동 인계 계획에, DroneTaskLifecycleApplySystem(StateApply)은 인계 성공분·예약·종료 상태 반영에 사용한다.
/// 후속 외부 수행부는 배정을 읽고 행동 신호를 제출하며 직접 성공 상태를 쓰지 않는다. 실제 이동·행동 신호 생성은 미구현이다.
/// 제거: Lifecycle이 적재품 없음·예약 정산·수행자 연결 해제·미소비 요청/결과 참조 없음 이후 EndSimulation에 삭제를 기록한다.
/// 적재품이 남으면 Retargeting으로 유지한다. 재배정은 같은 엔티티의 revision을 늘리고 최초 작업 생성 순서를 보존한다.
/// LastAppliedActionSequence는 내부 인계가 소비한 행동 번호이며 0은 미처리다. 실제 적재 품목·수량은 Worker의 StoredItemElement가 원본이다.
/// </summary>
public struct DroneTaskAssignment : IComponentData
{
    public Entity Worker;
    public Entity Task;
    public ulong OriginalTaskCreationSequence; // 재배정으로 Task가 바뀌어도 적재 그룹의 최초 작업 순서를 유지한다.
    public Entity Source;
    public Entity Destination;
    public ItemTypeEnum ItemType;
    public int AssignedQuantity;
    public uint Revision;
    public ulong LastAppliedActionSequence;
    public DroneTaskAssignmentStateEnum State;
    public DroneActionKindEnum NextAction;
    public int2 DropPosition; // DropCargo 도착 신호는 이 셀과 현재 관측 셀을 함께 대조한다.
}
