using Unity.Entities;
using Unity.Mathematics;

public partial class DroneDirectTaskSystem
{
    private void UpdateAssignedTask(Entity taskEntity)
    {
        DroneDirectTaskAssignment directAssignment = EntityManager
            .GetComponentData<DroneDirectTaskAssignment>(taskEntity);

        if (!EntityManager.Exists(directAssignment.droneEntity))
        {
            ReleaseAssignment(taskEntity, directAssignment, false);
            return;
        }

        Entity droneEntity = directAssignment.droneEntity;

        if (EntityManager.HasComponent<DroneRecoveryRequest>(droneEntity))
        {
            ReleaseAssignment(taskEntity, directAssignment, false);
            return;
        }

        DroneState state = EntityManager.GetComponentData<DroneState>(droneEntity);

        if (state.value == DroneStateEnum.Demolishing)
            CompleteDemolition(taskEntity, droneEntity, directAssignment);
        else if (state.value == DroneStateEnum.PickingUpWorldItem)
            PickUpWorldItem(taskEntity, droneEntity, directAssignment);
        else if (state.value == DroneStateEnum.DeliveringWorldItem)
            DeliverWorldItem(taskEntity, droneEntity, directAssignment);
    }

    private void CompleteDemolition(
        Entity taskEntity,
        Entity droneEntity,
        DroneDirectTaskAssignment directAssignment)
    {
        Entity target = EntityManager
            .GetComponentData<DroneDemolitionTaskData>(taskEntity)
            .targetBuilding;

        if (!IsValidDemolitionTarget(target))
        {
            ReleaseAssignment(taskEntity, directAssignment, true);
            RequestDroneRecovery(droneEntity);
            return;
        }

        int2 gridPosition = EntityManager.GetComponentData<GridPosition>(target)
            .gridPosition;
        Entity requestEntity = EntityManager.CreateEntity();
        EntityManager.AddComponentData(requestEntity, new BuildingDestroyRequest
        {
            gridPosition = gridPosition,
            cause = BuildingDestroyCauseEnum.UserDemolition
        });
        CompleteTask(taskEntity, directAssignment);
        DroneReturnRouteUtility.SetAwaitingDispatchAfterCompletion(
            EntityManager,
            droneEntity,
            taskEntity,
            Entity.Null);
    }

    private void PickUpWorldItem(
        Entity taskEntity,
        Entity droneEntity,
        DroneDirectTaskAssignment directAssignment)
    {
        Entity itemEntity = EntityManager
            .GetComponentData<DroneWorldItemRecoveryTaskData>(taskEntity)
            .itemEntity;

        if (!TryGetWorldItem(itemEntity, out Item item, out GridPosition position))
        {
            ReleaseAssignment(taskEntity, directAssignment, true);
            RequestDroneRecovery(droneEntity);
            return;
        }

        if (!_itemStorage.TryStoreItemImmediate(
                droneEntity,
                position.gridPosition,
                itemEntity))
        {
            ReleaseAssignment(taskEntity, directAssignment, false);
            RequestDroneRecovery(droneEntity);
            return;
        }

        EntityManager.SetComponentData(droneEntity, new DroneCargo
        {
            itemType = item.type,
            quantity = 1
        });
        EntityManager.SetComponentData(
            droneEntity,
            new DroneState { value = DroneStateEnum.MovingToWorldItemStorage });
    }

    private void DeliverWorldItem(
        Entity taskEntity,
        Entity droneEntity,
        DroneDirectTaskAssignment directAssignment)
    {
        if (!EntityManager.Exists(directAssignment.destinationOwner))
        {
            ReleaseAssignment(taskEntity, directAssignment, true);
            RequestDroneRecovery(droneEntity);
            return;
        }

        if (!_itemStorage.TryTransferOwnedItemImmediate<StoredItemElement>(
                droneEntity,
                0,
                directAssignment.destinationOwner))
        {
            ReleaseAssignment(taskEntity, directAssignment, true);
            RequestDroneRecovery(droneEntity);
            return;
        }

        EntityManager.SetComponentData(
            droneEntity,
            new DroneCargo { itemType = ItemTypeEnum.None });
        CompleteTask(taskEntity, directAssignment);
        DroneReturnRouteUtility.SetAwaitingDispatchAfterCompletion(
            EntityManager,
            droneEntity,
            taskEntity,
            Entity.Null);
    }

    private void CompleteTask(
        Entity taskEntity,
        DroneDirectTaskAssignment directAssignment)
    {
        ReleaseReservedCapacity(directAssignment);
        DroneTaskQuantity quantity = EntityManager
            .GetComponentData<DroneTaskQuantity>(taskEntity);
        quantity.deliveredQuantity = quantity.totalQuantity;
        EntityManager.SetComponentData(taskEntity, quantity);
        DroneTaskStatus status = EntityManager
            .GetComponentData<DroneTaskStatus>(taskEntity);
        status.state = DroneTaskStateEnum.Completed;
        EntityManager.SetComponentData(taskEntity, status);
        EntityManager.RemoveComponent<DroneDirectTaskAssignment>(taskEntity);
    }

    private void ReleaseAssignment(
        Entity taskEntity,
        DroneDirectTaskAssignment directAssignment,
        bool cancelTask)
    {
        ReleaseReservedCapacity(directAssignment);
        DroneTaskStatus status = EntityManager
            .GetComponentData<DroneTaskStatus>(taskEntity);
        status.state = cancelTask
            ? DroneTaskStateEnum.Cancelled
            : DroneTaskStateEnum.Pending;
        EntityManager.SetComponentData(taskEntity, status);
        EntityManager.RemoveComponent<DroneDirectTaskAssignment>(taskEntity);

        if (!EntityManager.Exists(directAssignment.droneEntity))
            return;

        DroneAssignment droneAssignment = EntityManager
            .GetComponentData<DroneAssignment>(directAssignment.droneEntity);

        if (droneAssignment.taskEntity != taskEntity)
            return;

        droneAssignment.taskEntity = Entity.Null;
        droneAssignment.reservationEntity = Entity.Null;
        droneAssignment.sourceOwner = Entity.Null;
        EntityManager.SetComponentData(
            directAssignment.droneEntity,
            droneAssignment);
    }

    private void ReleaseReservedCapacity(
        DroneDirectTaskAssignment directAssignment)
    {
        if (!directAssignment.hasReservedDestinationCapacity)
            return;

        _reservationSystem.ReleaseDirectDestinationCapacity(
            directAssignment.destinationOwner,
            directAssignment.itemType,
            1);
    }

    private void ReturnDrone(Entity droneEntity)
    {
        DroneAssignment assignment = EntityManager
            .GetComponentData<DroneAssignment>(droneEntity);

        if (DroneReturnRouteUtility.TrySetReturnRoute(
                EntityManager,
                _networkSystem,
                droneEntity,
                assignment,
                false))
            return;

        RequestDroneRecovery(droneEntity);
    }

    private void RequestDroneRecovery(Entity droneEntity)
    {
        DroneRecoveryRequestUtility.Request(
            EntityManager,
            droneEntity,
            DroneRecoveryReasonEnum.TargetUnavailable);
    }
}
