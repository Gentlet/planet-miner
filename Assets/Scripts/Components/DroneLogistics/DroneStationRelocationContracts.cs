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

/// <summary>활성 이관 수행자에서 철거 작업의 고정 멤버 인덱스로 연결한다.</summary>
public struct DroneStationRelocation : IComponentData
{
    public Entity Task;
    public int MemberIndex;
}

/// <summary>철거 작업의 고정 인덱스. 출고 전 수량과 수납 후 결과를 수행자 수명에서 분리한다.</summary>
[InternalBufferCapacity(0)]
public struct DroneStationRelocationMemberElement : IBufferElementData
{
    public Entity Drone;
    public int SourceSlotIndex;
    public bool Finished;
    public Entity Source;
    public Entity Task;
    public Entity Destination;
    public int SlotIndex;
    public DroneStationRelocationStageEnum Stage;
    public Entity Request;
    public float2 DeparturePosition;
    public bool HasDeparted;

    public readonly bool DepartureCommitted => HasDeparted || (SourceSlotIndex >= 0 && Drone != Entity.Null);
}

/// <summary>철거 작업과 멤버 인덱스로 식별하는 목적지 슬롯 예약. 해당 멤버의 도착만 허용한다.</summary>
[InternalBufferCapacity(0)]
public struct DroneStationReservedSlotElement : IBufferElementData
{
    public Entity Task;
    public int MemberIndex;
    public int SlotIndex;
}

/// <summary>전원 자격이 준비된 원본 정거장의 이번 틱 후보다. 수납 재고 후보는 출발 슬롯으로 식별한다. 멤버 인덱스가 음수이면 빈 정거장이다.</summary>
[InternalBufferCapacity(0)]
public struct DroneStationRelocationCandidateElement : IBufferElementData
{
    public Entity Source;
    public Entity Task;
    public Entity Drone;
    public int MemberIndex;
    public int SourceSlotIndex;
    public bool Replan;
    public ulong CreationSequence;
}

/// <summary>Decision의 단계 전이 의도다. Apply는 최신 원본을 읽어 이동의 HasDeparted 변경을 보존한다.</summary>
[InternalBufferCapacity(0)]
public struct DroneStationRelocationIntentElement : IBufferElementData
{
    public Entity Task;
    public int MemberIndex;
    public Entity Drone;
    public DroneStationRelocationStageEnum Stage;
    public Entity Request;
    public bool Finish;
    public bool Completed;
    public bool SetDeparturePosition;
}

/// <summary>
/// 재배치 소유자가 요청 생성 시 부착하고 결과 소비로 요청이 삭제될 때까지 유지하는 분류 표식이다.
/// Lifecycle Decision이 존재 여부를 기록하고 최종 Apply가 재사용한다. 자기 결과만 소비하며 일반 자동 요청과 섞지 않는다.
/// </summary>
public struct DroneStationRelocationRequest : IComponentData { }
