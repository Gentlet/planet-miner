using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;

[UpdateAfter(typeof(DroneTaskCommandSystem))]
[UpdateAfter(typeof(DroneStationNetworkSystem))]
[UpdateBefore(typeof(DroneDispatchSystem))]
public partial class DroneDirectTaskPlanningSystem : SystemBase
{
    private static readonly ProfilerMarker ScanDirectTaskPlansMarker =
        new("DroneDirectTaskPlanning.ScanTasks");
    private static readonly ProfilerMarker PlanWorldItemRecoveryMarker =
        new("DroneDirectTaskPlanning.PlanWorldItemRecovery");

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

        using (ScanDirectTaskPlansMarker.Auto())
        {
            for (int i = 0; i < tasks.Length; i++)
            {
                Entity taskEntity = tasks[i];
                DroneTask task = EntityManager.GetComponentData<DroneTask>(taskEntity);

                if (task.type == DroneTaskTypeEnum.Demolition)
                {
                    PlanDemolitionTask(taskEntity);
                }
                else if (task.type == DroneTaskTypeEnum.RecoverWorldItem)
                {
                    using (PlanWorldItemRecoveryMarker.Auto())
                        PlanWorldItemRecovery(taskEntity);
                }
            }
        }
    }

    private void PlanDemolitionTask(Entity taskEntity)
    {
        DroneTaskStatus status = EntityManager
            .GetComponentData<DroneTaskStatus>(taskEntity);

        if (status.state != DroneTaskStateEnum.Pending)
        {
            if (status.state == DroneTaskStateEnum.Completed ||
                status.state == DroneTaskStateEnum.Cancelled)
                RemovePlan(taskEntity);

            return;
        }

        if (!EntityManager.HasComponent<DroneDemolitionTaskData>(taskEntity))
        {
            RemovePlan(taskEntity);
            CancelTask(taskEntity, status);
            return;
        }

        Entity target = EntityManager
            .GetComponentData<DroneDemolitionTaskData>(taskEntity)
            .targetBuilding;

        if (target == Entity.Null)
        {
            RemovePlan(taskEntity);
            CancelTask(taskEntity, status);
            return;
        }

        if (!EntityManager.Exists(target))
        {
            RemovePlan(taskEntity);
            CancelTask(taskEntity, status);
            return;
        }

        if (!EntityManager.HasComponent<BuildingOccupant>(target))
        {
            RemovePlan(taskEntity);
            CancelTask(taskEntity, status);
            return;
        }

        if (EntityManager.HasComponent<IndestructibleBuilding>(target))
        {
            RemovePlan(taskEntity);
            CancelTask(taskEntity, status);
            return;
        }

        int2 workCell = EntityManager.GetComponentData<GridPosition>(target)
            .gridPosition;

        if (!_networkSystem.TryGetNetworkIdAtCell(workCell, out int networkId))
        {
            RemovePlan(taskEntity);
            return;
        }

        SetPlan(taskEntity, networkId, workCell, Entity.Null);
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
        {
            if (status.state == DroneTaskStateEnum.Completed ||
                status.state == DroneTaskStateEnum.Cancelled)
                RemovePlan(taskEntity);

            return;
        }

        if (!EntityManager.HasComponent<DroneWorldItemRecoveryTaskData>(
                taskEntity))
        {
            RemovePlan(taskEntity);
            CancelTask(taskEntity, status);
            return;
        }

        Entity itemEntity = EntityManager
            .GetComponentData<DroneWorldItemRecoveryTaskData>(taskEntity)
            .itemEntity;

        if (!TryGetWorldItemPosition(itemEntity, out GridPosition position))
        {
            RemovePlan(taskEntity);
            CancelTask(taskEntity, status);
            return;
        }

        if (!_networkSystem.TryGetNetworkIdAtCell(
                position.gridPosition,
                out int networkId))
        {
            RemovePlan(taskEntity);
            DroneTaskAutomaticSuspensionUtility.Suspend(
                EntityManager,
                taskEntity,
                DroneTaskAutomaticSuspensionReasonEnum.OutsideDroneNetwork);
            return;
        }

        DroneTaskAutomaticSuspensionUtility.Resume(
            EntityManager,
            taskEntity);

        if (TryGetCurrentPlan(
                taskEntity,
                networkId,
                position.gridPosition,
                out DroneDirectTaskPlan currentPlan))
        {
            if (_directTaskSystem.CanUseRecoveryDestination(
                    itemEntity,
                    networkId,
                    currentPlan.destinationOwner))
                return;

            RemovePlan(taskEntity);
        }

        if (_directTaskSystem.TryGetRecoveryDestination(
                itemEntity,
                networkId,
                out Entity destinationOwner))
        {
            SetPlan(
                taskEntity,
                networkId,
                position.gridPosition,
                destinationOwner);
            return;
        }

        RemovePlan(taskEntity);
        DroneTaskAutomaticSuspensionUtility.Suspend(
            EntityManager,
            taskEntity,
            DroneTaskAutomaticSuspensionReasonEnum
                .DestinationCapacityUnavailable);
    }

    private bool TryGetCurrentPlan(
        Entity taskEntity,
        int networkId,
        int2 workCell,
        out DroneDirectTaskPlan plan)
    {
        plan = default;

        if (!EntityManager.HasComponent<DroneDirectTaskPlan>(taskEntity))
            return false;

        plan = EntityManager.GetComponentData<DroneDirectTaskPlan>(taskEntity);

        if (plan.networkId != networkId)
            return false;

        if (!plan.workCell.Equals(workCell))
            return false;

        if (plan.destinationOwner == Entity.Null)
            return false;

        return EntityManager.Exists(plan.destinationOwner);
    }

    private void SetPlan(
        Entity taskEntity,
        int networkId,
        int2 workCell,
        Entity destinationOwner)
    {
        var plan = new DroneDirectTaskPlan
        {
            networkId = networkId,
            workCell = workCell,
            destinationOwner = destinationOwner
        };

        if (EntityManager.HasComponent<DroneDirectTaskPlan>(taskEntity))
        {
            EntityManager.SetComponentData(taskEntity, plan);
            return;
        }

        EntityManager.AddComponentData(taskEntity, plan);
    }

    private void RemovePlan(Entity taskEntity)
    {
        if (!EntityManager.HasComponent<DroneDirectTaskPlan>(taskEntity))
            return;

        EntityManager.RemoveComponent<DroneDirectTaskPlan>(taskEntity);
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
