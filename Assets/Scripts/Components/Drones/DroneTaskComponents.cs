using Unity.Entities;
using Unity.Mathematics;

public enum DroneTaskTypeEnum : byte
{
    Construction,
    Demolition,
    RemoveBuildingItem,
    InsertBuildingItem,
    RecoverWorldItem,
    Count
}

public enum DroneTaskStateEnum : byte
{
    Pending,
    InProgress,
    Suspended,
    Completed,
    Cancelled,
    Count
}

public enum DroneTaskPriorityClassEnum : byte
{
    Normal,
    Emergency,
    Count
}

public enum DroneTaskAutomaticSuspensionReasonEnum : byte
{
    MissingSourceItems,
    DestinationCapacityUnavailable,
    OutsideDroneNetwork,
    Count
}

public struct DroneTask : IComponentData
{
    public DroneTaskTypeEnum type;
}

public struct DroneTaskStatus : IComponentData
{
    public DroneTaskStateEnum state;
    public DroneTaskStateEnum stateBeforeSuspension;
}

public struct DroneTaskPriority : IComponentData
{
    public DroneTaskPriorityClassEnum priorityClass;
    public int normalPriority;
}

public struct DroneTaskAutomaticSuspension : IComponentData
{
    public DroneTaskAutomaticSuspensionReasonEnum reason;
}

public struct DroneTaskCreationOrder : IComponentData
{
    public ulong value;
}

public struct DroneTaskQuantity : IComponentData
{
    public int totalQuantity;
    public int deliveredQuantity;
    public int reservedQuantity;

    public int UnreservedQuantity =>
        totalQuantity - deliveredQuantity - reservedQuantity;
}

public struct DroneTaskSchedulerState : IComponentData
{
    public ulong nextCreationOrder;
}

public struct DroneTaskCreateRequest : IComponentData
{
    public DroneTaskTypeEnum type;
    public DroneTaskPriorityClassEnum priorityClass;
    public int normalPriority;
    public int totalQuantity;
}

public struct DroneBuildingItemTaskData : IComponentData
{
    public Entity targetBuilding;
    public ItemTypeEnum itemType;
}

public struct DroneWorldItemRecoveryTaskData : IComponentData
{
    public Entity itemEntity;
}

public struct DroneWorldItemRecoveryCreateRequest : IComponentData
{
    public Entity itemEntity;
    public int normalPriority;
}

public struct DroneDemolitionRequest : IComponentData
{
    public int2 gridPosition;
    public int normalPriority;
}

public struct DroneDemolitionTaskData : IComponentData
{
    public Entity targetBuilding;
}

public struct DroneDirectTaskAssignment : IComponentData
{
    public Entity droneEntity;
    public Entity destinationOwner;
    public ItemTypeEnum itemType;
    public bool hasReservedDestinationCapacity;
}

public struct DroneTaskPriorityChangeRequest : IComponentData
{
    public Entity taskEntity;
    public DroneTaskPriorityClassEnum priorityClass;
    public int normalPriority;
}

public struct DroneTaskSuspensionRequest : IComponentData
{
    public Entity taskEntity;
    public bool suspend;
}

public struct DroneTaskCancelRequest : IComponentData
{
    public Entity taskEntity;
}
