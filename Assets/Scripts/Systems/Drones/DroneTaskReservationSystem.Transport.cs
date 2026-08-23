using Unity.Entities;

public partial class DroneTaskReservationSystem
{
    public bool TryClaimReservation(
        Entity reservationEntity,
        Entity droneEntity)
    {
        if (reservationEntity == Entity.Null)
            return false;

        if (droneEntity == Entity.Null)
            return false;

        if (!EntityManager.Exists(reservationEntity))
            return false;

        if (!EntityManager.Exists(droneEntity))
            return false;

        if (!EntityManager.HasComponent<DroneTaskReservation>(
                reservationEntity))
            return false;

        if (EntityManager.HasComponent<DroneReservationAssignment>(
                reservationEntity))
            return false;

        DroneTaskReservation reservation = EntityManager
            .GetComponentData<DroneTaskReservation>(reservationEntity);

        if (!IsReservationValid(reservationEntity, reservation))
            return false;

        EntityManager.AddComponentData(
            reservationEntity,
            new DroneReservationAssignment { droneEntity = droneEntity });
        return true;
    }

    public bool TryReleaseReservationForDrone(
        Entity reservationEntity,
        Entity droneEntity,
        bool cancelTask)
    {
        if (!TryGetAssignedReservation(
                reservationEntity,
                droneEntity,
                out DroneTaskReservation reservation))
            return false;

        Entity taskEntity = reservation.taskEntity;
        ReleaseReservation(reservationEntity, reservation);
        UpdateTaskAfterRelease(taskEntity, cancelTask);
        return true;
    }

    public bool TryCompleteReservationForDrone(
        Entity reservationEntity,
        Entity droneEntity)
    {
        if (!TryGetAssignedReservation(
                reservationEntity,
                droneEntity,
                out DroneTaskReservation reservation))
            return false;

        if (!AreReservedItemsAtDestination(
                reservationEntity,
                reservation))
            return false;

        CopyReservedItems(reservationEntity);

        for (int i = 0; i < _releaseItems.Count; i++)
        {
            RemoveItemReservationMarker(
                _releaseItems[i].itemEntity,
                reservationEntity);
        }

        RemoveDestinationReservedCapacity(
            reservation.destinationOwner,
            reservation.itemType,
            reservation.quantity);
        CompleteTaskQuantity(
            reservation.taskEntity,
            reservation.quantity);
        EntityManager.DestroyEntity(reservationEntity);
        _releaseItems.Clear();
        return true;
    }

    public void CancelTaskAfterLostReservation(Entity taskEntity)
    {
        UpdateTaskAfterRelease(taskEntity, true);
    }

    private bool TryGetAssignedReservation(
        Entity reservationEntity,
        Entity droneEntity,
        out DroneTaskReservation reservation)
    {
        reservation = default;

        if (reservationEntity == Entity.Null)
            return false;

        if (droneEntity == Entity.Null)
            return false;

        if (!EntityManager.Exists(reservationEntity))
            return false;

        if (!EntityManager.HasComponent<DroneTaskReservation>(
                reservationEntity))
            return false;

        if (!EntityManager.HasComponent<DroneReservationAssignment>(
                reservationEntity))
            return false;

        DroneReservationAssignment assignment = EntityManager
            .GetComponentData<DroneReservationAssignment>(reservationEntity);

        if (assignment.droneEntity != droneEntity)
            return false;

        reservation = EntityManager
            .GetComponentData<DroneTaskReservation>(reservationEntity);
        return true;
    }

    private bool AreReservedItemsAtDestination(
        Entity reservationEntity,
        DroneTaskReservation reservation)
    {
        if (!EntityManager.Exists(reservation.destinationOwner))
            return false;

        if (!EntityManager.HasBuffer<StoredItemElement>(
                reservation.destinationOwner))
            return false;

        DynamicBuffer<DroneTaskReservedItemElement> reservedItems =
            EntityManager.GetBuffer<DroneTaskReservedItemElement>(
                reservationEntity,
                true);

        if (reservedItems.Length != reservation.quantity)
            return false;

        for (int i = 0; i < reservedItems.Length; i++)
        {
            Entity itemEntity = reservedItems[i].itemEntity;

            if (!EntityManager.Exists(itemEntity))
                return false;

            if (!EntityManager.HasComponent<StoredItem>(itemEntity))
                return false;

            if (!EntityManager.HasComponent<DroneItemReservation>(itemEntity))
                return false;

            StoredItem storedItem = EntityManager
                .GetComponentData<StoredItem>(itemEntity);

            if (storedItem.owner != reservation.destinationOwner)
                return false;

            if (!SourceContainsItem<StoredItemElement>(
                    reservation.destinationOwner,
                    itemEntity))
                return false;

            DroneItemReservation itemReservation = EntityManager
                .GetComponentData<DroneItemReservation>(itemEntity);

            if (itemReservation.reservationEntity != reservationEntity)
                return false;
        }

        return true;
    }

    private void CompleteTaskQuantity(Entity taskEntity, int quantity)
    {
        if (!EntityManager.Exists(taskEntity))
            return;

        DroneTaskQuantity taskQuantity = EntityManager
            .GetComponentData<DroneTaskQuantity>(taskEntity);
        taskQuantity.reservedQuantity = Unity.Mathematics.math.max(
            0,
            taskQuantity.reservedQuantity - quantity);
        taskQuantity.deliveredQuantity = Unity.Mathematics.math.min(
            taskQuantity.totalQuantity,
            taskQuantity.deliveredQuantity + quantity);
        EntityManager.SetComponentData(taskEntity, taskQuantity);

        DroneTaskStatus status = EntityManager
            .GetComponentData<DroneTaskStatus>(taskEntity);
        status.state = taskQuantity.deliveredQuantity >=
                       taskQuantity.totalQuantity
            ? DroneTaskStateEnum.Completed
            : DroneTaskStateEnum.Pending;
        EntityManager.SetComponentData(taskEntity, status);
    }

    private void UpdateTaskAfterRelease(Entity taskEntity, bool cancelTask)
    {
        if (!EntityManager.Exists(taskEntity))
            return;

        if (!EntityManager.HasComponent<DroneTaskStatus>(taskEntity))
            return;

        DroneTaskStatus status = EntityManager
            .GetComponentData<DroneTaskStatus>(taskEntity);

        if (status.state == DroneTaskStateEnum.Completed)
            return;

        if (status.state == DroneTaskStateEnum.Cancelled)
            return;

        status.state = cancelTask
            ? DroneTaskStateEnum.Cancelled
            : DroneTaskStateEnum.Pending;
        EntityManager.SetComponentData(taskEntity, status);
    }
}
