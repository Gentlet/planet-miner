using Unity.Entities;

/// <summary>
/// 역할·목적: 경로 요청의 Create/Retain/Remove 의도를 명시하여 Execution이 요청 필요성을 다시 탐색하지 않게 한다.
/// 부착 엔티티: DroneTaskCandidateDecisionElement와 함께 있는 World 단일 후보 관리 엔티티의 버퍼다.
/// 생성: DroneSchedulingUtility.GetOrCreateCandidates가 버퍼를 준비하고 Decision의 DroneTaskDecisionSystem이 매 틱 의도를 작성한다.
/// 이용: Execution의 DroneTaskExecutionSystem이 적용 시점의 유효성을 검사하고 생성·삭제를 EndStateApply ECB에 기록한다. Retain은 변경하지 않는다.
/// 제거: Execution이 의도 소비 후 버퍼 내용을 비운다. 버퍼 자체와 관리 엔티티는 유지한다.
/// 이후 phase에서 원본이 바뀐 요청은 다음 Decision에서 재검사한다.
/// </summary>
[InternalBufferCapacity(0)]
public struct DroneRouteDecisionElement : IBufferElementData
{
    public DroneRouteDecisionKindEnum Kind;
    public Entity ExistingRequest;
    public DroneRouteEvaluationRequest Request;
}

/// <summary>
/// 역할·목적: 경로 요청 Create/Retain/Remove 의도를 구분한다.
/// 부착 엔티티: 열거형 자체는 부착하지 않으며 DroneRouteDecisionElement.Kind에 포함한다.
/// 생성·이용: DroneTaskDecisionSystem(Decision)이 의도를 정하고 DroneTaskExecutionSystem(Execution)이 생성·삭제를 EndStateApply에 기록한다. Retain은 상태 변경이 없다.
/// 제거: 값을 담은 컴포넌트·판단·결과의 갱신/삭제 수명을 따른다. 별도 엔티티 수명을 갖지 않는다.
/// </summary>
public enum DroneRouteDecisionKindEnum : byte
{
    None,
    Create,
    Retain,
    Remove,
    Count
}
