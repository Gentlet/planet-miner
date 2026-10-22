using Unity.Entities;

/// <summary>일반 Storage 슬롯 뒤의 드론 전용 슬롯 수. 공통 건물 생성이 Config에서 부착하며 이후 불변이다.</summary>
public struct DroneStationStorage : IComponentData
{
    public int DedicatedSlotCount;
}

/// <summary>활성 정박 드론의 역참조. Station Lifecycle만 Docked 관계와 같은 EndSimulation에서 갱신한다.</summary>
public struct DockedDroneElement : IBufferElementData
{
    public Entity Drone;
    public int SlotIndex;
}

/// <summary>활성 드론의 정박 장소와 슬롯 원본. ItemOwnership과 상호 배타적이며 배정 상태를 복제하지 않는다.</summary>
public struct DroneDocked : IComponentData
{
    public Entity Station;
    public int SlotIndex;
}

public enum DroneActivityKind : byte
{
    Idle,
    Moving,
    Acting,
    Charging,
    WaitingForCharge
}

/// <summary>수행자 자체의 진행 중 동작. 생성은 Idle, 실제 이동/충전 Writer는 후속 Task다.</summary>
public struct DroneActivityState : IComponentData
{
    public DroneActivityKind Activity;
}

/// <summary>DroneConfig의 최대 배터리 입력. 초기화 이후 읽기 전용이며 현재량은 개체의 DroneBatteryState다.</summary>
public struct DroneBatteryConfig : IComponentData
{
    public float MaximumCharge;
}
