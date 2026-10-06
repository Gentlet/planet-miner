using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: 공급원을 경유하는 전체 경로와 현재 위치에서 목적지로 가는 Direct 평가를 구분한다.
/// 부착 엔티티: 열거형 자체는 부착하지 않으며 DroneRouteEvaluationRequest.Kind에 포함한다.
/// 생성·이용: DroneTaskDecisionSystem(Decision)이 선택하고 DroneTaskExecutionSystem(Execution)이 요청을 게시하며 후속 외부 평가자가 경로를 계산할 계약이다.
/// 제거: 값을 담은 컴포넌트·판단·결과의 갱신/삭제 수명을 따른다. 별도 엔티티 수명을 갖지 않는다.
/// </summary>
public enum DroneRouteKindEnum : byte
{
    None,
    ViaSource,
    Direct,
    Count
}

/// <summary>
/// 역할·목적: 미평가·도달 가능·도달 불가의 경로 평가 결과를 구분한다.
/// 부착 엔티티: 열거형 자체는 부착하지 않으며 DroneRouteEvaluationResult.Status에 포함한다.
/// 생성·이용: 후속 외부 경로 평가자가 작성할 계약이며 현재 Producer는 없다. Decision/Reservation/Publish는 유효한 Reachable 결과만 선택한다.
/// 제거: 값을 담은 컴포넌트·판단·결과의 갱신/삭제 수명을 따른다. 별도 엔티티 수명을 갖지 않는다.
/// </summary>
public enum DroneRouteEvaluationStatusEnum : byte
{
    None,
    Reachable,
    Unreachable,
    Count
}

/// <summary>
/// 역할·목적: 외부 경로 평가에 전달하는 불변 입력이다. 실제 위치의 원본이나 도달 가능 판정 자체가 아니다.
/// 부착 엔티티: 수행자·배정·후보 관리 엔티티와 별도인 경로 요청 엔티티다. 결과도 같은 엔티티에 붙는다.
/// 생성: Decision의 DroneTaskDecisionSystem이 생성 의도를 작성하고 Execution의 DroneTaskExecutionSystem이 EndSimulation에 게시한다.
/// 이용: 외부 평가자는 충전 경유를 포함한 실제 경로를 계산한다. 이 Producer는 미구현이다.
/// DroneTaskDecisionSystem(Decision), ConstructionSupplyReservationSystem(Reservation), DroneTaskAssignmentPublishSystem(StateApply)은 관측 revision·배정·대상 좌표를 재검사해 유효한 결과만 후보 선택에 사용한다.
/// 제거: Decision이 사용하지 않거나 유효하지 않은 요청의 Remove 의도를 작성하고 Execution이 EndSimulation에 결과를 포함한 엔티티 삭제를 기록한다.
/// ViaSource는 OriginPosition → Source → Destination, Direct는 현재 위치 → Destination을 평가하며 Direct의 Source는 Null이다.
/// SourcePosition/DestinationPosition은 대상 GridPosition의 스냅샷이다. 배정 전 평가는 Assignment=Null/revision=0, 적재 재배정은 현재 배정·revision을 사용한다.
/// IsDropPositionSearch이면 Source/Destination은 Null이며 가장 가까운 도달 가능 현장 외부 셀의 평가를 요청한다.
/// 관리층은 검색 범위나 기본 거리를 만들지 않는다. 방출 위치가 미평가/Unreachable이면 적재품을 유지하며 기다린다.
/// </summary>
public struct DroneRouteEvaluationRequest : IRequestComponent
{
    public Entity Worker;
    public Entity Assignment;
    public uint AssignmentRevision;
    public uint EvaluationRevision;
    public uint WorkerObservationRevision;
    public DroneRouteKindEnum Kind;
    public bool IsDropPositionSearch;
    public float3 OriginPosition;
    public Entity Source;
    public int2 SourcePosition;
    public Entity Destination;
    public int2 DestinationPosition;
}

/// <summary>
/// 역할·목적: 요청된 경로의 도달 가능 여부·실제 총 거리·선택된 방출 셀을 전달하는 외부 평가 결과다.
/// 부착 엔티티: 대응하는 DroneRouteEvaluationRequest와 같은 경로 요청 엔티티다.
/// 생성: 후속 외부 경로 평가자가 게시할 계약이며 현재 제품 코드에 평가 Producer는 없다.
/// 이용: Decision의 DroneTaskDecisionSystem, Reservation의 ConstructionSupplyReservationSystem, StateApply의 DroneTaskAssignmentPublishSystem이 읽는다.
/// 양수 EvaluationRevision과 요청·관측·배정·대상 스냅샷이 유효해야 하며 Reachable일 때만 거리를 후보 비교에 사용한다.
/// 제거: Decision의 Remove 의도를 소비한 DroneTaskExecutionSystem이 EndSimulation에 경로 요청 엔티티와 함께 삭제한다.
/// TotalDistance는 충전 경유를 포함한 유한한 0 이상 실제 이동거리다. 기본 None/거리 0만으로 도달 가능이라고 해석하지 않는다.
/// </summary>
public struct DroneRouteEvaluationResult : IComponentData
{
    public uint EvaluationRevision;
    public DroneRouteEvaluationStatusEnum Status;
    public float TotalDistance;
    public bool HasDropPosition; // 검색 성공 좌표를 명시하며 기본 0 좌표를 성공으로 해석하지 않는다.
    public int2 DropPosition;
}
