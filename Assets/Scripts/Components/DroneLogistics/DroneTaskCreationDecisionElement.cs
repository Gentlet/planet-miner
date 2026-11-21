using Unity.Entities;

/// <summary>
/// 역할·목적: 공급, 월드 회수와 영역별 반출의 판단을 실제 작업 생성과 분리한다.
/// 부착 엔티티: DroneTaskCandidateDecisionElement와 함께 있는 World 단일 후보 관리 엔티티의 버퍼다.
/// 생성: DroneSchedulingUtility.GetOrCreateCandidates가 준비한다. DroneTaskDecisionSystem이 필요 작업을 기록하고 철거 반출·벨트 회수는 DemolitionRoot를 연결한다.
/// 이용: Execution의 DroneTaskExecutionSystem이 대상·타입·중복을 검사하고 순번 발급과 EndSimulation 작업 생성 명령을 기록한다.
/// 제거: Execution이 생성 명령 처리 후 버퍼 내용을 비운다. 버퍼 자체와 관리 엔티티는 유지한다.
/// 실제 작업 엔티티는 EndSimulation에 공개되어 다음 틱부터 배정 판단에 사용한다.
/// </summary>
[InternalBufferCapacity(0)]
public struct DroneTaskCreationDecisionElement : IBufferElementData
{
    public DroneLogisticsTaskKindEnum Kind;
    public Entity Target;
    public Entity DemolitionRoot;
    public ItemTypeEnum ItemType;
    public DroneRecoveryReasonEnum RecoveryReason;
    public int Quantity;
    public DroneTransportAreaEnum Area;
    public DroneTransportPurposeEnum Purpose;
}
