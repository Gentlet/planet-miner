using Unity.Entities;
using Unity.Mathematics;

public static class DroneDispatchBatteryUtility
{
    public static bool CanCompleteRoute(
        EntityManager entityManager,
        DroneStationNetworkSystem networkSystem,
        Entity droneEntity,
        int networkId,
        Entity firstWaypoint,
        Entity secondWaypoint)
    {
        if (!TryGetGridCell(entityManager, droneEntity, out int2 droneCell))
            return false;

        if (!TryGetGridCell(
                entityManager,
                firstWaypoint,
                out int2 firstWaypointCell))
            return false;

        float routeDistance = math.distance(
            new float2(droneCell),
            new float2(firstWaypointCell));
        int2 finalWaypointCell = firstWaypointCell;

        if (secondWaypoint != Entity.Null)
        {
            if (!TryGetGridCell(
                    entityManager,
                    secondWaypoint,
                    out int2 secondWaypointCell))
                return false;

            routeDistance += math.distance(
                new float2(firstWaypointCell),
                new float2(secondWaypointCell));
            finalWaypointCell = secondWaypointCell;
        }

        if (!networkSystem.TryFindNearestStation(
                networkId,
                finalWaypointCell,
                out Entity returnStation))
            return false;

        if (!TryGetGridCell(
                entityManager,
                returnStation,
                out int2 returnStationCell))
            return false;

        routeDistance += math.distance(
            new float2(finalWaypointCell),
            new float2(returnStationCell));
        DroneBattery battery = entityManager
            .GetComponentData<DroneBattery>(droneEntity);
        float requiredBattery = routeDistance * battery.consumptionPerDistance;
        return battery.current >= requiredBattery;
    }

    private static bool TryGetGridCell(
        EntityManager entityManager,
        Entity entity,
        out int2 gridCell)
    {
        gridCell = default;

        if (entity == Entity.Null)
            return false;

        if (!entityManager.Exists(entity))
            return false;

        if (!entityManager.HasComponent<GridPosition>(entity))
            return false;

        gridCell = entityManager.GetComponentData<GridPosition>(entity)
            .gridPosition;
        return true;
    }
}
