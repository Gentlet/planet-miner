using Unity.Collections;
using Unity.Entities;

public static class DroneBuildingItemRequestUtility
{
    public static bool TryCreateRequest(
        EntityManager entityManager,
        Entity targetBuilding,
        ItemTypeEnum itemType,
        int quantity,
        DroneTaskTypeEnum taskType)
    {
        if (!ValidateRequest(
                entityManager,
                targetBuilding,
                itemType,
                quantity,
                taskType))
            return false;

        Entity requestEntity = entityManager.CreateEntity(
            typeof(DroneTaskCreateRequest),
            typeof(DroneBuildingItemTaskData));
        entityManager.SetComponentData(
            requestEntity,
            CreateTaskRequest(taskType, quantity));
        entityManager.SetComponentData(
            requestEntity,
            CreateTaskData(targetBuilding, itemType));
        return true;
    }

    public static bool TryCreateUncoveredRemovalRequest(
        EntityManager entityManager,
        ref EntityCommandBuffer ecb,
        Entity targetBuilding,
        ItemTypeEnum itemType,
        int storedQuantity)
    {
        int outstandingQuantity = GetOutstandingRemovalQuantity(
            entityManager,
            targetBuilding,
            itemType);
        int uncoveredQuantity = storedQuantity - outstandingQuantity;

        if (uncoveredQuantity <= 0)
            return false;

        if (!ValidateRequest(
                entityManager,
                targetBuilding,
                itemType,
                uncoveredQuantity,
                DroneTaskTypeEnum.RemoveBuildingItem))
            return false;

        CreateRequest(
            ref ecb,
            targetBuilding,
            itemType,
            uncoveredQuantity,
            DroneTaskTypeEnum.RemoveBuildingItem);
        return true;
    }

    private static bool ValidateRequest(
        EntityManager entityManager,
        Entity targetBuilding,
        ItemTypeEnum itemType,
        int quantity,
        DroneTaskTypeEnum taskType)
    {
        if (targetBuilding == Entity.Null)
            return false;

        if (!entityManager.Exists(targetBuilding))
            return false;

        if (!entityManager.HasBuffer<StoredItemElement>(targetBuilding))
            return false;

        if (!itemType.IsValid())
            return false;

        if (quantity <= 0)
            return false;

        return taskType == DroneTaskTypeEnum.RemoveBuildingItem ||
               taskType == DroneTaskTypeEnum.InsertBuildingItem;
    }

    private static void CreateRequest(
        ref EntityCommandBuffer ecb,
        Entity targetBuilding,
        ItemTypeEnum itemType,
        int quantity,
        DroneTaskTypeEnum taskType)
    {
        Entity requestEntity = ecb.CreateEntity();
        ecb.AddComponent(
            requestEntity,
            CreateTaskRequest(taskType, quantity));
        ecb.AddComponent(
            requestEntity,
            CreateTaskData(targetBuilding, itemType));
    }

    private static DroneTaskCreateRequest CreateTaskRequest(
        DroneTaskTypeEnum taskType,
        int quantity)
    {
        return new DroneTaskCreateRequest
        {
            type = taskType,
            priorityClass = DroneTaskPriorityClassEnum.Normal,
            normalPriority = 0,
            totalQuantity = quantity
        };
    }

    private static DroneBuildingItemTaskData CreateTaskData(
        Entity targetBuilding,
        ItemTypeEnum itemType)
    {
        return new DroneBuildingItemTaskData
        {
            targetBuilding = targetBuilding,
            itemType = itemType
        };
    }

    private static int GetOutstandingRemovalQuantity(
        EntityManager entityManager,
        Entity targetBuilding,
        ItemTypeEnum itemType)
    {
        using EntityQuery query = entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<DroneBuildingItemTaskData>());
        using NativeArray<Entity> entities =
            query.ToEntityArray(Allocator.Temp);
        int outstandingQuantity = 0;

        for (int i = 0; i < entities.Length; i++)
        {
            Entity entity = entities[i];
            DroneBuildingItemTaskData taskData = entityManager
                .GetComponentData<DroneBuildingItemTaskData>(entity);

            if (taskData.targetBuilding != targetBuilding)
                continue;

            if (taskData.itemType != itemType)
                continue;

            if (entityManager.HasComponent<DroneTaskCreateRequest>(entity))
            {
                DroneTaskCreateRequest request = entityManager
                    .GetComponentData<DroneTaskCreateRequest>(entity);

                if (request.type == DroneTaskTypeEnum.RemoveBuildingItem)
                    outstandingQuantity += request.totalQuantity;

                continue;
            }

            if (!entityManager.HasComponent<DroneTask>(entity))
                continue;

            DroneTask task = entityManager.GetComponentData<DroneTask>(entity);

            if (task.type != DroneTaskTypeEnum.RemoveBuildingItem)
                continue;

            if (!entityManager.HasComponent<DroneTaskStatus>(entity))
                continue;

            DroneTaskStatus status = entityManager
                .GetComponentData<DroneTaskStatus>(entity);

            if (status.state == DroneTaskStateEnum.Completed)
                continue;

            if (status.state == DroneTaskStateEnum.Cancelled)
                continue;

            DroneTaskQuantity quantity = entityManager
                .GetComponentData<DroneTaskQuantity>(entity);
            outstandingQuantity += quantity.totalQuantity -
                                   quantity.deliveredQuantity;
        }

        return outstandingQuantity;
    }
}
