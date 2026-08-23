using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

[UpdateAfter(typeof(DroneTaskCommandSystem))]
[UpdateBefore(typeof(StorageSystem))]
[UpdateBefore(typeof(MiningSystem))]
[UpdateBefore(typeof(CrafterSystem))]
[UpdateBefore(typeof(BuildingDestroySystem))]
public partial class DroneTaskReservationSystem : SystemBase
{
    private EntityQuery _reservationRequestQuery;
    private EntityQuery _reservationQuery;
    private EntityQuery _storedItemOwnerQuery;
    private EntityQuery _producedItemOwnerQuery;
    private EntityQuery _storageLimitQuery;
    private EntityQuery _crafterRecipeQuery;
    private EntityQuery _crafterIngredientQuery;
    private readonly List<ReservedItemCandidate> _selectedItems = new();
    private readonly List<SourceInventoryCandidate> _sourceInventories = new();
    private readonly List<DroneTaskReservedItemElement> _releaseItems = new();

    protected override void OnCreate()
    {
        _reservationRequestQuery = GetEntityQuery(
            ComponentType.ReadOnly<DroneTaskReservationRequest>());
        _reservationQuery = GetEntityQuery(
            ComponentType.ReadOnly<DroneTaskReservation>(),
            ComponentType.ReadOnly<DroneTaskReservedItemElement>());
        _storedItemOwnerQuery = GetEntityQuery(
            ComponentType.ReadOnly<StoredItemElement>());
        _producedItemOwnerQuery = GetEntityQuery(
            ComponentType.ReadOnly<ProducedItemElement>());
        _storageLimitQuery = GetEntityQuery(
            ComponentType.ReadOnly<ItemStorageLimitElement>());
        _crafterRecipeQuery = GetEntityQuery(
            ComponentType.ReadOnly<CrafterRecipeElement>());
        _crafterIngredientQuery = GetEntityQuery(
            ComponentType.ReadOnly<CrafterRecipeIngredientElement>());
    }

    protected override void OnUpdate()
    {
        ReleaseInvalidReservations();
        ProcessReservationRequests();
    }

    private void ProcessReservationRequests()
    {
        if (_reservationRequestQuery.IsEmptyIgnoreFilter)
            return;

        using NativeArray<Entity> requestEntities =
            _reservationRequestQuery.ToEntityArray(Allocator.Temp);

        for (int i = 0; i < requestEntities.Length; i++)
        {
            Entity requestEntity = requestEntities[i];
            DroneTaskReservationRequest request = EntityManager
                .GetComponentData<DroneTaskReservationRequest>(requestEntity);

            if (TryCreateReservation(request, out Entity reservationEntity))
            {
                Debug.Log(
                    $"Created drone task reservation. Entity : {reservationEntity}, " +
                    $"Task : {request.taskEntity}, Item : {request.itemType}, " +
                    $"Quantity : {request.quantity}");
            }

            if (EntityManager.Exists(requestEntity))
                EntityManager.DestroyEntity(requestEntity);
        }
    }

    private bool TryCreateReservation(
        DroneTaskReservationRequest request,
        out Entity reservationEntity)
    {
        reservationEntity = Entity.Null;

        if (!ValidateReservationRequest(
                request,
                out DroneTaskQuantity taskQuantity))
            return false;

        if (!TrySelectSourceItems(request))
            return false;

        if (!CanReserveDestinationCapacity(request))
            return false;

        return TryCommitReservation(
            request,
            taskQuantity,
            out reservationEntity);
    }

    private bool ValidateReservationRequest(
        DroneTaskReservationRequest request,
        out DroneTaskQuantity taskQuantity)
    {
        taskQuantity = default;

        if (request.taskEntity == Entity.Null)
        {
            Debug.LogError("Drone reservation request has no task entity.");
            return false;
        }

        if (!EntityManager.Exists(request.taskEntity))
        {
            Debug.LogError(
                $"Drone reservation task does not exist. Task : {request.taskEntity}");
            return false;
        }

        if (!EntityManager.HasComponent<DroneTask>(request.taskEntity))
        {
            Debug.LogError(
                $"Drone reservation target is not a task. Task : {request.taskEntity}");
            return false;
        }

        if (!EntityManager.HasComponent<DroneTaskStatus>(request.taskEntity))
        {
            Debug.LogError(
                $"Drone reservation task has no status. Task : {request.taskEntity}");
            return false;
        }

        if (!EntityManager.HasComponent<DroneTaskQuantity>(request.taskEntity))
        {
            Debug.LogError(
                $"Drone reservation task has no quantity state. Task : {request.taskEntity}");
            return false;
        }

        DroneTaskStatus taskStatus = EntityManager
            .GetComponentData<DroneTaskStatus>(request.taskEntity);

        if (taskStatus.state != DroneTaskStateEnum.Pending &&
            taskStatus.state != DroneTaskStateEnum.InProgress)
        {
            Debug.LogWarning(
                $"Drone reservation requires a schedulable task. Task : {request.taskEntity}, " +
                $"State : {taskStatus.state}");
            return false;
        }

        if (!request.itemType.IsValid())
        {
            Debug.LogError(
                $"Drone reservation has an invalid item type. Type : {request.itemType}");
            return false;
        }

        if (request.quantity <= 0)
        {
            Debug.LogError(
                $"Drone reservation quantity must be positive. Quantity : {request.quantity}");
            return false;
        }

        if (!TryValidateTaskQuantity(request.taskEntity, out taskQuantity))
            return false;

        if (request.quantity > taskQuantity.UnreservedQuantity)
        {
            Debug.LogWarning(
                $"Drone reservation exceeds the task's unreserved quantity. " +
                $"Task : {request.taskEntity}, Requested : {request.quantity}, " +
                $"Available : {taskQuantity.UnreservedQuantity}");
            return false;
        }

        if (!ValidateDestination(
                request.destinationOwner,
                request.itemType))
            return false;

        if (request.sourceOwner != Entity.Null)
        {
            if (request.sourceOwner == request.destinationOwner)
            {
                Debug.LogWarning(
                    $"Drone reservation source and destination are identical. " +
                    $"Owner : {request.sourceOwner}");
                return false;
            }
        }

        return true;
    }

    private bool TryValidateTaskQuantity(
        Entity taskEntity,
        out DroneTaskQuantity taskQuantity)
    {
        taskQuantity = EntityManager
            .GetComponentData<DroneTaskQuantity>(taskEntity);

        if (taskQuantity.totalQuantity <= 0)
        {
            Debug.LogError(
                $"Drone task has a non-positive total quantity. Task : {taskEntity}");
            return false;
        }

        if (taskQuantity.deliveredQuantity < 0)
        {
            Debug.LogError(
                $"Drone task has a negative delivered quantity. Task : {taskEntity}");
            return false;
        }

        if (taskQuantity.reservedQuantity < 0)
        {
            Debug.LogError(
                $"Drone task has a negative reserved quantity. Task : {taskEntity}");
            return false;
        }

        if (taskQuantity.UnreservedQuantity < 0)
        {
            Debug.LogError(
                $"Drone task quantity invariant is broken. Task : {taskEntity}, " +
                $"Total : {taskQuantity.totalQuantity}, " +
                $"Delivered : {taskQuantity.deliveredQuantity}, " +
                $"Reserved : {taskQuantity.reservedQuantity}");
            return false;
        }

        return true;
    }

    private bool ValidateDestination(
        Entity destinationOwner,
        ItemTypeEnum itemType)
    {
        if (destinationOwner == Entity.Null)
        {
            Debug.LogError("Drone reservation has no destination owner.");
            return false;
        }

        if (!EntityManager.Exists(destinationOwner))
        {
            Debug.LogError(
                $"Drone reservation destination does not exist. Destination : {destinationOwner}");
            return false;
        }

        if (!EntityManager.HasBuffer<StoredItemElement>(destinationOwner))
        {
            Debug.LogError(
                $"Drone reservation destination has no stored-item buffer. Destination : {destinationOwner}");
            return false;
        }

        if (!IsDestinationItemAccepted(destinationOwner, itemType))
        {
            Debug.LogWarning(
                $"Drone reservation destination cannot accept the item. " +
                $"Destination : {destinationOwner}, Item : {itemType}");
            return false;
        }

        return true;
    }

    private bool IsDestinationItemAccepted(
        Entity destinationOwner,
        ItemTypeEnum itemType)
    {
        if (EntityManager.HasComponent<ConstructionSite>(destinationOwner))
        {
            return DroneItemDestinationUtility.CanAcceptItem(
                EntityManager,
                destinationOwner,
                itemType,
                default,
                default);
        }

        if (_crafterRecipeQuery.IsEmptyIgnoreFilter)
            return EntityManager.HasComponent<Storage>(destinationOwner);

        if (_crafterIngredientQuery.IsEmptyIgnoreFilter)
            return EntityManager.HasComponent<Storage>(destinationOwner);

        Entity recipeEntity = _crafterRecipeQuery.GetSingletonEntity();
        using NativeArray<CrafterRecipeElement> recipes =
            DynamicBufferCopyUtility.CreateNativeCopy(
                EntityManager.GetBuffer<CrafterRecipeElement>(
                    recipeEntity,
                    true),
                Allocator.Temp);
        Entity ingredientEntity = _crafterIngredientQuery.GetSingletonEntity();
        using NativeArray<CrafterRecipeIngredientElement> ingredients =
            DynamicBufferCopyUtility.CreateNativeCopy(
                EntityManager.GetBuffer<CrafterRecipeIngredientElement>(
                    ingredientEntity,
                    true),
                Allocator.Temp);
        return DroneItemDestinationUtility.CanAcceptItem(
            EntityManager,
            destinationOwner,
            itemType,
            recipes,
            ingredients);
    }
}
