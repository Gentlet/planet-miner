using System;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

public partial class DroneTaskReservationSystem
{
    public bool TryReserveDirectDestinationCapacity(
        Entity destinationOwner,
        ItemTypeEnum itemType,
        int quantity)
    {
        DroneTaskReservationRequest request = new()
        {
            destinationOwner = destinationOwner,
            itemType = itemType,
            quantity = quantity
        };

        if (!CanReserveDestinationCapacity(request))
            return false;

        AddDestinationReservedCapacity(
            destinationOwner,
            itemType,
            quantity);
        return true;
    }

    public void ReleaseDirectDestinationCapacity(
        Entity destinationOwner,
        ItemTypeEnum itemType,
        int quantity)
    {
        RemoveDestinationReservedCapacity(
            destinationOwner,
            itemType,
            quantity);
    }

    private bool CanReserveDestinationCapacity(
        DroneTaskReservationRequest request)
    {
        if (_storageLimitQuery.IsEmptyIgnoreFilter)
        {
            Debug.LogError(
                "Cannot reserve drone destination capacity because storage limits are unavailable.");
            return false;
        }

        Entity storageLimitEntity = _storageLimitQuery.GetSingletonEntity();
        using NativeArray<ItemStorageLimitElement> storageLimits =
            DynamicBufferCopyUtility.CreateNativeCopy(
                EntityManager.GetBuffer<ItemStorageLimitElement>(
                    storageLimitEntity,
                    true),
                Allocator.Temp);
        using NativeArray<CrafterRecipeElement> recipes =
            CreateRecipeCopy();
        using NativeArray<CrafterRecipeIngredientElement> ingredients =
            CreateIngredientCopy();

        bool canReserve =
            DroneItemDestinationUtility.CanReserveAdditionalQuantity(
            EntityManager,
            request.destinationOwner,
            request.itemType,
            request.quantity,
            storageLimits,
            recipes,
            ingredients);

        if (!canReserve)
        {
            Debug.LogWarning(
                $"Drone reservation destination has insufficient capacity. " +
                $"Destination : {request.destinationOwner}, Item : {request.itemType}, " +
                $"Quantity : {request.quantity}");
        }

        return canReserve;
    }

    private NativeArray<CrafterRecipeElement> CreateRecipeCopy()
    {
        if (_crafterRecipeQuery.IsEmptyIgnoreFilter)
            return default;

        Entity recipeEntity = _crafterRecipeQuery.GetSingletonEntity();
        return DynamicBufferCopyUtility.CreateNativeCopy(
            EntityManager.GetBuffer<CrafterRecipeElement>(
                recipeEntity,
                true),
            Allocator.Temp);
    }

    private NativeArray<CrafterRecipeIngredientElement> CreateIngredientCopy()
    {
        if (_crafterIngredientQuery.IsEmptyIgnoreFilter)
            return default;

        Entity ingredientEntity = _crafterIngredientQuery
            .GetSingletonEntity();
        return DynamicBufferCopyUtility.CreateNativeCopy(
            EntityManager.GetBuffer<CrafterRecipeIngredientElement>(
                ingredientEntity,
                true),
            Allocator.Temp);
    }

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

    private void AddDestinationReservedCapacity(
        Entity destinationOwner,
        ItemTypeEnum itemType,
        int quantity)
    {
        if (!EntityManager.HasBuffer<DroneReservedStorageCapacityElement>(
                destinationOwner))
        {
            EntityManager.AddBuffer<DroneReservedStorageCapacityElement>(
                destinationOwner);
        }

        DynamicBuffer<DroneReservedStorageCapacityElement> reservedCapacity =
            EntityManager.GetBuffer<DroneReservedStorageCapacityElement>(
                destinationOwner);

        for (int i = 0; i < reservedCapacity.Length; i++)
        {
            DroneReservedStorageCapacityElement reservation =
                reservedCapacity[i];

            if (reservation.itemType != itemType)
                continue;

            reservation.quantity += quantity;
            reservedCapacity[i] = reservation;
            return;
        }

        reservedCapacity.Add(new DroneReservedStorageCapacityElement
        {
            itemType = itemType,
            quantity = quantity
        });
    }

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
        Debug.Log(
            $"Released drone task reservation. Entity : {reservationEntity}, " +
            $"Task : {reservation.taskEntity}");
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

    private void RemoveDestinationReservedCapacity(
        Entity destinationOwner,
        ItemTypeEnum itemType,
        int quantity)
    {
        if (destinationOwner == Entity.Null)
            return;

        if (!EntityManager.Exists(destinationOwner))
            return;

        if (!EntityManager.HasBuffer<DroneReservedStorageCapacityElement>(
                destinationOwner))
            return;

        DynamicBuffer<DroneReservedStorageCapacityElement> reservedCapacity =
            EntityManager.GetBuffer<DroneReservedStorageCapacityElement>(
                destinationOwner);

        for (int i = 0; i < reservedCapacity.Length; i++)
        {
            DroneReservedStorageCapacityElement reservation =
                reservedCapacity[i];

            if (reservation.itemType != itemType)
                continue;

            reservation.quantity -= quantity;

            if (reservation.quantity > 0)
            {
                reservedCapacity[i] = reservation;
            }
            else
            {
                reservedCapacity.RemoveAt(i);
            }

            return;
        }
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
