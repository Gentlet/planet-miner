using Unity.Collections;
using Unity.Entities;

public static class DroneWorldItemRecoveryTaskUtility
{
    public static bool TryCreateRequest(
        EntityManager entityManager,
        Entity itemEntity,
        int normalPriority)
    {
        if (itemEntity == Entity.Null)
            return false;

        if (!entityManager.Exists(itemEntity))
            return false;

        if (HasRecoveryTask(entityManager, itemEntity))
            return false;

        Entity requestEntity = entityManager.CreateEntity();
        entityManager.AddComponentData(requestEntity,
            new DroneWorldItemRecoveryCreateRequest
            {
                itemEntity = itemEntity,
                normalPriority = normalPriority
            });
        return true;
    }

    private static bool HasRecoveryTask(
        EntityManager entityManager,
        Entity itemEntity)
    {
        EntityQuery taskQuery = entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<DroneWorldItemRecoveryTaskData>());
        using NativeArray<Entity> entities =
            taskQuery.ToEntityArray(Allocator.Temp);
        taskQuery.Dispose();

        for (int i = 0; i < entities.Length; i++)
        {
            DroneWorldItemRecoveryTaskData data = entityManager
                .GetComponentData<DroneWorldItemRecoveryTaskData>(entities[i]);

            if (data.itemEntity != itemEntity)
                continue;

            if (!entityManager.HasComponent<DroneTaskStatus>(entities[i]))
                return true;

            DroneTaskStatus status = entityManager
                .GetComponentData<DroneTaskStatus>(entities[i]);

            if (status.state != DroneTaskStateEnum.Completed &&
                status.state != DroneTaskStateEnum.Cancelled)
                return true;
        }


        EntityQuery requestQuery = entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<DroneWorldItemRecoveryCreateRequest>());
        using NativeArray<Entity> requests =
            requestQuery.ToEntityArray(Allocator.Temp);
        requestQuery.Dispose();

        for (int i = 0; i < requests.Length; i++)
        {
            DroneWorldItemRecoveryCreateRequest request = entityManager
                .GetComponentData<DroneWorldItemRecoveryCreateRequest>(
                    requests[i]);

            if (request.itemEntity == itemEntity)
                return true;
        }

        return false;
    }
}
