using Unity.Collections;
using Unity.Entities;

public partial class DroneTaskSchedulingSystem : SystemBase
{
    private EntityQuery _taskQuery;

    protected override void OnCreate()
    {
        _taskQuery = GetEntityQuery(
            ComponentType.ReadOnly<DroneTask>(),
            ComponentType.ReadOnly<DroneTaskStatus>(),
            ComponentType.ReadOnly<DroneTaskPriority>(),
            ComponentType.ReadOnly<DroneTaskCreationOrder>());
    }

    protected override void OnUpdate()
    {
    }

    public bool TryGetNextTask(out Entity taskEntity)
    {
        taskEntity = Entity.Null;
        DroneTaskPriority selectedPriority = default;
        ulong selectedCreationOrder = 0;

        using NativeArray<Entity> taskEntities =
            _taskQuery.ToEntityArray(Allocator.Temp);

        for (int i = 0; i < taskEntities.Length; i++)
        {
            Entity candidateEntity = taskEntities[i];
            DroneTaskStatus candidateStatus = EntityManager
                .GetComponentData<DroneTaskStatus>(candidateEntity);

            if (candidateStatus.state != DroneTaskStateEnum.Pending)
                continue;

            DroneTaskPriority candidatePriority = EntityManager
                .GetComponentData<DroneTaskPriority>(candidateEntity);
            ulong candidateCreationOrder = EntityManager
                .GetComponentData<DroneTaskCreationOrder>(candidateEntity)
                .value;

            if (taskEntity != Entity.Null)
            {
                if (!DroneTaskPriorityUtility.IsHigherPriority(
                        candidatePriority,
                        candidateCreationOrder,
                        selectedPriority,
                        selectedCreationOrder))
                    continue;
            }

            taskEntity = candidateEntity;
            selectedPriority = candidatePriority;
            selectedCreationOrder = candidateCreationOrder;
        }

        return taskEntity != Entity.Null;
    }
}
