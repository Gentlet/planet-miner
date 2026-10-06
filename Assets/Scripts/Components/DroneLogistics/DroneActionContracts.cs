using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: 수집·회수·현장 공급·보관·월드 방출 행동을 구분한다.
/// 부착 엔티티: 열거형 자체는 부착하지 않으며 DroneActionIdentity.Kind·DroneTaskAssignment.NextAction·후보에 포함한다.
/// 생성·이용: 외부 행동 신호와 Decision/Publish가 행동을 선택하고 인계 Decision·Execution·Lifecycle이 같은 종류로 검증·적용한다.
/// 제거: 값을 담은 컴포넌트·판단·결과의 갱신/삭제 수명을 따른다. 별도 엔티티 수명을 갖지 않는다.
/// </summary>
public enum DroneActionKindEnum : byte
{
    None,
    CollectFromStorage,
    RecoverWorldItem,
    SupplyConstructionSite,
    StoreCargo,
    DropCargo,
    Count
}

/// <summary>
/// 역할·목적: 내부 실물 인계의 완료·부분 성공·불가·거부 결과를 구분한다.
/// 부착 엔티티: 열거형 자체는 부착하지 않으며 DroneItemTransferResult.Status에 포함한다.
/// 생성·이용: DroneTaskLifecycleApplySystem(StateApply)이 실제 옮긴 수량으로 확정하고 외부 TryConsumeResult가 읽는다. None은 미결정이다.
/// 제거: 값을 담은 컴포넌트·판단·결과의 갱신/삭제 수명을 따른다. 별도 엔티티 수명을 갖지 않는다.
/// </summary>
public enum DroneItemTransferStatusEnum : byte
{
    None,
    Completed,
    Partial,
    Unavailable,
    Rejected,
    Count
}

/// <summary>
/// 역할·목적: 행동 신호가 어느 배정의 어떤 행동인지 식별하여 중복 인계와 재배정 이전 신호를 구분한다.
/// 부착 엔티티: 독립 ECS 컴포넌트가 아니며 요청·결과 컴포넌트에 포함하는 값 형식이다.
/// 생성: 외부 수행부가 현재 배정의 엔티티·양수 revision/sequence·Worker·행동·Target을 채운다. 실제 수행부는 미구현이다.
/// 이용: Decision의 DroneItemTransferDecisionSystem과 StateApply의 DroneTaskLifecycleApplySystem이 현재 배정과 대조한다.
/// 제거: 이 값을 포함한 요청이 처리되거나 결과 엔티티가 소비될 때 함께 사라진다.
/// DropCargo의 Target은 Null이며 다른 행동은 해당 공급원·회수품·목적지다.
/// </summary>
public struct DroneActionIdentity
{
    public Entity Assignment;
    public uint AssignmentRevision;
    public ulong Sequence;
    public Entity Worker;
    public DroneActionKindEnum Kind;
    public Entity Target;
}

/// <summary>
/// 역할·목적: 도착·행동 완료 후 실물 인계를 시도하도록 알리는 외부 입력이다. 성공 수량은 입력하지 않는다.
/// 부착 엔티티: 배정·수행자와 별도인 행동 요청 엔티티이며 인계 판단·실물 후보·슬롯 계획과 함께 존재한다.
/// 생성: 외부 입력 경계의 DroneActionRequestUtility.Submit이 World 접수 순번을 발급하여 즉시 게시한다. 실제 수행부의 호출은 미구현이다.
/// 이용: Decision의 DroneItemTransferDecisionSystem이 자격을 판단하고 Execution의 DroneItemTransferExecutionSystem이 계획을 준비한다.
/// StateApply의 DroneTaskLifecycleApplySystem이 접수 순서대로 현재 실물·배정·대상을 재검사하여 인계와 결과 정산을 수행한다.
/// 제거: Lifecycle이 요청·계획 제거와 같은 엔티티의 결과 추가를 EndStateApply에 기록한다. 결과 엔티티는 외부 소비까지 남는다.
/// WorldPosition은 DropCargo의 선택 목표 및 WorkerObservationRevision에 해당하는 현재 관측 셀과 대조할 위치다.
/// </summary>
public struct DroneActionReadyRequest : IRequestComponent
{
    public ulong ReceiptSequence;
    public DroneActionIdentity Action;
    public uint WorkerObservationRevision;
    public int2 WorldPosition;
}

/// <summary>
/// 역할·목적: 외부 행동 완료 신호에 대한 내부 인계 결과와 실제 옮긴 실물 개수를 공개한다.
/// 부착 엔티티: 처리한 DroneActionReadyRequest와 같은 엔티티에 부착되며 요청·계획 제거 후에도 유지된다.
/// 생성: StateApply의 DroneTaskLifecycleApplySystem이 실물 인계·현장 도착량·예약 정산 후 EndStateApply에 게시한다.
/// 이용: 외부 수행부는 DroneActionRequestUtility.TryConsumeResult로 읽는다. Lifecycle은 미소비 결과의 배정 참조를 삭제 보류에 사용한다.
/// 제거: EndStateApply 공개 후 TryConsumeResult가 성공하면 결과 엔티티를 삭제한다. 소비되지 않은 결과는 보존한다.
/// Completed/Partial은 양수 MovedQuantity, Unavailable/Rejected는 0이며 기본 None은 미결정이다.
/// </summary>
public struct DroneItemTransferResult : IComponentData
{
    public DroneActionIdentity Action;
    public DroneItemTransferStatusEnum Status;
    public int MovedQuantity;
}
