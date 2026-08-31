using System;
using Unity.Entities;
using UnityEngine;

public partial class DroneTaskReservationSystem
{
    private bool TryCommitReservation(
        DroneTaskReservationRequest request,
        DroneTaskQuantity taskQuantity,
        out Entity reservationEntity)
    {
        reservationEntity = Entity.Null;
        bool destinationCapacityAdded = false;

        try
        {
            reservationEntity = EntityManager.CreateEntity();
            EntityManager.AddComponentData(
                reservationEntity,
                new DroneTaskReservation
                {
                    taskEntity = request.taskEntity,
                    destinationOwner = request.destinationOwner,
                    itemType = request.itemType,
                    quantity = request.quantity
                });
            EntityManager.AddBuffer<DroneTaskReservedItemElement>(
                reservationEntity);

            for (int i = 0; i < _selectedItems.Count; i++)
            {
                ReservedItemCandidate selectedItem = _selectedItems[i];
                EntityManager.AddComponentData(
                    selectedItem.ItemEntity,
                    new DroneItemReservation
                    {
                        reservationEntity = reservationEntity,
                        taskEntity = request.taskEntity
                    });
                DynamicBuffer<DroneTaskReservedItemElement> reservedItems =
                    EntityManager.GetBuffer<DroneTaskReservedItemElement>(
                        reservationEntity);
                reservedItems.Add(new DroneTaskReservedItemElement
                {
                    itemEntity = selectedItem.ItemEntity,
                    sourceOwner = selectedItem.SourceOwner,
                    sourceKind = selectedItem.SourceKind
                });
            }

            destinationCapacityAdded = true;
            AddDestinationReservedCapacity(
                request.destinationOwner,
                request.itemType,
                request.quantity);

            taskQuantity.reservedQuantity += request.quantity;
            EntityManager.SetComponentData(request.taskEntity, taskQuantity);
            _selectedItems.Clear();
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogError(
                $"Failed to commit drone reservation. Task : {request.taskEntity}, " +
                $"Error : {exception}");

            RollbackFailedReservation(
                reservationEntity,
                request.destinationOwner,
                request.itemType,
                request.quantity,
                destinationCapacityAdded);
            reservationEntity = Entity.Null;
            _selectedItems.Clear();
            return false;
        }
    }

    private void ReleaseReservation(
        Entity reservationEntity,
        DroneTaskReservation reservation)
    {
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
        DecreaseTaskReservedQuantity(
            reservation.taskEntity,
            reservation.quantity);

        if (EntityManager.Exists(reservationEntity))
            EntityManager.DestroyEntity(reservationEntity);

        _releaseItems.Clear();
        DroneDiagnostics.LogReservationReleased(
            reservationEntity,
            reservation.taskEntity);
    }

    private void CopyReservedItems(Entity reservationEntity)
    {
        _releaseItems.Clear();

        if (!EntityManager.HasBuffer<DroneTaskReservedItemElement>(
                reservationEntity))
            return;

        DynamicBuffer<DroneTaskReservedItemElement> reservedItems =
            EntityManager.GetBuffer<DroneTaskReservedItemElement>(
                reservationEntity,
                true);

        for (int i = 0; i < reservedItems.Length; i++)
            _releaseItems.Add(reservedItems[i]);
    }

    private void DecreaseTaskReservedQuantity(
        Entity taskEntity,
        int quantity)
    {
        if (taskEntity == Entity.Null)
            return;

        if (!EntityManager.Exists(taskEntity))
            return;

        if (!EntityManager.HasComponent<DroneTaskQuantity>(taskEntity))
            return;

        DroneTaskQuantity taskQuantity = EntityManager
            .GetComponentData<DroneTaskQuantity>(taskEntity);
        taskQuantity.reservedQuantity = Math.Max(
            0,
            taskQuantity.reservedQuantity - quantity);
        EntityManager.SetComponentData(taskEntity, taskQuantity);
    }

    private void RollbackFailedReservation(
        Entity reservationEntity,
        Entity destinationOwner,
        ItemTypeEnum itemType,
        int quantity,
        bool destinationCapacityAdded)
    {
        if (reservationEntity != Entity.Null)
        {
            if (EntityManager.Exists(reservationEntity))
            {
                for (int i = 0; i < _selectedItems.Count; i++)
                {
                    RemoveItemReservationMarker(
                        _selectedItems[i].ItemEntity,
                        reservationEntity);
                }

                EntityManager.DestroyEntity(reservationEntity);
            }
        }

        if (destinationCapacityAdded)
        {
            RemoveDestinationReservedCapacity(
                destinationOwner,
                itemType,
                quantity);
        }

        _releaseItems.Clear();
    }

    private void RemoveItemReservationMarker(
        Entity itemEntity,
        Entity reservationEntity)
    {
        if (itemEntity == Entity.Null)
            return;

        if (!EntityManager.Exists(itemEntity))
            return;

        if (!EntityManager.HasComponent<DroneItemReservation>(itemEntity))
            return;

        DroneItemReservation itemReservation = EntityManager
            .GetComponentData<DroneItemReservation>(itemEntity);

        if (itemReservation.reservationEntity != reservationEntity)
            return;

        EntityManager.RemoveComponent<DroneItemReservation>(itemEntity);
    }
}
