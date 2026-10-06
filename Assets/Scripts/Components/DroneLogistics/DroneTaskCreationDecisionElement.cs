using Unity.Entities;

/// <summary>
/// 역할·목적: 공급·회수 필요 판단을 실제 작업 생성과 분리하여 전달하는 생성 의도다.
/// 부착 엔티티: DroneTaskCandidateDecisionElement와 함께 있는 World 단일 후보 관리 엔티티의 버퍼다.
/// 생성: DroneSchedulingUtility.GetOrCreateCandidates가 버퍼를 준비하고 Decision의 DroneTaskDecisionSystem이 현재 요구·회수 필요를 판단하여 채운다.
/// 이용: Execution의 DroneTaskExecutionSystem이 대상·타입·중복을 검사하고 순번 발급과 EndStateApply 작업 생성 명령을 기록한다.
/// 제거: Execution이 생성 명령 처리 후 버퍼 내용을 비운다. 버퍼 자체와 관리 엔티티는 유지한다.
/// 실제 작업 엔티티는 EndStateApply에 공개되어 다음 틱부터 배정 판단에 사용한다.
/// </summary>
[InternalBufferCapacity(0)]
public struct DroneTaskCreationDecisionElement : IBufferElementData
{
    public DroneLogisticsTaskKindEnum Kind;
    public Entity Target;
    public ItemTypeEnum ItemType;
    public DroneRecoveryReasonEnum RecoveryReason;
}
