using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 정거장의 철거 준비 원본이다. Command가 생성/취소하고 생성 Execution이 같은 ECB에서 Task를 연결한다.
/// Reservation은 전원분 공간을 확보한 뒤 Started/TotalCount를, Relocation Apply는 완료 수만 갱신한다.
/// 실제 PendingBuildingDemolition은 전원 수납 뒤 드론 철거 확인과 같은 EndSimulation에 기록한다.
/// </summary>
public struct PreparingStationDemolition : IComponentData
{
    public Entity Task;
    public bool Started;
    public int TotalCount;
    public int CompletedCount;
}

public enum DroneStationRelocationStageEnum : byte
{
    None,
    WaitingForActivation,
    Travelling,
    Charging,
    Count
}

/// <summary>
/// 같은 Entity의 정거장 재배치 진행이다. 슬롯 예약은 목적지 역참조와 같은 소유자가 관리한다.
/// Reservation이 최초 목적지를 기록하고 무효 목적지를 재계획한다. 단계/요청은 Apply가 EndSimulation에 기록한다.
/// HasDeparted만 이동 뒤 Execution이 갱신하여 취소 시 실제 출발 여부를 구분한다.
/// </summary>
public struct DroneStationRelocation : IComponentData
{
    public Entity Source;
    public Entity Task;
    public Entity Destination;
    public int SlotIndex;
    public DroneStationRelocationStageEnum Stage;
    public Entity Request;
    public float2 DeparturePosition;
    public bool HasDeparted;
}

/// <summary>목적지의 영속 도착 슬롯 예약이다. 일반 입고/정박은 제외하고 해당 Drone만 사용할 수 있다.</summary>
[InternalBufferCapacity(0)]
public struct DroneStationReservedSlotElement : IBufferElementData
{
    public Entity Drone;
    public int SlotIndex;
}

/// <summary>전원 자격이 준비된 원본 정거장의 이번 틱 후보다. Actor Null은 빈 정거장을 뜻한다.</summary>
[InternalBufferCapacity(0)]
public struct DroneStationRelocationCandidateElement : IBufferElementData
{
    public Entity Source;
    public Entity Task;
    public Entity Drone;
    public bool Replan;
    public ulong CreationSequence;
}

/// <summary>Decision의 단계 전이 의도다. Apply는 최신 원본을 읽어 이동의 HasDeparted 변경을 보존한다.</summary>
[InternalBufferCapacity(0)]
public struct DroneStationRelocationIntentElement : IBufferElementData
{
    public Entity Drone;
    public DroneStationRelocationStageEnum Stage;
    public Entity Request;
    public bool Finish;
    public bool Completed;
    public bool SetDeparturePosition;
}

/// <summary>재배치 소유자의 정거장 요청 표식이다. 자기 결과만 소비하며 일반 자동 요청과 섞지 않는다.</summary>
public struct DroneStationRelocationRequest : IComponentData { }
