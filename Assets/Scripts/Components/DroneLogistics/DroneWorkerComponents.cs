using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: 현재 적재품의 없음·공급원 수집·월드 회수 출처 제약을 구분한다.
/// 부착 엔티티: 열거형 자체는 부착하지 않으며 DroneCargoState.Origin에 포함한다.
/// 생성·이용: DroneTaskLifecycleApplySystem(StateApply)이 성공 인계로 갱신하고 Decision/인계 검증은 Recovery의 보관 전 직접 현장 공급을 차단한다.
/// 제거: 값을 담은 컴포넌트·판단·결과의 갱신/삭제 수명을 따른다. 별도 엔티티 수명을 갖지 않는다.
/// </summary>
public enum DroneCargoOriginEnum : byte
{
    None,
    Supply,
    Recovery,
    Count
}

/// <summary>
/// 역할·목적: 드론 작업을 수행할 엔티티를 식별하는 태그다. 최대 적재량이나 행동 상태를 저장하지 않는다.
/// 부착 엔티티: DroneWorkerObservation·DroneWorkerAssignment·DroneCargoState·StoredItemElement를 갖춘 드론 수행자 엔티티다.
/// 생성: 후속 수행부의 수행자 등록 경계에서 붙일 계약이며 실제 드론 생성·등록 Producer는 아직 없다.
/// 이용: Decision의 DroneTaskDecisionSystem이 수행자를 조회하고 DroneItemTransferDecisionSystem이 행동 자격을 검사한다.
/// ConstructionSupplyReservationSystem(Reservation), DroneTaskExecutionSystem/DroneItemTransferExecutionSystem(Execution), DroneTaskAssignmentPublishSystem/DroneTaskLifecycleApplySystem(StateApply)도 공통 검증 Utility로 수행자 자격을 검사한다.
/// 제거: 작업·배정 종료로 제거하지 않는다. 수행자 등록 해제·삭제의 수명주기는 후속이며 엔티티 삭제 시 함께 사라진다.
/// 공통 최대 적재량은 World의 DroneCapacityState, 실제 적재 실물은 StoredItemElement와 ItemOwnership이 소유한다.
/// </summary>
public struct DroneWorker : IComponentData
{
}

/// <summary>
/// 역할·목적: 관리 측에 위치·작업 가능 상태와 관측 revision을 전달하는 스냅샷이며 위치 원본을 대체하지 않는다.
/// 부착 엔티티: DroneWorker가 있는 드론 수행자 엔티티다.
/// 생성: 후속 수행부가 위치 원본에서 게시·갱신할 계약이다. 현재 제품 코드에 관측 Writer나 실제 드론 이동 시스템은 없다.
/// 이용: Decision의 DroneTaskDecisionSystem은 경로 출발점·후보 revision에, DroneItemTransferDecisionSystem은 행동 자격 검사에 사용한다.
/// ConstructionSupplyReservationSystem(Reservation), DroneTaskExecutionSystem/DroneItemTransferExecutionSystem(Execution), DroneTaskAssignmentPublishSystem/DroneTaskLifecycleApplySystem(StateApply)도 관측·경로·행동의 현재성을 재검사한다.
/// 제거: 배정 종료로 제거하지 않는 수행자 관측이다. 실제 Producer의 수명주기는 후속이며 엔티티 삭제 시 함께 사라진다.
/// Position은 월드 좌표다. 관련 관측 변경 시 양수 Revision을 갱신하고 Revision=0/CanAcceptTask=false는 신규 작업 가능 상태로 해석하지 않는다.
/// TODO(SoT 연결): 실제 드론 위치의 원본/Writer를 확정하고 이 관측을 원본에서 갱신하는 경계를 연결해야 한다.
/// 현재는 실행 중 SoT 위반을 재현한 것이 아니라, 위치 원본과 관측의 연결이 미완성인 상태다.
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
/// 생성: 후속 수행자 등록 시 빈 상태로 붙여야 하며 실제 등록 Producer는 아직 없다.
/// 이용: Decision의 DroneTaskDecisionSystem과 Reservation의 ConstructionSupplyReservationSystem이 유휴·현재 배정 여부를 검사한다.
/// StateApply의 DroneTaskAssignmentPublishSystem이 EndStateApply에 연결하고 DroneTaskLifecycleApplySystem은 행동·무효화 처리 중 필요 시 Null로 해제한다.
/// 제거: 연결 해제는 Assignment를 Null로 설정하는 것이며 컴포넌트는 유지한다. 수행자 엔티티 삭제 시 함께 사라진다.
/// </summary>
public struct DroneWorkerAssignment : IComponentData
{
    public Entity Assignment;
}

/// <summary>
/// 역할·목적: 현재 적재품의 Supply/Recovery 출처를 유지하여 회수품의 현장 직접 공급을 제한한다. 실물·품목·수량의 원본은 StoredItemElement다.
/// 부착 엔티티: DroneWorker가 있는 드론 수행자 엔티티다.
/// 생성: 후속 수행자 등록 시 Origin=None으로 붙여야 하며 실제 등록 Producer는 아직 없다.
/// 이용: StateApply의 DroneTaskLifecycleApplySystem이 성공한 수집에 출처를 설정하고 보관·공급·방출 후 빈 적재가 되면 None으로 되돌린다.
/// Decision의 DroneTaskDecisionSystem은 출처별 재배정 목적지를 구분하며 DroneItemTransferDecisionSystem 및 후속 phase의 공통 검증도 출처 제약을 확인한다.
/// 제거: 상위 작업 종료·재배정으로 지우지 않는다. 빈 적재에서 None으로 초기화하되 컴포넌트는 수행자 삭제까지 유지한다.
/// Recovery는 보관 건물에 넣기 전에는 현장에 직접 공급할 수 없다는 뜻이다.
/// </summary>
public struct DroneCargoState : IComponentData
{
    public DroneCargoOriginEnum Origin;
}
