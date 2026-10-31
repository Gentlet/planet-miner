using Unity.Entities;

/// <summary>
/// 철거의 공개 도착을 판단한 이번 틱 의도다. Task Decision이 비우고 작성하며 Lifecycle이 최신 배정으로 소비한다.
/// 실제 건물 제거 승인은 이 버퍼가 아니라 Lifecycle이 EndSimulation에 기록하는 실행 요청이다.
/// </summary>
[InternalBufferCapacity(0)]
public struct DroneDemolitionDecisionElement : IBufferElementData
{
    public Entity Assignment;
    public uint AssignmentRevision;
    public uint WorkerObservationRevision;
}

/// <summary>
/// 드론 철거 확인이 끝난 root에 붙는 일회성 건물 실행 요청이다.
/// Drone Lifecycle만 EndSimulation에 기록하며 Building Lifecycle이 다음 EndBuilding의 반환/환급/삭제에 소비한다.
/// 대상은 root의 Target, 역방향 연결은 건물의 PendingBuildingDemolition.Task가 소유한다.
/// </summary>
public struct BuildingDemolitionExecutionRequest : IComponentData { }

/// <summary>
/// 기존 건물 철거 입력을 Task 요청으로 변환한 Command 소유 표식이다.
/// Command는 EndSimulation 뒤 자기 요청의 결과만 소비하며 외부 Task 요청 결과는 남긴다.
/// </summary>
public struct BuildingDemolitionAutomaticTaskRequest : IComponentData { }

/// <summary>건물 소유자가 실제 반환/환급/삭제 기록의 완료 또는 실패를 구분한다.</summary>
public enum BuildingDemolitionResultStatus : byte
{
    Completed,
    Failed
}

/// <summary>
/// Building Lifecycle이 철거 root에 EndBuilding으로 공개하는 실제 건물 처리 결과다.
/// 드론은 결과를 읽은 뒤 root를 닫고 참조 정리 후 삭제하며, 확인을 보냈다는 사실만으로 성공을 만들지 않는다.
/// </summary>
public struct BuildingDemolitionResult : IComponentData
{
    public BuildingDemolitionResultStatus Status;
}
