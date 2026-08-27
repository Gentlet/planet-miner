using Unity.Entities;
using Unity.Rendering;

public static class DroneStationStorageUtility
{
    public static int GetStoredDroneCount(
        EntityManager entityManager,
        Entity stationEntity)
    {
        if (stationEntity == Entity.Null)
            return 0;

        if (!entityManager.Exists(stationEntity))
            return 0;

        if (!entityManager.HasBuffer<StoredDroneElement>(stationEntity))
            return 0;

        return entityManager.GetBuffer<StoredDroneElement>(
            stationEntity,
            true).Length;
    }

    public static bool TryReleaseStoredDrone(
        EntityManager entityManager,
        Entity droneEntity)
    {
        if (!entityManager.HasComponent<StoredDrone>(droneEntity))
            return true;

        StoredDrone storedDrone = entityManager.GetComponentData<StoredDrone>(
            droneEntity);

        if (!TryRemoveFromStationBuffer(
                entityManager,
                storedDrone.stationEntity,
                droneEntity))
            return false;

        entityManager.RemoveComponent<StoredDrone>(droneEntity);
        EnableDroneRendering(entityManager, droneEntity);
        return true;
    }

    public static bool TryAddStoredDrone(
        EntityManager entityManager,
        Entity stationEntity,
        Entity droneEntity)
    {
        if (stationEntity == Entity.Null)
            return false;

        if (!entityManager.Exists(stationEntity))
            return false;

        if (!entityManager.HasComponent<DroneStation>(stationEntity))
            return false;

        if (!entityManager.HasBuffer<StoredDroneElement>(stationEntity))
            return false;

        if (entityManager.HasComponent<StoredDrone>(droneEntity))
            return false;

        DynamicBuffer<StoredDroneElement> storedDrones = entityManager
            .GetBuffer<StoredDroneElement>(stationEntity);
        storedDrones.Add(new StoredDroneElement { droneEntity = droneEntity });
        entityManager.AddComponentData(
            droneEntity,
            new StoredDrone { stationEntity = stationEntity });
        UpdateDroneRendering(entityManager, droneEntity);
        return true;
    }

    public static bool TryRemoveFromStationBuffer(
        EntityManager entityManager,
        Entity stationEntity,
        Entity droneEntity)
    {
        if (stationEntity == Entity.Null)
            return false;

        if (!entityManager.Exists(stationEntity))
            return false;

        if (!entityManager.HasBuffer<StoredDroneElement>(stationEntity))
            return false;

        DynamicBuffer<StoredDroneElement> storedDrones = entityManager
            .GetBuffer<StoredDroneElement>(stationEntity);

        for (int i = 0; i < storedDrones.Length; i++)
        {
            if (storedDrones[i].droneEntity != droneEntity)
                continue;

            storedDrones.RemoveAt(i);
            return true;
        }

        return false;
    }

    private static void DisableDroneRendering(
        EntityManager entityManager,
        Entity droneEntity)
    {
        if (entityManager.HasComponent<DisableRendering>(droneEntity))
            return;

        entityManager.AddComponent<DisableRendering>(droneEntity);
    }

    public static void UpdateDroneRendering(
        EntityManager entityManager,
        Entity droneEntity)
    {
        DroneState state = entityManager.GetComponentData<DroneState>(
            droneEntity);

        if (state.value == DroneStateEnum.Stored)
        {
            DisableDroneRendering(entityManager, droneEntity);
            return;
        }

        EnableDroneRendering(entityManager, droneEntity);
    }

    private static void EnableDroneRendering(
        EntityManager entityManager,
        Entity droneEntity)
    {
        if (!entityManager.HasComponent<DisableRendering>(droneEntity))
            return;

        entityManager.RemoveComponent<DisableRendering>(droneEntity);
    }
}
