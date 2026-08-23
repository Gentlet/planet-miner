using Unity.Entities;

public static class DroneReturnRouteUtility
{
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
