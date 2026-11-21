using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 화물의 출처와 다음 인계 자격을 나타낸다. Supply는 공사에 공급할 수 있고 Recovery는 창고를 거친다.
/// DemolitionRecovery는 선회수 재고와 확정 환급품이며 창고를 찾는 동안 화물을 보존한다.
/// 실제 수집과 인계에서 Drone Lifecycle이 갱신한다.
/// </summary>
public enum DroneCargoOriginEnum : byte
{
    None,
    Supply,
    Recovery,
    DemolitionRecovery,
    Count
}

/// <summary>
/// 역할·목적: 드론 작업을 수행할 엔티티를 식별하는 태그다. 최대 적재량이나 행동 상태를 저장하지 않는다.
/// 부착 엔티티: DroneWorkerObservation·DroneWorkerAssignment·DroneCargoState를 갖춘 드론 수행자 엔티티다.
/// 생성: DroneSpawnUtility가 초기 구성을 기록하고 DroneStationLifecycleApplySystem이 출고/수납 역할 전환을 EndSimulation에 공개한다.
/// 이용: Decision의 DroneTaskDecisionSystem이 수행자를 조회하고 DroneItemTransferDecisionSystem이 행동 자격을 검사한다.
/// DroneTaskReservationSystem(Reservation), DroneTaskExecutionSystem/DroneItemTransferExecutionSystem(Execution), DroneTaskAssignmentPublishSystem/DroneTaskLifecycleApplySystem(StateApply)도 공통 검증 Utility로 수행자 자격을 검사한다.
/// 제거: 작업·배정 종료로 제거하지 않는다. 수행자 등록 해제·삭제의 수명주기는 후속이며 엔티티 삭제 시 함께 사라진다.
/// 공통 최대 적재량은 World의 DroneCapacityState, 실제 적재 품목과 수량은 DroneCargoState가 소유한다.
/// </summary>
public struct DroneWorker : IComponentData
{
}

/// <summary>
/// 역할·목적: 관리 측에 위치·작업 가능 상태와 관측 revision을 전달하는 스냅샷이며 위치 원본을 대체하지 않는다.
/// 부착 엔티티: DroneWorker가 있는 드론 수행자 엔티티다.
/// 생성: DroneSpawnUtility가 입력 위치, Revision=1, CanAcceptTask=false로 초기화한다. 지속 관측은 DroneWorkerObservationPreparationSystem이 마지막 확정 이동 결과로 준비한다.
/// 이용: Decision의 DroneTaskDecisionSystem은 경로 출발점·후보 revision에, DroneItemTransferDecisionSystem은 행동 자격 검사에 사용한다.
/// DroneTaskReservationSystem(Reservation), DroneTaskExecutionSystem/DroneItemTransferExecutionSystem(Execution), DroneTaskAssignmentPublishSystem/DroneTaskLifecycleApplySystem(StateApply)도 관측·경로·행동의 현재성을 재검사한다.
/// 제거: 배정 종료로 제거하지 않는 수행자 관측이다. 관측 Producer는 Preparation에서 값이 바뀔 때만 Revision을 증가시킨다. 엔티티 삭제 시 함께 사라진다.
/// Position은 월드 좌표다. 관련 관측 변경 시 양수 Revision을 갱신하고 Revision=0/CanAcceptTask=false는 신규 작업 가능 상태로 해석하지 않는다.
/// 위치 연결: 이동 Execution은 LocalTransform/GridPosition을 함께 갱신하고 다음 Preparation이 이 관측을 준비한다.
/// </summary>
public struct DroneWorkerObservation : IComponentData
{
    public float3 Position;
    public uint Revision;
    public bool CanAcceptTask;
}

/// <summary>
/// 역할·목적: 관리 측이 수행자와 활성 배정을 연결하는 상태다. Null은 배정 없음이며 수행부 관측과 구분한다.
/// 부착 엔티티: DroneWorker가 있는 드론 수행자 엔티티다.
/// 생성: DroneSpawnUtility가 호출자의 EndSimulation ECB에 Assignment=Null인 초기 구성을 기록한다.
/// 이용: Decision의 DroneTaskDecisionSystem과 Reservation의 DroneTaskReservationSystem이 유휴·현재 배정 여부를 검사한다.
/// StateApply의 DroneTaskAssignmentPublishSystem이 EndSimulation에 연결하고 DroneTaskLifecycleApplySystem은 행동·무효화 처리 중 필요 시 Null로 해제한다.
/// 제거: 연결 해제는 Assignment를 Null로 설정하는 것이며 컴포넌트는 유지한다. 수행자 엔티티 삭제 시 함께 사라진다.
/// </summary>
public struct DroneWorkerAssignment : IComponentData
{
    public Entity Assignment;
}

/// <summary>
/// 역할·목적: 적재품의 품목·수량과 출처를 보존하여 공급, 회수, 철거 화물의 인계 자격을 구분한다.
/// 부착 엔티티: DroneWorker가 있는 드론 수행자 엔티티다.
/// 생성: DroneSpawnUtility가 호출자의 EndSimulation ECB에 빈 화물 상태를 기록한다.
/// 이용: StateApply의 DroneTaskLifecycleApplySystem이 성공한 수집에 출처를 설정하고 보관·공급·방출 후 빈 적재가 되면 None으로 되돌린다.
/// Decision의 DroneTaskDecisionSystem은 출처별 재배정 목적지를 구분하며 DroneItemTransferDecisionSystem 및 후속 phase의 공통 검증도 출처 제약을 확인한다.
/// 제거: 상위 작업 종료·재배정으로 지우지 않는다. 빈 적재에서 None으로 초기화하되 컴포넌트는 수행자 삭제까지 유지한다.
/// Recovery는 창고로 운반한다. DemolitionRecovery는 철거 재고와 환급 화물이며 창고를 다시 찾을 때도 자동 방출을 막는다.
/// </summary>
public struct DroneCargoState : IComponentData
{
    public ItemTypeEnum ItemType;
    public int Quantity;
    public bool BlockAutomaticRecoveryOnDrop;
    public DroneCargoOriginEnum Origin;
}
