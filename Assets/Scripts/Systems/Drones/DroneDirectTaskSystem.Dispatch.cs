using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

public partial class DroneDirectTaskSystem
{
    public bool TryDispatchHigherPriorityTask(
        Entity droneEntity,
        int networkId,
        Entity returnStation,
        bool hasReservationCandidate,
        DroneTaskPriority reservationPriority,
        ulong reservationCreationOrder)
    {
        if (!TrySelectTask(
                droneEntity,
                networkId,
                hasReservationCandidate,
                reservationPriority,
                reservationCreationOrder,
                out Entity taskEntity))
            return false;

        DroneTask task = EntityManager.GetComponentData<DroneTask>(taskEntity);

        if (task.type == DroneTaskTypeEnum.Demolition)
        {
            return TryClaimDemolitionTask(
                droneEntity,
                taskEntity,
                networkId,
                returnStation);
        }

        if (task.type == DroneTaskTypeEnum.RecoverWorldItem)
        {
            return TryClaimWorldItemTask(
                droneEntity,
                taskEntity,
                networkId,
                returnStation);
        }

        return false;
    }

    private bool TrySelectTask(
        Entity droneEntity,
        int networkId,
        bool hasReservationCandidate,
        DroneTaskPriority reservationPriority,
        ulong reservationCreationOrder,
        out Entity taskEntity)
    {
        taskEntity = Entity.Null;
        DroneTaskPriority selectedPriority = reservationPriority;
        ulong selectedCreationOrder = reservationCreationOrder;
        bool hasSelectedCandidate = hasReservationCandidate;
        using NativeArray<Entity> tasks =
            _taskQuery.ToEntityArray(Allocator.Temp);

        for (int i = 0; i < tasks.Length; i++)
        {
            Entity candidate = tasks[i];
            DroneTaskStatus status = EntityManager
                .GetComponentData<DroneTaskStatus>(candidate);

            if (status.state != DroneTaskStateEnum.Pending)
                continue;

            if (!IsDirectTaskAvailableInNetwork(
                    droneEntity,
                    candidate,
                    networkId))
                continue;

            DroneTaskPriority candidatePriority = EntityManager
                .GetComponentData<DroneTaskPriority>(candidate);
            ulong candidateCreationOrder = EntityManager
                .GetComponentData<DroneTaskCreationOrder>(candidate).value;

            if (hasSelectedCandidate &&
                !DroneTaskPriorityUtility.IsHigherPriority(
                    candidatePriority,
                    candidateCreationOrder,
                    selectedPriority,
                    selectedCreationOrder))
                continue;

            taskEntity = candidate;
            selectedPriority = candidatePriority;
            selectedCreationOrder = candidateCreationOrder;
            hasSelectedCandidate = true;
        }

        return taskEntity != Entity.Null;
    }

    private bool IsDirectTaskAvailableInNetwork(
        Entity droneEntity,
        Entity taskEntity,
        int networkId)
    {
        DroneTask task = EntityManager.GetComponentData<DroneTask>(taskEntity);

        if (task.type == DroneTaskTypeEnum.Demolition)
        {
            return IsDemolitionAvailableInNetwork(
                droneEntity,
                taskEntity,
                networkId);
        }

        if (task.type == DroneTaskTypeEnum.RecoverWorldItem)
        {
            return IsWorldItemAvailableInNetwork(
                droneEntity,
                taskEntity,
                networkId);
        }

        return false;
    }

    private bool IsDemolitionAvailableInNetwork(
        Entity droneEntity,
        Entity taskEntity,
        int networkId)
    {
        if (!EntityManager.HasComponent<DroneDemolitionTaskData>(taskEntity))
            return false;

        Entity target = EntityManager
            .GetComponentData<DroneDemolitionTaskData>(taskEntity)
            .targetBuilding;

        if (!IsValidDemolitionTarget(target))
            return false;

        int2 cell = EntityManager.GetComponentData<GridPosition>(target)
            .gridPosition;
        if (!_networkSystem.TryGetNetworkIdAtCell(
                cell,
                out int targetNetwork))
            return false;

        if (targetNetwork != networkId)
            return false;

        return DroneDispatchBatteryUtility.CanCompleteRoute(
            EntityManager,
            _networkSystem,
            droneEntity,
            networkId,
            target,
            Entity.Null);
    }

    private bool IsWorldItemAvailableInNetwork(
        Entity droneEntity,
        Entity taskEntity,
        int networkId)
    {
        if (!EntityManager.HasComponent<DroneWorldItemRecoveryTaskData>(taskEntity))
            return false;

        Entity itemEntity = EntityManager
            .GetComponentData<DroneWorldItemRecoveryTaskData>(taskEntity)
            .itemEntity;

        if (!TryGetWorldItem(itemEntity, out Item item, out GridPosition position))
            return false;

        if (!_networkSystem.TryGetNetworkIdAtCell(
                position.gridPosition,
                out int itemNetwork))
            return false;

        if (itemNetwork != networkId)
            return false;

        if (!TryFindRecoveryDestination(
                position.gridPosition,
                networkId,
                item.type,
                out Entity destinationOwner))
            return false;

        return DroneDispatchBatteryUtility.CanCompleteRoute(
            EntityManager,
            _networkSystem,
            droneEntity,
            networkId,
            itemEntity,
            destinationOwner);
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

        if (!DroneStationStorageUtility.TryReleaseStoredDrone(
                EntityManager,
                droneEntity))
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
        Entity returnStation)
    {
        Entity itemEntity = EntityManager
            .GetComponentData<DroneWorldItemRecoveryTaskData>(taskEntity)
            .itemEntity;

        if (!TryGetWorldItem(itemEntity, out Item item, out GridPosition position))
            return false;

        if (!TryFindRecoveryDestination(
                position.gridPosition,
                networkId,
                item.type,
                out Entity destinationOwner))
            return false;

        if (!_reservationSystem.TryReserveDirectDestinationCapacity(
                destinationOwner,
                item.type,
                1))
            return false;

        if (!DroneStationStorageUtility.TryReleaseStoredDrone(
                EntityManager,
                droneEntity))
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
