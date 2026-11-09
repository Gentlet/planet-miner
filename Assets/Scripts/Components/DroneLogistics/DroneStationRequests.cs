using Unity.Entities;

public enum DroneStationOperation : byte
{
    Activate,
    Dock,
    Store
}

/// <summary>외부 접수 API가 순번과 Decision을 함께 부착한다. 결과는 같은 요청 Entity에 EndSimulation 공개한다.</summary>
public struct DroneStationRequest : IRequestComponent
{
    public Entity Drone;
    public Entity Station;
    public DroneStationOperation Operation;
    public ulong Sequence;
}

public enum DroneStationRequestStatus : byte
{
    Rejected,
    Applied,
    Busy,
    NoSpace,
    Duplicate
}

public struct DroneStationDecision : IComponentData
{
    public bool Eligible;
    public bool Approved;
    // Decision이 기록하는 요청 분류 스냅샷. Reservation은 보존하고 최종 Apply가 재사용한다.
    public bool IsRelocationRequest;
    public bool IsAutomaticRequest;
    public int SlotIndex;
    public DroneStationRequestStatus Status;
}

public struct DroneStationResult : IComponentData
{
    public Entity Drone;
    public DroneStationOperation Operation;
    public DroneStationRequestStatus Status;
}

public struct DroneStationRequestSequence : IComponentData
{
    public ulong NextValue;
}
