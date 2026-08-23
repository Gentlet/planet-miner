using Unity.Collections;
using Unity.Entities;

[UpdateAfter(typeof(DroneTaskReservationSystem))]
[UpdateAfter(typeof(DroneStationNetworkSystem))]
public partial class DroneDispatchSystem : SystemBase
{
    private EntityQuery _droneQuery;
    private EntityQuery _reservationQuery;
    private DroneTaskReservationSystem _reservationSystem;
    private DroneStationNetworkSystem _networkSystem;
    private DroneDirectTaskSystem _directTaskSystem;

    protected override void OnCreate()
    {
        _droneQuery = GetEntityQuery(
            ComponentType.ReadOnly<ActiveDrone>(),
            ComponentType.ReadOnly<DroneBattery>(),
            ComponentType.ReadOnly<DroneState>(),
            ComponentType.ReadOnly<DroneAssignment>(),
            ComponentType.ReadOnly<DroneCargo>(),
            ComponentType.ReadOnly<GridPosition>(),
            ComponentType.ReadOnly<StoredItemElement>());
        _reservationQuery = GetEntityQuery(
            ComponentType.ReadOnly<DroneTaskReservation>(),
            ComponentType.ReadOnly<DroneTaskReservedItemElement>());
        _reservationSystem = World.GetOrCreateSystemManaged<
            DroneTaskReservationSystem>();
        _networkSystem = World.GetOrCreateSystemManaged<
            DroneStationNetworkSystem>();
        _directTaskSystem = World.GetOrCreateSystemManaged<
            DroneDirectTaskSystem>();
    }

    protected override void OnUpdate()
    {
        using NativeArray<Entity> droneEntities =
            _droneQuery.ToEntityArray(Allocator.Temp);

        for (int i = 0; i < droneEntities.Length; i++)
            TryDispatchDrone(droneEntities[i]);
    }

    private void TryDispatchDrone(Entity droneEntity)
    {
        DroneState state = EntityManager
            .GetComponentData<DroneState>(droneEntity);

        if (state.value != DroneStateEnum.Stored)
            return;

        DroneBattery battery = EntityManager
            .GetComponentData<DroneBattery>(droneEntity);

        if (battery.current < battery.maximum)
            return;

        DroneCargo cargo = EntityManager
            .GetComponentData<DroneCargo>(droneEntity);

        if (cargo.quantity != 0)
            return;

        DynamicBuffer<StoredItemElement> cargoItems = EntityManager
            .GetBuffer<StoredItemElement>(droneEntity, true);

        if (cargoItems.Length != 0)
            return;

        ActiveDrone drone = EntityManager
            .GetComponentData<ActiveDrone>(droneEntity);
        DroneAssignment currentAssignment = EntityManager
            .GetComponentData<DroneAssignment>(droneEntity);

        if (!TryResolveDroneNetwork(
                droneEntity,
                currentAssignment,
                out int networkId,
                out Entity returnStation))
            return;

        bool hasReservation = TrySelectReservation(
                drone.carryingCapacity,
                networkId,
                out Entity reservationEntity,
                out DroneTaskReservation reservation,
                out Entity sourceOwner,
                out DroneTaskPriority reservationPriority,
                out ulong reservationCreationOrder);

        if (_directTaskSystem.TryDispatchHigherPriorityTask(
                droneEntity,
                networkId,
                returnStation,
                hasReservation,
                reservationPriority,
                reservationCreationOrder))
            return;

        if (!hasReservation)
            return;

        if (!_reservationSystem.TryClaimReservation(
                reservationEntity,
                droneEntity))
            return;

        if (!DroneStationStorageUtility.TryReleaseStoredDrone(
                EntityManager,
                droneEntity))
        {
            _reservationSystem.TryReleaseReservationForDrone(
                reservationEntity,
                droneEntity,
                false);
            return;
        }

        EntityManager.SetComponentData(
            droneEntity,
            new DroneAssignment
            {
                taskEntity = reservation.taskEntity,
                reservationEntity = reservationEntity,
                sourceOwner = sourceOwner,
                destinationOwner = reservation.destinationOwner,
                returnStation = returnStation,
                networkId = networkId
            });
        EntityManager.SetComponentData(
            droneEntity,
            new DroneState { value = DroneStateEnum.MovingToPickup });
        SetTaskInProgress(reservation.taskEntity);
    }

    private bool TryResolveDroneNetwork(
        Entity droneEntity,
        DroneAssignment assignment,
        out int networkId,
        out Entity returnStation)
    {
        networkId = 0;
        returnStation = assignment.returnStation;

        if (returnStation != Entity.Null)
        {
            if (_networkSystem.TryGetNetworkId(
                    returnStation,
                    out networkId))
                return true;
        }

        GridPosition dronePosition = EntityManager
            .GetComponentData<GridPosition>(droneEntity);

        if (!_networkSystem.TryFindNearestStation(
                dronePosition.gridPosition,
                out returnStation))
            return false;

        return _networkSystem.TryGetNetworkId(
            returnStation,
            out networkId);
    }

    private bool TrySelectReservation(
        int carryingCapacity,
        int networkId,
        out Entity reservationEntity,
        out DroneTaskReservation reservation,
        out Entity sourceOwner,
        out DroneTaskPriority selectedPriority,
        out ulong selectedCreationOrder)
    {
        reservationEntity = Entity.Null;
        reservation = default;
        sourceOwner = Entity.Null;
        selectedPriority = default;
        selectedCreationOrder = 0;

        using NativeArray<Entity> reservationEntities =
            _reservationQuery.ToEntityArray(Allocator.Temp);

        for (int i = 0; i < reservationEntities.Length; i++)
        {
            Entity candidateEntity = reservationEntities[i];

            if (!TryGetReservationCandidate(
                    candidateEntity,
                    carryingCapacity,
                    networkId,
                    out DroneTaskReservation candidate,
                    out Entity candidateSource,
                    out DroneTaskPriority candidatePriority,
                    out ulong candidateCreationOrder))
                continue;

            if (reservationEntity != Entity.Null)
            {
                if (!DroneTaskPriorityUtility.IsHigherPriority(
                        candidatePriority,
                        candidateCreationOrder,
                        selectedPriority,
                        selectedCreationOrder))
                    continue;
            }

            reservationEntity = candidateEntity;
            reservation = candidate;
            sourceOwner = candidateSource;
            selectedPriority = candidatePriority;
            selectedCreationOrder = candidateCreationOrder;
        }

        return reservationEntity != Entity.Null;
    }

    private bool TryGetReservationCandidate(
        Entity reservationEntity,
        int carryingCapacity,
        int networkId,
        out DroneTaskReservation reservation,
        out Entity sourceOwner,
        out DroneTaskPriority priority,
        out ulong creationOrder)
    {
        reservation = EntityManager
            .GetComponentData<DroneTaskReservation>(reservationEntity);
        sourceOwner = Entity.Null;
        priority = default;
        creationOrder = 0;

        if (EntityManager.HasComponent<DroneReservationAssignment>(
                reservationEntity))
            return false;

        if (reservation.quantity <= 0)
            return false;

        if (reservation.quantity > carryingCapacity)
            return false;

        if (!TryGetSchedulableTask(
                reservation.taskEntity,
                out priority,
                out creationOrder))
            return false;

        if (!TryGetSingleSourceOwner(
                reservationEntity,
                reservation.quantity,
                out sourceOwner))
            return false;

        if (!IsOwnerInNetwork(sourceOwner, networkId))
            return false;

        return IsOwnerInNetwork(reservation.destinationOwner, networkId);
    }

    private bool TryGetSchedulableTask(
        Entity taskEntity,
        out DroneTaskPriority priority,
        out ulong creationOrder)
    {
        priority = default;
        creationOrder = 0;

        if (!EntityManager.Exists(taskEntity))
            return false;

        DroneTaskStatus status = EntityManager
            .GetComponentData<DroneTaskStatus>(taskEntity);

        if (status.state != DroneTaskStateEnum.Pending &&
            status.state != DroneTaskStateEnum.InProgress)
            return false;

        priority = EntityManager
            .GetComponentData<DroneTaskPriority>(taskEntity);
        creationOrder = EntityManager
            .GetComponentData<DroneTaskCreationOrder>(taskEntity)
            .value;
        return true;
    }

    private bool TryGetSingleSourceOwner(
        Entity reservationEntity,
        int expectedQuantity,
        out Entity sourceOwner)
    {
        sourceOwner = Entity.Null;
        DynamicBuffer<DroneTaskReservedItemElement> reservedItems =
            EntityManager.GetBuffer<DroneTaskReservedItemElement>(
                reservationEntity,
                true);

        if (reservedItems.Length != expectedQuantity)
            return false;

        for (int i = 0; i < reservedItems.Length; i++)
        {
            Entity candidateOwner = reservedItems[i].sourceOwner;

            if (candidateOwner == Entity.Null)
                return false;

            if (sourceOwner == Entity.Null)
            {
                sourceOwner = candidateOwner;
                continue;
            }

            if (sourceOwner != candidateOwner)
                return false;
        }

        return sourceOwner != Entity.Null;
    }

    private bool IsOwnerInNetwork(Entity ownerEntity, int networkId)
    {
        if (!EntityManager.Exists(ownerEntity))
            return false;

        if (!EntityManager.HasComponent<GridPosition>(ownerEntity))
            return false;

        GridPosition position = EntityManager
            .GetComponentData<GridPosition>(ownerEntity);

        if (!_networkSystem.TryGetNetworkIdAtCell(
                position.gridPosition,
                out int ownerNetworkId))
            return false;

        return ownerNetworkId == networkId;
    }

    private void SetTaskInProgress(Entity taskEntity)
    {
        DroneTaskStatus status = EntityManager
            .GetComponentData<DroneTaskStatus>(taskEntity);
        status.state = DroneTaskStateEnum.InProgress;
        EntityManager.SetComponentData(taskEntity, status);
    }
}
