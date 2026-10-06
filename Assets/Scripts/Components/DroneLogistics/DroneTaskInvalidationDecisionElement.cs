using Unity.Entities;

/// <summary>
/// 역할·목적: 상위 작업 닫기와 개별 배정 무효화 의도를 구분한다.
/// 부착 엔티티: 열거형 자체는 부착하지 않으며 DroneTaskInvalidationDecisionElement.Kind에 포함한다.
/// 생성·이용: DroneTaskDecisionSystem(Decision)이 판단하고 DroneTaskLifecycleApplySystem(StateApply)이 대상/현재 상태를 재검사하여 반영한다.
/// 제거: 값을 담은 컴포넌트·판단·결과의 갱신/삭제 수명을 따른다. 별도 엔티티 수명을 갖지 않는다.
/// </summary>
public enum DroneTaskInvalidationDecisionKindEnum : byte
{
    None,
    CloseTask,
    InvalidateAssignment,
    Count
}

/// <summary>
/// 역할·목적: 상위 작업 닫기·개별 배정 무효화의 판단을 원본 상태 변경과 분리하여 전달한다.
/// 부착 엔티티: DroneTaskCandidateDecisionElement와 함께 있는 World 단일 후보 관리 엔티티의 버퍼다.
/// 생성: DroneSchedulingUtility.GetOrCreateCandidates가 버퍼를 준비하고 Decision의 DroneTaskDecisionSystem이 매 틱 무효화 의도를 작성한다.
/// 이용: StateApply의 DroneTaskLifecycleApplySystem이 대상·배정 revision·현재 상태를 재검사하여 Closed/Cancelled/Retargeting·수행자 연결·삭제 보류를 반영한다.
/// 제거: Lifecycle이 의도 처리 후 버퍼 내용을 비운다. 버퍼 자체와 관리 엔티티는 유지한다.
/// </summary>
[InternalBufferCapacity(0)]
public struct DroneTaskInvalidationDecisionElement : IBufferElementData
{
    public DroneTaskInvalidationDecisionKindEnum Kind;
    public Entity Target;
    public uint AssignmentRevision;
}
