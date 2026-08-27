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
}
