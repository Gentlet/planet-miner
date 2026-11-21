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
/// Drone Lifecycle이 전원 도착 뒤 EndSimulation에 기록한다. Building Lifecycle이 다음 EndBuilding에 삭제와 환급 수량을 확정한다.
/// 대상은 root의 Target, 역방향 연결은 건물의 PendingBuildingDemolition.Task가 소유한다.
/// </summary>
public struct BuildingDemolitionExecutionRequest : IComponentData { }

/// <summary>
/// 기존 건물 철거 입력을 Task 요청으로 변환한 Command 소유 표식이다.
/// Command는 EndSimulation 뒤 자기 요청의 결과만 소비하며 외부 Task 요청 결과는 남긴다.
/// </summary>
public struct BuildingDemolitionAutomaticTaskRequest : IComponentData { }

/// <summary>건물 삭제 완료, 재검사 뒤 재시도, 대상 소실을 구분한다.</summary>
public enum BuildingDemolitionResultStatus : byte
{
    Completed,
    Failed,
    Retry
}

/// <summary>
/// Building Lifecycle이 철거 root에 EndBuilding으로 공개하는 실제 건물 처리 결과다.
/// Drone Lifecycle이 확정 수량을 인계한 뒤 root를 닫는다. Retry는 일반 건물의 중단을 유지하고 root를 다시 연다.
/// </summary>
public struct BuildingDemolitionResult : IComponentData
{
    public BuildingDemolitionResultStatus Status;
}

/// <summary>철거 원본과 공개 배정을 읽어 UI와 자동 출고에 제공하는 진행 정보다.</summary>
public struct DroneDemolitionProgress : IComponentData
{
    public int RemainingStock;
    public int RequiredWorkers;
    public int AssignedWorkers;
    public int ArrivedWorkers;
    public bool AwaitingRelocation;
    public bool AwaitingSpace;
}

/// <summary>EndBuilding에서 철거와 함께 확정한 수량이며 실제 화물 인계를 마친 만큼 제거한다.</summary>
[InternalBufferCapacity(0)]
public struct DroneDemolitionRefundElement : IBufferElementData
{
    public ItemTypeEnum ItemType;
    public int Quantity;
}

/// <summary>작업 Reservation 뒤 실제 슬롯 경합으로 전량 수납이 막힌 자동 출고 수요. 다음 Reservation에서 다시 작성한다.</summary>
[InternalBufferCapacity(0)]
public struct DroneDemolitionActivationBlockElement : IBufferElementData
{
    public Entity Task;
}
