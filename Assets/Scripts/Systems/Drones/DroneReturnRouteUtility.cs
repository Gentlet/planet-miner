using Unity.Entities;

public static class DroneReturnRouteUtility
{
    public static void SetAwaitingDispatchAfterCompletion(
        EntityManager entityManager,
        Entity droneEntity,
        Entity completedTaskEntity,
        Entity completedReservationEntity)
    {
        DroneAssignment assignment = entityManager
            .GetComponentData<DroneAssignment>(droneEntity);
        GridPosition position = entityManager
            .GetComponentData<GridPosition>(droneEntity);
        int networkId = assignment.networkId;
        assignment.taskEntity = Entity.Null;
        assignment.reservationEntity = Entity.Null;
        assignment.sourceOwner = Entity.Null;
        assignment.destinationOwner = Entity.Null;
        assignment.emergencyReturn = false;
        entityManager.SetComponentData(droneEntity, assignment);
        entityManager.SetComponentData(
            droneEntity,
            new DroneState { value = DroneStateEnum.AwaitingDispatch });
        Entity completionEvent = entityManager.CreateEntity();
        entityManager.AddComponentData(
            completionEvent,
            new DroneTaskCompletionEvent
            {
                droneEntity = droneEntity,
                completedTaskEntity = completedTaskEntity,
                completedReservationEntity = completedReservationEntity,
                currentCell = position.gridPosition,
                networkId = networkId
            });
    }

    public static bool TrySetReturnRoute(
        EntityManager entityManager,
        DroneStationNetworkSystem networkSystem,
        Entity droneEntity,
        DroneAssignment assignment,
        bool emergencyReturn)
    {
        GridPosition position = entityManager
            .GetComponentData<GridPosition>(droneEntity);
        Entity returnStation;

        if (!networkSystem.TryFindNearestStation(
                assignment.networkId,
                position.gridPosition,
                out returnStation))
        {
            networkSystem.TryFindNearestStation(
                position.gridPosition,
                out returnStation);
        }

        if (returnStation == Entity.Null)
            return false;

        assignment.taskEntity = Entity.Null;
        assignment.reservationEntity = Entity.Null;
        assignment.sourceOwner = Entity.Null;
        assignment.destinationOwner = Entity.Null;
        assignment.returnStation = returnStation;
        assignment.emergencyReturn = emergencyReturn;
        entityManager.SetComponentData(droneEntity, assignment);
        entityManager.SetComponentData(
            droneEntity,
            new DroneState
            {
                value = emergencyReturn
                    ? DroneStateEnum.EmergencyReturning
                    : DroneStateEnum.Returning
            });
        return true;
    }
}
