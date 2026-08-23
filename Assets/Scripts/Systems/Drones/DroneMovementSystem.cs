using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateAfter(typeof(DroneDispatchSystem))]
public partial class DroneMovementSystem : SystemBase
{
    private const float ArrivalDistance = 0.001f;

    private EntityQuery _movingDroneQuery;

    protected override void OnCreate()
    {
        _movingDroneQuery = GetEntityQuery(
            ComponentType.ReadOnly<ActiveDrone>(),
            ComponentType.ReadWrite<DroneBattery>(),
            ComponentType.ReadWrite<DroneState>(),
            ComponentType.ReadOnly<DroneAssignment>(),
            ComponentType.ReadWrite<LocalTransform>(),
            ComponentType.ReadWrite<GridPosition>());
    }

    protected override void OnUpdate()
    {
        float deltaTime = SystemAPI.Time.DeltaTime;

        if (deltaTime <= 0f)
            return;

        using NativeArray<Entity> droneEntities =
            _movingDroneQuery.ToEntityArray(Allocator.Temp);

        for (int i = 0; i < droneEntities.Length; i++)
            MoveDrone(droneEntities[i], deltaTime);
    }

    private void MoveDrone(Entity droneEntity, float deltaTime)
    {
        DroneState state = EntityManager
            .GetComponentData<DroneState>(droneEntity);

        if (!IsMovingState(state.value))
            return;

        DroneAssignment assignment = EntityManager
            .GetComponentData<DroneAssignment>(droneEntity);

        if (!TryGetMovementTarget(
                state.value,
                assignment,
                out float3 targetPosition))
        {
            DroneRecoveryRequestUtility.Request(
                EntityManager,
                droneEntity,
                DroneRecoveryReasonEnum.TargetUnavailable);
            return;
        }

        LocalTransform transform = EntityManager
            .GetComponentData<LocalTransform>(droneEntity);
        float3 displacement = targetPosition - transform.Position;
        float remainingDistance = math.length(displacement.xy);

        if (remainingDistance <= ArrivalDistance)
        {
            ArriveAtTarget(droneEntity, state.value, targetPosition);
            return;
        }

        ActiveDrone drone = EntityManager
            .GetComponentData<ActiveDrone>(droneEntity);
        DroneBattery battery = EntityManager
            .GetComponentData<DroneBattery>(droneEntity);
        bool emergencyReturn =
            state.value == DroneStateEnum.EmergencyReturning ||
            state.value == DroneStateEnum.MovingToRecoveryStorage &&
            assignment.emergencyReturn;

        if (!emergencyReturn)
        {
            float requiredBattery =
                remainingDistance * battery.consumptionPerDistance;

            if (battery.current < requiredBattery)
            {
                DroneRecoveryRequestUtility.Request(
                    EntityManager,
                    droneEntity,
                    DroneRecoveryReasonEnum.InsufficientBattery);
                return;
            }
        }

        float speed = emergencyReturn
            ? drone.emergencyMovementSpeed
            : drone.movementSpeed;
        float traveledDistance = math.min(
            remainingDistance,
            speed * deltaTime);
        transform.Position += displacement / remainingDistance *
                              traveledDistance;
        EntityManager.SetComponentData(droneEntity, transform);
        EntityManager.SetComponentData(
            droneEntity,
            new GridPosition
            {
                gridPosition = transform.Position.ToGridCell()
            });

        if (!emergencyReturn)
        {
            battery.current = math.max(
                0f,
                battery.current - traveledDistance *
                battery.consumptionPerDistance);
            EntityManager.SetComponentData(droneEntity, battery);
        }

        if (traveledDistance >= remainingDistance)
            ArriveAtTarget(droneEntity, state.value, targetPosition);
    }

    private bool TryGetMovementTarget(
        DroneStateEnum state,
        DroneAssignment assignment,
        out float3 targetPosition)
    {
        targetPosition = default;
        Entity targetEntity;

        switch (state)
        {
            case DroneStateEnum.MovingToPickup:
                targetEntity = assignment.sourceOwner;
                break;
            case DroneStateEnum.MovingToDelivery:
                targetEntity = assignment.destinationOwner;
                break;
            case DroneStateEnum.MovingToRecoveryStorage:
                targetEntity = assignment.destinationOwner;
                break;
            case DroneStateEnum.MovingToDemolition:
                targetEntity = assignment.destinationOwner;
                break;
            case DroneStateEnum.MovingToWorldItem:
                targetEntity = assignment.sourceOwner;
                break;
            case DroneStateEnum.MovingToWorldItemStorage:
                targetEntity = assignment.destinationOwner;
                break;
            case DroneStateEnum.Returning:
            case DroneStateEnum.EmergencyReturning:
                targetEntity = assignment.returnStation;
                break;
            default:
                return false;
        }

        if (targetEntity == Entity.Null)
            return false;

        if (!EntityManager.Exists(targetEntity))
            return false;

        if (!EntityManager.HasComponent<GridPosition>(targetEntity))
            return false;

        int2 targetCell = EntityManager
            .GetComponentData<GridPosition>(targetEntity)
            .gridPosition;
        targetPosition = new float3(targetCell.x, targetCell.y, -0.2f);
        return true;
    }

    private void ArriveAtTarget(
        Entity droneEntity,
        DroneStateEnum currentState,
        float3 targetPosition)
    {
        LocalTransform transform = EntityManager
            .GetComponentData<LocalTransform>(droneEntity);
        transform.Position = targetPosition;
        EntityManager.SetComponentData(droneEntity, transform);
        EntityManager.SetComponentData(
            droneEntity,
            new GridPosition
            {
                gridPosition = targetPosition.ToGridCell()
            });

        DroneStateEnum nextState;

        switch (currentState)
        {
            case DroneStateEnum.MovingToPickup:
                nextState = DroneStateEnum.PickingUp;
                break;
            case DroneStateEnum.MovingToDelivery:
                nextState = DroneStateEnum.Delivering;
                break;
            case DroneStateEnum.MovingToRecoveryStorage:
                nextState = DroneStateEnum.RecoveringCargo;
                break;
            case DroneStateEnum.MovingToDemolition:
                nextState = DroneStateEnum.Demolishing;
                break;
            case DroneStateEnum.MovingToWorldItem:
                nextState = DroneStateEnum.PickingUpWorldItem;
                break;
            case DroneStateEnum.MovingToWorldItemStorage:
                nextState = DroneStateEnum.DeliveringWorldItem;
                break;
            case DroneStateEnum.Returning:
            case DroneStateEnum.EmergencyReturning:
                DroneBattery battery = EntityManager
                    .GetComponentData<DroneBattery>(droneEntity);
                nextState = battery.current >= battery.maximum
                    ? DroneStateEnum.Stored
                    : DroneStateEnum.AwaitingCharge;
                break;
            default:
                return;
        }

        EntityManager.SetComponentData(
            droneEntity,
            new DroneState { value = nextState });
    }

    private static bool IsMovingState(DroneStateEnum state)
    {
        return state == DroneStateEnum.MovingToPickup ||
               state == DroneStateEnum.MovingToDelivery ||
               state == DroneStateEnum.MovingToRecoveryStorage ||
               state == DroneStateEnum.MovingToDemolition ||
               state == DroneStateEnum.MovingToWorldItem ||
               state == DroneStateEnum.MovingToWorldItemStorage ||
               state == DroneStateEnum.Returning ||
               state == DroneStateEnum.EmergencyReturning;
    }
}
