using Unity.Collections;
using Unity.Entities;

public partial class DroneTaskReservationSystem
{
    private void ReleaseInvalidReservations()
    {
        if (_reservationQuery.IsEmptyIgnoreFilter)
            return;

        using NativeArray<Entity> reservationEntities =
            _reservationQuery.ToEntityArray(Allocator.Temp);

        for (int i = 0; i < reservationEntities.Length; i++)
        {
            Entity reservationEntity = reservationEntities[i];
            DroneTaskReservation reservation = EntityManager
                .GetComponentData<DroneTaskReservation>(reservationEntity);

            if (IsReservationValid(reservationEntity, reservation))
                continue;

            Entity taskEntity = reservation.taskEntity;
            ReleaseReservation(reservationEntity, reservation);
            UpdateTaskAfterRelease(taskEntity, false);
        }
    }

    private bool IsReservationValid(
        Entity reservationEntity,
        DroneTaskReservation reservation)
    {
        if (reservation.taskEntity == Entity.Null)
            return false;

        if (!EntityManager.Exists(reservation.taskEntity))
            return false;

        if (!EntityManager.HasComponent<DroneTaskStatus>(
                reservation.taskEntity))
            return false;

        if (!EntityManager.HasComponent<DroneTaskQuantity>(
                reservation.taskEntity))
            return false;

        DroneTaskStatus taskStatus = EntityManager
            .GetComponentData<DroneTaskStatus>(reservation.taskEntity);

        if (taskStatus.state == DroneTaskStateEnum.Completed)
            return false;

        if (taskStatus.state == DroneTaskStateEnum.Cancelled)
            return false;

        if (!ValidateReservationDestination(reservation))
            return false;

        DynamicBuffer<DroneTaskReservedItemElement> reservedItems =
            EntityManager.GetBuffer<DroneTaskReservedItemElement>(
                reservationEntity,
                true);

        if (reservedItems.Length != reservation.quantity)
            return false;

        for (int i = 0; i < reservedItems.Length; i++)
        {
            if (!ValidateReservedItem(
                    reservationEntity,
                    reservation,
                    reservedItems[i]))
                return false;
        }

        DroneTaskQuantity taskQuantity = EntityManager
            .GetComponentData<DroneTaskQuantity>(reservation.taskEntity);
        return taskQuantity.reservedQuantity >= reservation.quantity;
    }

    private bool ValidateReservationDestination(
        DroneTaskReservation reservation)
    {
        if (reservation.destinationOwner == Entity.Null)
            return false;

        if (!EntityManager.Exists(reservation.destinationOwner))
            return false;

        if (!IsDestinationItemAccepted(
                reservation.destinationOwner,
                reservation.itemType))
            return false;

        if (!EntityManager.HasBuffer<DroneReservedStorageCapacityElement>(
                reservation.destinationOwner))
            return false;

        DynamicBuffer<DroneReservedStorageCapacityElement> reservedCapacity =
            EntityManager.GetBuffer<DroneReservedStorageCapacityElement>(
                reservation.destinationOwner,
                true);

        for (int i = 0; i < reservedCapacity.Length; i++)
        {
            if (reservedCapacity[i].itemType != reservation.itemType)
                continue;

            return reservedCapacity[i].quantity >= reservation.quantity;
        }

        return false;
    }

    private bool ValidateReservedItem(
        Entity reservationEntity,
        DroneTaskReservation reservation,
        DroneTaskReservedItemElement reservedItem)
    {
        if (reservedItem.itemEntity == Entity.Null)
            return false;

        if (!EntityManager.Exists(reservedItem.itemEntity))
            return false;

        if (!EntityManager.HasComponent<Item>(reservedItem.itemEntity))
            return false;

        if (!EntityManager.HasComponent<StoredItem>(reservedItem.itemEntity))
            return false;

        if (!EntityManager.HasComponent<DroneItemReservation>(
                reservedItem.itemEntity))
            return false;

        Item item = EntityManager.GetComponentData<Item>(
            reservedItem.itemEntity);

        if (item.type != reservation.itemType)
            return false;

        StoredItem storedItem = EntityManager.GetComponentData<StoredItem>(
            reservedItem.itemEntity);

        DroneItemReservation itemReservation = EntityManager
            .GetComponentData<DroneItemReservation>(reservedItem.itemEntity);

        if (itemReservation.reservationEntity != reservationEntity)
            return false;

        if (itemReservation.taskEntity != reservation.taskEntity)
            return false;

        if (storedItem.owner != reservedItem.sourceOwner)
        {
            if (!EntityManager.HasComponent<DroneReservationAssignment>(
                    reservationEntity))
                return false;

            Entity assignedDrone = EntityManager
                .GetComponentData<DroneReservationAssignment>(
                    reservationEntity)
                .droneEntity;

            if (storedItem.owner != assignedDrone)
                return false;

            return SourceContainsItem<StoredItemElement>(
                assignedDrone,
                reservedItem.itemEntity);
        }

        if (reservedItem.sourceKind == DroneTaskItemSourceKind.Stored)
        {
            return SourceContainsItem<StoredItemElement>(
                reservedItem.sourceOwner,
                reservedItem.itemEntity);
        }

        if (reservedItem.sourceKind == DroneTaskItemSourceKind.Produced)
        {
            return SourceContainsItem<ProducedItemElement>(
                reservedItem.sourceOwner,
                reservedItem.itemEntity);
        }

        return false;
    }

    private bool SourceContainsItem<TElement>(
        Entity sourceOwner,
        Entity itemEntity)
        where TElement : unmanaged, IBufferElementData, IItemStorageElement
    {
        if (sourceOwner == Entity.Null)
            return false;

        if (!EntityManager.Exists(sourceOwner))
            return false;

        if (!EntityManager.HasBuffer<TElement>(sourceOwner))
            return false;

        DynamicBuffer<TElement> storedItems = EntityManager
            .GetBuffer<TElement>(sourceOwner, true);

        for (int i = 0; i < storedItems.Length; i++)
        {
            if (storedItems[i].ItemEntity == itemEntity)
                return true;
        }

        return false;
    }
}
