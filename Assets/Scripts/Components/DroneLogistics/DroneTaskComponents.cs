using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 역할/목적: 현장/품목별 공급, 월드 실물별 회수, 일반 보관품의 일회성 반입/반출과 건물 철거 상위 작업을 구분한다.
/// 부착 엔티티: 열거형 자체는 부착하지 않으며 DroneLogisticsTask.Kind·생성 의도에 포함한다.
/// 생성/이용: DroneTaskDecisionSystem(Decision)이 종류별 필요를 판단하고 DroneTaskExecutionSystem(Execution)이 작업을 생성한다. Reservation은 공급/비공급 선두를 따로 비교한다.
/// 제거: 값을 담은 컴포넌트·판단·결과의 갱신/삭제 수명을 따른다. 별도 엔티티 수명을 갖지 않는다.
/// </summary>
public enum DroneLogisticsTaskKindEnum : byte
{
    None,
    ConstructionSupply,
    WorldItemRecovery,
    StorageImport,
    StorageExport,
    BuildingDemolition,
    Count
}

/// <summary>
/// 역할/목적: 상위 작업의 Open/Closed/Cancelled와 건물 결과를 기다리는 ExecutionConfirmed 상태를 구분한다.
/// 부착 엔티티: 열거형 자체는 부착하지 않으며 DroneLogisticsTask.State에 포함한다.
/// 생성/이용: Task Execution이 Open을 기록한다. Lifecycle은 일반 종료 Closed/명시 취소 Cancelled를, 드론 철거 확인은 ExecutionConfirmed를 EndSimulation에 기록한다. 확인 이후 취소는 거부하며 건물 결과를 읽은 뒤 Closed로 전환한다.
/// 제거: 값을 담은 컴포넌트·판단·결과의 갱신/삭제 수명을 따른다. 별도 엔티티 수명을 갖지 않는다.
/// </summary>
public enum DroneLogisticsTaskStateEnum : byte
{
    None,
    Open,
    Closed,
    Cancelled,
    ExecutionConfirmed,
    Count
}

/// <summary>
/// 역할/목적: 현장 정리/드론 방출/철거 반환/사용자 선택의 월드 실물 회수 근거를 구분한다.
/// 부착 엔티티: 열거형 자체는 부착하지 않으며 DroneLogisticsTask.RecoveryReason·생성 의도에 포함한다.
/// 생성/이용: Task Decision이 현장/반환 표시 또는 명시 요청으로 선택한다. 사용자 선택은 일반 바닥 자격을, 기존 자동 회수는 각 사유의 현재성을 검사한다.
/// 제거: 값을 담은 컴포넌트·판단·결과의 갱신/삭제 수명을 따른다. 별도 엔티티 수명을 갖지 않는다.
/// </summary>
public enum DroneRecoveryReasonEnum : byte
{
    None,
    SiteClearance,
    DroneDrop,
    BuildingDemolition,
    UserRequested,
    Count
}

/// <summary>
/// 역할·목적: 배정의 이동·수집/공급 대기·재배정·완료/취소 상태를 구분한다.
/// 부착 엔티티: 열거형 자체는 부착하지 않으며 DroneTaskAssignment.State에 포함한다.
/// 생성/이용: DroneTaskAssignmentPublishSystem이 초기/재배정을, DroneTaskLifecycleApplySystem이 행동 결과/무효화를 기록한다. DroneMovementApplySystem은 실제 도착 상태를 EndSimulation에 기록한다.
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
/// 역할·목적: 공급/회수/일반 운반 요구와 건물 철거를 수행자별 배정과 분리하는 상위 작업이다.
/// 부착 엔티티: 현장·아이템·수행자·배정과 별도인 상위 작업 엔티티다. Target은 현장/회수 실물/고정 보관처/철거 건물이며 철거 ItemType은 None이다.
/// 생성: Decision의 DroneTaskDecisionSystem이 의도를 작성하고 Execution의 DroneTaskExecutionSystem이 순번을 발급하여 EndSimulation에 생성한다.
/// 이용: 다음 틱부터 Decision의 DroneTaskDecisionSystem이 후보를 판단하고 Reservation의 DroneTaskReservationSystem이 생성 순서·PlacementStamp에 따른 선두를 선택한다.
/// StateApply의 AssignmentPublish는 배정에 작업을 연결하고 TaskLifecycle은 일반 종료 Closed/명시 취소 Cancelled를, 철거 확인은 EndSimulation의 ExecutionConfirmed를 기록한다. 건물 결과가 있어야 확인된 root를 닫는다.
/// 제거: Lifecycle이 Closed/Cancelled 작업의 연결 배정과 미정산 확보량이 없음을 확인한 뒤 EndSimulation에 삭제를 기록한다.
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
    /// <summary>
    /// 최초 RecoveryReason과 별개로 같은 실물 root에 합쳐진 승인된 사용자 회수 의도다.
    /// 새 root는 Execution이 생성과 함께 기록하고 기존 root는 Lifecycle이 성공한 명시 요청에만 반영한다.
    /// 요청 Entity 소비 뒤에도 유지하며 root의 명시 취소/종료와 함께 수명을 마친다.
    /// </summary>
    public bool HasExplicitRecoveryIntent;
}

/// <summary>
/// 역할·목적: 수행자에게 배정한 개별 수집/공급/보관/방출/철거의 대상과 수량/행동 상태를 관리한다.
/// 부착 엔티티: 현장·상위 작업·수행자와 별도인 하위 배정 엔티티다. 현장 공급/회수는 ConstructionSupplyReservation, 일반 운반은 DroneTransportReservation을 같은 엔티티에 붙인다.
/// 생성: StateApply의 DroneTaskAssignmentPublishSystem이 승인 후보를 Revision=1로 EndSimulation에 생성하고 수행자를 연결한다.
/// 철거 배정은 MovingToDestination으로, 수집이 필요한 배정은 MovingToSource로 시작한다.
/// 이용: DroneTaskDecisionSystem(Decision), DroneTaskReservationSystem(Reservation), DroneTaskAssignmentPublishSystem(StateApply)이 배정 유효성과 적재 재배정 후보를 검사한다.
/// DroneItemTransferDecisionSystem(Decision)·DroneItemTransferExecutionSystem(Execution)은 행동 인계 계획에, DroneTaskLifecycleApplySystem(StateApply)은 인계 성공분·예약·종료 상태 반영에 사용한다.
/// 이동 Decision/Execution은 배정 목표로 비행하고 Movement Apply가 도착 상태만 기록한다. 후속 행동 수행부는 신호를 제출하며 실물 인계 성공 상태를 직접 쓰지 않는다.
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
