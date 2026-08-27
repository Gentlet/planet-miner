using Unity.Entities;

public static class DroneTaskAutomaticSuspensionUtility
{
    public static bool IsAutomaticallySuspended(
        EntityManager entityManager,
        Entity taskEntity)
    {
        return entityManager.HasComponent<DroneTaskAutomaticSuspension>(
            taskEntity);
    }

    public static void Suspend(
        EntityManager entityManager,
        Entity taskEntity,
        DroneTaskAutomaticSuspensionReasonEnum reason)
    {
        DroneTaskStatus status = entityManager
            .GetComponentData<DroneTaskStatus>(taskEntity);

        if (status.state != DroneTaskStateEnum.Suspended)
        {
            status.stateBeforeSuspension = status.state;
            status.state = DroneTaskStateEnum.Suspended;
            entityManager.SetComponentData(taskEntity, status);
        }

        DroneTaskAutomaticSuspension suspension =
            new DroneTaskAutomaticSuspension { reason = reason };

        if (IsAutomaticallySuspended(entityManager, taskEntity))
        {
            entityManager.SetComponentData(taskEntity, suspension);
            return;
        }

        entityManager.AddComponentData(taskEntity, suspension);
    }

    public static void Resume(EntityManager entityManager, Entity taskEntity)
    {
        if (!IsAutomaticallySuspended(entityManager, taskEntity))
            return;

        DroneTaskStatus status = entityManager
            .GetComponentData<DroneTaskStatus>(taskEntity);

        if (status.state == DroneTaskStateEnum.Suspended)
        {
            status.state = GetResumedState(status.stateBeforeSuspension);
            entityManager.SetComponentData(taskEntity, status);
        }

        entityManager.RemoveComponent<DroneTaskAutomaticSuspension>(
            taskEntity);
    }

    public static void Clear(EntityManager entityManager, Entity taskEntity)
    {
        if (!IsAutomaticallySuspended(entityManager, taskEntity))
            return;

        entityManager.RemoveComponent<DroneTaskAutomaticSuspension>(
            taskEntity);
    }

    private static DroneTaskStateEnum GetResumedState(
        DroneTaskStateEnum stateBeforeSuspension)
    {
        if (stateBeforeSuspension == DroneTaskStateEnum.Pending ||
            stateBeforeSuspension == DroneTaskStateEnum.InProgress)
            return stateBeforeSuspension;

        return DroneTaskStateEnum.Pending;
    }
}
