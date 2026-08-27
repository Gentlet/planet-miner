using Unity.Collections;
using Unity.Entities;

[UpdateAfter(typeof(DroneTaskCommandSystem))]
[UpdateAfter(typeof(DroneStationNetworkSystem))]
[UpdateBefore(typeof(DroneDispatchSystem))]
public partial class DroneDirectTaskPlanningSystem : SystemBase
{
    private EntityQuery _taskQuery;
    private DroneStationNetworkSystem _networkSystem;
    private DroneDirectTaskSystem _directTaskSystem;

    protected override void OnCreate()
    {
        _taskQuery = GetEntityQuery(
            ComponentType.ReadOnly<DroneTask>(),
            ComponentType.ReadWrite<DroneTaskStatus>(),
            ComponentType.ReadWrite<DroneTaskPriority>());
        _networkSystem = World.GetOrCreateSystemManaged<
            DroneStationNetworkSystem>();
        _directTaskSystem = World.GetOrCreateSystemManaged<
            DroneDirectTaskSystem>();
    }

    protected override void OnUpdate()
    {
        using NativeArray<Entity> tasks =
            _taskQuery.ToEntityArray(Allocator.Temp);

        for (int i = 0; i < tasks.Length; i++)
        {
            Entity taskEntity = tasks[i];
            DroneTask task = EntityManager.GetComponentData<DroneTask>(taskEntity);

            if (task.type == DroneTaskTypeEnum.Demolition)
                ValidateDemolitionTask(taskEntity);
            else if (task.type == DroneTaskTypeEnum.RecoverWorldItem)
                PlanWorldItemRecovery(taskEntity);
        }
    }

    private void ValidateDemolitionTask(Entity taskEntity)
    {
        DroneTaskStatus status = EntityManager
            .GetComponentData<DroneTaskStatus>(taskEntity);

        if (status.state != DroneTaskStateEnum.Pending)
            return;

        if (!EntityManager.HasComponent<DroneDemolitionTaskData>(taskEntity))
        {
            CancelTask(taskEntity, status);
            return;
        }

        Entity target = EntityManager
            .GetComponentData<DroneDemolitionTaskData>(taskEntity)
            .targetBuilding;

        if (target == Entity.Null)
        {
            CancelTask(taskEntity, status);
            return;
        }

        if (!EntityManager.Exists(target))
        {
            CancelTask(taskEntity, status);
            return;
        }

        if (!EntityManager.HasComponent<BuildingOccupant>(target))
        {
            CancelTask(taskEntity, status);
            return;
        }

        if (EntityManager.HasComponent<IndestructibleBuilding>(target))
            CancelTask(taskEntity, status);
    }

    private void PlanWorldItemRecovery(Entity taskEntity)
    {
        DroneTaskStatus status = EntityManager
            .GetComponentData<DroneTaskStatus>(taskEntity);
        bool isAutomaticallySuspended =
            DroneTaskAutomaticSuspensionUtility.IsAutomaticallySuspended(
                EntityManager,
                taskEntity);

        if (status.state != DroneTaskStateEnum.Pending &&
            !isAutomaticallySuspended)
            return;

        if (!EntityManager.HasComponent<DroneWorldItemRecoveryTaskData>(
                taskEntity))
        {
            CancelTask(taskEntity, status);
            return;
        }

        Entity itemEntity = EntityManager
            .GetComponentData<DroneWorldItemRecoveryTaskData>(taskEntity)
            .itemEntity;

        if (!TryGetWorldItemPosition(itemEntity, out GridPosition position))
        {
            CancelTask(taskEntity, status);
            return;
        }

        if (!_networkSystem.TryGetNetworkIdAtCell(
                position.gridPosition,
                out int networkId))
        {
            DroneTaskAutomaticSuspensionUtility.Suspend(
                EntityManager,
                taskEntity,
                DroneTaskAutomaticSuspensionReasonEnum.OutsideDroneNetwork);
            return;
        }

        DroneTaskAutomaticSuspensionUtility.Resume(
            EntityManager,
            taskEntity);

        if (_directTaskSystem.HasAvailableRecoveryDestination(
                itemEntity,
                networkId))
            return;

        DroneTaskPriority priority = EntityManager
            .GetComponentData<DroneTaskPriority>(taskEntity);

        if (priority.priorityClass != DroneTaskPriorityClassEnum.Normal)
            return;

        priority.normalPriority = DroneTaskPriorityUtility.MaximumNormalPriority;
        EntityManager.SetComponentData(taskEntity, priority);
    }

    private bool TryGetWorldItemPosition(
        Entity itemEntity,
        out GridPosition position)
    {
        position = default;

        if (itemEntity == Entity.Null)
            return false;

        if (!EntityManager.Exists(itemEntity))
            return false;

        if (!EntityManager.HasComponent<Item>(itemEntity))
            return false;

        if (!EntityManager.HasComponent<GridPosition>(itemEntity))
            return false;

        if (EntityManager.HasComponent<StoredItem>(itemEntity))
            return false;

        if (EntityManager.HasComponent<Disabled>(itemEntity))
            return false;

        position = EntityManager.GetComponentData<GridPosition>(itemEntity);
        return true;
    }

    private void CancelTask(Entity taskEntity, DroneTaskStatus status)
    {
        DroneTaskAutomaticSuspensionUtility.Clear(
            EntityManager,
            taskEntity);
        status.state = DroneTaskStateEnum.Cancelled;
        EntityManager.SetComponentData(taskEntity, status);
    }
}
