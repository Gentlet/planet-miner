using Unity.Entities;

public enum DroneStateEnum : byte
{
    Stored,
    MovingToPickup,
    PickingUp,
    MovingToDelivery,
    Delivering,
    MovingToRecoveryStorage,
    RecoveringCargo,
    Returning,
    AwaitingCharge,
    EmergencyReturning,
    MovingToDemolition,
    Demolishing,
    MovingToWorldItem,
    PickingUpWorldItem,
    MovingToWorldItemStorage,
    DeliveringWorldItem,
    AwaitingStorage,
    AwaitingDispatch,
    Count
}

public enum DroneRecoveryReasonEnum : byte
{
    TargetUnavailable,
    InsufficientBattery,
    TransferFailed,
    Count
}

public struct ActiveDrone : IComponentData
{
    public int carryingCapacity;
    public float movementSpeed;
    public float emergencyMovementSpeed;
}

public struct DroneBattery : IComponentData
{
    public float current;
    public float maximum;
    public float consumptionPerDistance;
}

public struct DroneState : IComponentData
{
    public DroneStateEnum value;
}

public struct DroneAssignment : IComponentData
{
    public Entity taskEntity;
    public Entity reservationEntity;
    public Entity sourceOwner;
    public Entity destinationOwner;
    public Entity returnStation;
    public int networkId;
    public bool emergencyReturn;
}

public struct DroneCargo : IComponentData
{
    public ItemTypeEnum itemType;
    public int quantity;
}

public struct DroneRecoveryRequest : IComponentData
{
    public DroneRecoveryReasonEnum reason;
}

public struct DroneReservationAssignment : IComponentData
{
    public Entity droneEntity;
}

public struct DronePrefab : IComponentData
{
    public Entity value;
}

public struct ValidationDrone : IComponentData
{
}
