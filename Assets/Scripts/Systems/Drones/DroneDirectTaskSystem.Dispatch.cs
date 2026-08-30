using Unity.Entities;

public partial class DroneDirectTaskSystem
{
    public bool TryClaimScheduledTask(
        Entity droneEntity,
        Entity taskEntity,
        int networkId,
        Entity returnStation)
    {
        if (!EntityManager.Exists(taskEntity))
            return false;

        if (!EntityManager.HasComponent<DroneDirectTaskPlan>(taskEntity))
            return false;

        DroneTaskStatus status = EntityManager
            .GetComponentData<DroneTaskStatus>(taskEntity);

        if (status.state != DroneTaskStateEnum.Pending)
            return false;

        DroneDirectTaskPlan plan = EntityManager
            .GetComponentData<DroneDirectTaskPlan>(taskEntity);

        if (plan.networkId != networkId)
            return false;

        DroneTask task = EntityManager.GetComponentData<DroneTask>(taskEntity);

        if (task.type == DroneTaskTypeEnum.Demolition)
        {
            Entity target = EntityManager
                .GetComponentData<DroneDemolitionTaskData>(taskEntity)
                .targetBuilding;

            if (!DroneDispatchBatteryUtility.CanCompleteRoute(
                    EntityManager,
                    _networkSystem,
                    droneEntity,
                    networkId,
                    target,
                    Entity.Null))
                return false;

            bool claimed = TryClaimDemolitionTask(
                droneEntity,
                taskEntity,
                networkId,
                returnStation);

            if (!claimed)
                RemoveScheduledPlan(taskEntity);

            return claimed;
        }

        if (task.type != DroneTaskTypeEnum.RecoverWorldItem)
            return false;

        Entity itemEntity = EntityManager
            .GetComponentData<DroneWorldItemRecoveryTaskData>(taskEntity)
            .itemEntity;

        if (!DroneDispatchBatteryUtility.CanCompleteRoute(
                EntityManager,
                _networkSystem,
                droneEntity,
                networkId,
                itemEntity,
                plan.destinationOwner))
            return false;

        bool worldItemClaimed = TryClaimWorldItemTask(
            droneEntity,
            taskEntity,
            networkId,
            returnStation,
            plan.destinationOwner);

        if (!worldItemClaimed)
            RemoveScheduledPlan(taskEntity);

        return worldItemClaimed;
    }

    private void RemoveScheduledPlan(Entity taskEntity)
    {
        if (!EntityManager.Exists(taskEntity))
            return;

        if (!EntityManager.HasComponent<DroneDirectTaskPlan>(taskEntity))
            return;

        EntityManager.RemoveComponent<DroneDirectTaskPlan>(taskEntity);
    }

    private bool TryClaimDemolitionTask(
        Entity droneEntity,
        Entity taskEntity,
        int networkId,
        Entity returnStation)
    {
        Entity target = EntityManager
            .GetComponentData<DroneDemolitionTaskData>(taskEntity)
            .targetBuilding;

        if (!IsValidDemolitionTarget(target))
            return false;

        if (!TryReleaseDroneForDispatch(droneEntity))
            return false;

        SetTaskAssigned(
            taskEntity,
            droneEntity,
            Entity.Null,
            ItemTypeEnum.None,
            false);
        SetDroneAssigned(
            droneEntity,
            taskEntity,
            Entity.Null,
            target,
            returnStation,
            networkId,
            DroneStateEnum.MovingToDemolition);
        return true;
    }

    private bool TryClaimWorldItemTask(
        Entity droneEntity,
        Entity taskEntity,
        int networkId,
        Entity returnStation,
        Entity plannedDestinationOwner = default)
    {
        Entity itemEntity = EntityManager
            .GetComponentData<DroneWorldItemRecoveryTaskData>(taskEntity)
            .itemEntity;

        if (!TryGetWorldItem(itemEntity, out Item item, out GridPosition position))
            return false;

        Entity destinationOwner = plannedDestinationOwner;

        if (destinationOwner == Entity.Null)
        {
            if (!TryFindRecoveryDestination(
                    position.gridPosition,
                    networkId,
                    item.type,
                    out destinationOwner))
                return false;
        }

        if (!_reservationSystem.TryReserveDirectDestinationCapacity(
                destinationOwner,
                item.type,
                1))
            return false;

        if (!TryReleaseDroneForDispatch(droneEntity))
        {
            _reservationSystem.ReleaseDirectDestinationCapacity(
                destinationOwner,
                item.type,
                1);
            return false;
        }

        SetTaskAssigned(
            taskEntity,
            droneEntity,
            destinationOwner,
            item.type,
            true);
        SetDroneAssigned(
            droneEntity,
            taskEntity,
            itemEntity,
            destinationOwner,
            returnStation,
            networkId,
            DroneStateEnum.MovingToWorldItem);
        return true;
    }

    private bool TryReleaseDroneForDispatch(Entity droneEntity)
    {
        DroneState state = EntityManager
            .GetComponentData<DroneState>(droneEntity);

        if (state.value == DroneStateEnum.AwaitingDispatch)
            return true;

        return DroneStationStorageUtility.TryReleaseStoredDrone(
            EntityManager,
            droneEntity);
    }

    private void SetTaskAssigned(
        Entity taskEntity,
        Entity droneEntity,
        Entity destinationOwner,
        ItemTypeEnum itemType,
        bool hasReservedCapacity)
    {
        DroneTaskStatus status = EntityManager
            .GetComponentData<DroneTaskStatus>(taskEntity);
        status.state = DroneTaskStateEnum.InProgress;
        EntityManager.SetComponentData(taskEntity, status);
        EntityManager.AddComponentData(taskEntity, new DroneDirectTaskAssignment
        {
            droneEntity = droneEntity,
            destinationOwner = destinationOwner,
            itemType = itemType,
            hasReservedDestinationCapacity = hasReservedCapacity
        });
    }

    private void SetDroneAssigned(
        Entity droneEntity,
        Entity taskEntity,
        Entity sourceOwner,
        Entity destinationOwner,
        Entity returnStation,
        int networkId,
        DroneStateEnum state)
    {
        EntityManager.SetComponentData(droneEntity, new DroneAssignment
        {
            taskEntity = taskEntity,
            reservationEntity = Entity.Null,
            sourceOwner = sourceOwner,
            destinationOwner = destinationOwner,
            returnStation = returnStation,
            networkId = networkId
        });
        EntityManager.SetComponentData(
            droneEntity,
            new DroneState { value = state });
    }
}
