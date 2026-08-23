using Unity.Entities;

public struct DroneTaskReservationRequest : IComponentData
{
    public Entity taskEntity;
    public Entity sourceOwner;
    public Entity destinationOwner;
    public ItemTypeEnum itemType;
    public int quantity;
}

public struct DroneTaskReservation : IComponentData
{
    public Entity taskEntity;
    public Entity destinationOwner;
    public ItemTypeEnum itemType;
    public int quantity;
}

public enum DroneTaskItemSourceKind : byte
{
    Stored,
    Produced
}

public struct DroneTaskReservedItemElement : IBufferElementData
{
    public Entity itemEntity;
    public Entity sourceOwner;
    public DroneTaskItemSourceKind sourceKind;
}

public struct DroneItemReservation : IComponentData
{
    public Entity reservationEntity;
    public Entity taskEntity;
}

public struct DroneReservedStorageCapacityElement : IBufferElementData
{
    public ItemTypeEnum itemType;
    public int quantity;
}
