using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

public partial class DroneBuildingItemTaskPlanningSystem
{
    private ReservationPlanResult PlanInsertion(
        Entity taskEntity,
        DroneBuildingItemTaskData taskData,
        int unreservedQuantity,
        int carryingCapacity,
        int networkId,
        NativeArray<Entity> storageEntities,
        NativeArray<ItemStorageLimitElement> storageLimits,
        NativeArray<CrafterRecipeElement> recipes,
        NativeArray<CrafterRecipeIngredientElement> ingredients)
    {
        int maximumQuantity = math.min(
            unreservedQuantity,
            carryingCapacity);
        maximumQuantity = FindReservableQuantity(
            taskData.targetBuilding,
            taskData.itemType,
            maximumQuantity,
            storageLimits,
            recipes,
            ingredients);

        if (maximumQuantity <= 0)
            return ReservationPlanResult.DestinationCapacityUnavailable;

        if (!TryFindNearestItemSource(
                taskData.targetBuilding,
                taskData.itemType,
                maximumQuantity,
                networkId,
                storageEntities,
                out Entity sourceOwner,
                out int availableQuantity))
            return ReservationPlanResult.MissingSourceItems;

        CreateReservationRequest(
            taskEntity,
            sourceOwner,
            taskData.targetBuilding,
            taskData.itemType,
            math.min(maximumQuantity, availableQuantity));
        return ReservationPlanResult.ReservationCreated;
    }

    private ReservationPlanResult PlanRemoval(
        Entity taskEntity,
        DroneBuildingItemTaskData taskData,
        int unreservedQuantity,
        int carryingCapacity,
        int networkId,
        NativeArray<Entity> storageEntities,
        NativeArray<ItemStorageLimitElement> storageLimits,
        NativeArray<CrafterRecipeElement> recipes,
        NativeArray<CrafterRecipeIngredientElement> ingredients)
    {
        int availableQuantity = CountAvailableItems(
            taskData.targetBuilding,
            taskData.itemType);

        if (!HasSufficientDestinationCapacity(
                taskData.targetBuilding,
                taskData.itemType,
                unreservedQuantity,
                networkId,
                storageEntities,
                storageLimits,
                recipes,
                ingredients))
            return ReservationPlanResult.DestinationCapacityUnavailable;

        int maximumQuantity = math.min(
            math.min(unreservedQuantity, carryingCapacity),
            availableQuantity);

        if (maximumQuantity <= 0)
            return ReservationPlanResult.MissingSourceItems;

        if (!TryFindNearestDestination(
                taskData.targetBuilding,
                taskData.itemType,
                maximumQuantity,
                networkId,
                storageEntities,
                storageLimits,
                recipes,
                ingredients,
                out Entity destinationOwner,
                out int reservableQuantity))
            return ReservationPlanResult.DestinationCapacityUnavailable;

        CreateReservationRequest(
            taskEntity,
            taskData.targetBuilding,
            destinationOwner,
            taskData.itemType,
            reservableQuantity);
        return ReservationPlanResult.ReservationCreated;
    }
}
