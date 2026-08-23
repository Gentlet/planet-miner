using Unity.Entities;
using UnityEngine;

public partial class DroneTaskCommandSystem : SystemBase
{
    private Entity _schedulerStateEntity;

    protected override void OnCreate()
    {
        _schedulerStateEntity = EntityManager.CreateEntity();
        EntityManager.AddComponentData(
            _schedulerStateEntity,
            new DroneTaskSchedulerState { nextCreationOrder = 1 });

        RequireForUpdate<DroneConfig>();
    }

    protected override void OnUpdate()
    {
        DroneConfig config = SystemAPI.GetSingleton<DroneConfig>();
        DroneTaskSchedulerState schedulerState = EntityManager
            .GetComponentData<DroneTaskSchedulerState>(_schedulerStateEntity);
        EntityCommandBuffer ecb = SystemAPI
            .GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
            .CreateCommandBuffer(World.Unmanaged);

        foreach (var (requestReference, requestEntity) in
                 SystemAPI.Query<RefRO<DroneTaskCreateRequest>>()
                     .WithEntityAccess())
        {
            DroneTaskCreateRequest request = requestReference.ValueRO;

            if (!TryCreateTask(
                    request,
                    config,
                    schedulerState.nextCreationOrder,
                    ref ecb,
                    out Entity taskEntity))
            {
                ecb.DestroyEntity(requestEntity);
                continue;
            }


            if (IsBuildingItemTask(request.type))
            {
                if (!TryAddBuildingItemTaskData(
                        requestEntity,
                        taskEntity,
                        ref ecb))
                {
                    ecb.DestroyEntity(taskEntity);
                    ecb.DestroyEntity(requestEntity);
                    continue;
                }
            }

            if (request.type == DroneTaskTypeEnum.RecoverWorldItem)
            {
                if (!TryAddWorldItemRecoveryData(
                        requestEntity,
                        taskEntity,
                        ref ecb))
                {
                    ecb.DestroyEntity(taskEntity);
                    ecb.DestroyEntity(requestEntity);
                    continue;
                }
            }

            if (request.type == DroneTaskTypeEnum.Demolition)
            {
                if (!TryAddDemolitionTaskData(
                        requestEntity,
                        taskEntity,
                        ref ecb))
                {
                    ecb.DestroyEntity(taskEntity);
                    ecb.DestroyEntity(requestEntity);
                    continue;
                }
            }

            schedulerState.nextCreationOrder++;
            Debug.Log(
                $"Created drone task. Entity : {taskEntity}, Type : {request.type}, " +
                $"CreationOrder : {schedulerState.nextCreationOrder - 1}");
            ecb.DestroyEntity(requestEntity);
        }

        EntityManager.SetComponentData(_schedulerStateEntity, schedulerState);

        foreach (var (requestReference, requestEntity) in
                 SystemAPI.Query<RefRO<DroneTaskPriorityChangeRequest>>()
                     .WithEntityAccess())
        {
            ApplyPriorityChange(requestReference.ValueRO, config);
            ecb.DestroyEntity(requestEntity);
        }

        foreach (var (requestReference, requestEntity) in
                 SystemAPI.Query<RefRO<DroneTaskSuspensionRequest>>()
                     .WithEntityAccess())
        {
            ApplySuspensionChange(requestReference.ValueRO);
            ecb.DestroyEntity(requestEntity);
        }

        foreach (var (requestReference, requestEntity) in
                 SystemAPI.Query<RefRO<DroneTaskCancelRequest>>()
                     .WithEntityAccess())
        {
            ApplyCancellation(requestReference.ValueRO);
            ecb.DestroyEntity(requestEntity);
        }
    }

    private bool TryAddBuildingItemTaskData(
        Entity requestEntity,
        Entity taskEntity,
        ref EntityCommandBuffer ecb)
    {
        if (!EntityManager.HasComponent<DroneBuildingItemTaskData>(
                requestEntity))
        {
            Debug.LogError(
                "Building-item drone task request has no transfer data.");
            return false;
        }

        DroneBuildingItemTaskData taskData = EntityManager
            .GetComponentData<DroneBuildingItemTaskData>(requestEntity);

        if (taskData.targetBuilding == Entity.Null)
        {
            Debug.LogError(
                "Building-item drone task request has no target building.");
            return false;
        }

        if (!EntityManager.Exists(taskData.targetBuilding))
        {
            Debug.LogError(
                $"Building-item drone task target does not exist. Target : {taskData.targetBuilding}");
            return false;
        }

        if (!EntityManager.HasBuffer<StoredItemElement>(
                taskData.targetBuilding))
        {
            Debug.LogError(
                $"Building-item drone task target has no stored-item buffer. Target : {taskData.targetBuilding}");
            return false;
        }

        if (!taskData.itemType.IsValid())
        {
            Debug.LogError(
                $"Building-item drone task has an invalid item type. Type : {taskData.itemType}");
            return false;
        }

        ecb.AddComponent(taskEntity, taskData);
        return true;
    }

    private static bool IsBuildingItemTask(DroneTaskTypeEnum taskType)
    {
        return taskType == DroneTaskTypeEnum.Construction ||
               taskType == DroneTaskTypeEnum.RemoveBuildingItem ||
               taskType == DroneTaskTypeEnum.InsertBuildingItem;
    }

    private bool TryAddWorldItemRecoveryData(
        Entity requestEntity,
        Entity taskEntity,
        ref EntityCommandBuffer ecb)
    {
        if (!EntityManager.HasComponent<DroneWorldItemRecoveryTaskData>(
                requestEntity))
            return false;

        DroneWorldItemRecoveryTaskData data = EntityManager
            .GetComponentData<DroneWorldItemRecoveryTaskData>(requestEntity);

        if (data.itemEntity == Entity.Null)
            return false;

        if (!EntityManager.Exists(data.itemEntity))
            return false;

        if (!EntityManager.HasComponent<Item>(data.itemEntity))
            return false;

        if (EntityManager.HasComponent<StoredItem>(data.itemEntity))
            return false;

        ecb.AddComponent(taskEntity, data);
        return true;
    }

    private bool TryAddDemolitionTaskData(
        Entity requestEntity,
        Entity taskEntity,
        ref EntityCommandBuffer ecb)
    {
        if (!EntityManager.HasComponent<DroneDemolitionTaskData>(
                requestEntity))
            return false;

        DroneDemolitionTaskData data = EntityManager
            .GetComponentData<DroneDemolitionTaskData>(requestEntity);

        if (data.targetBuilding == Entity.Null)
            return false;

        if (!EntityManager.Exists(data.targetBuilding))
            return false;

        if (!EntityManager.HasComponent<BuildingOccupant>(
                data.targetBuilding))
            return false;

        if (EntityManager.HasComponent<IndestructibleBuilding>(
                data.targetBuilding))
            return false;

        ecb.AddComponent(taskEntity, data);
        return true;
    }

    private static bool TryCreateTask(
        DroneTaskCreateRequest request,
        DroneConfig config,
        ulong creationOrder,
        ref EntityCommandBuffer ecb,
        out Entity taskEntity)
    {
        taskEntity = Entity.Null;

        if (request.type >= DroneTaskTypeEnum.Count)
        {
            Debug.LogError(
                $"Cannot create drone task with invalid type. Type : {request.type}");
            return false;
        }

        if (!DroneTaskPriorityUtility.IsValidPriorityClass(
                request.priorityClass))
        {
            Debug.LogError(
                $"Cannot create drone task with invalid priority class. Class : {request.priorityClass}");
            return false;
        }

        if (request.totalQuantity <= 0)
        {
            Debug.LogError(
                $"Cannot create drone task with non-positive quantity. Quantity : {request.totalQuantity}");
            return false;
        }

        if (!TryResolveNormalPriority(
                request.normalPriority,
                config,
                out int normalPriority))
            return false;

        taskEntity = ecb.CreateEntity();
        ecb.AddComponent(taskEntity, new DroneTask { type = request.type });
        ecb.AddComponent(
            taskEntity,
            new DroneTaskStatus
            {
                state = DroneTaskStateEnum.Pending,
                stateBeforeSuspension = DroneTaskStateEnum.Pending
            });
        ecb.AddComponent(
            taskEntity,
            new DroneTaskPriority
            {
                priorityClass = request.priorityClass,
                normalPriority = normalPriority
            });
        ecb.AddComponent(
            taskEntity,
            new DroneTaskCreationOrder { value = creationOrder });
        ecb.AddComponent(
            taskEntity,
            new DroneTaskQuantity
            {
                totalQuantity = request.totalQuantity,
                deliveredQuantity = 0,
                reservedQuantity = 0
            });
        return true;
    }

    private void ApplyPriorityChange(
        DroneTaskPriorityChangeRequest request,
        DroneConfig config)
    {
        if (!ValidateTaskCommandTarget(request.taskEntity))
            return;

        if (!DroneTaskPriorityUtility.IsValidPriorityClass(
                request.priorityClass))
        {
            Debug.LogError(
                $"Cannot change drone task to invalid priority class. Class : {request.priorityClass}");
            return;
        }

        if (!TryResolveNormalPriority(
                request.normalPriority,
                config,
                out int normalPriority))
            return;

        EntityManager.SetComponentData(
            request.taskEntity,
            new DroneTaskPriority
            {
                priorityClass = request.priorityClass,
                normalPriority = normalPriority
            });
        Debug.Log(
            $"Changed drone task priority. Entity : {request.taskEntity}, " +
            $"Class : {request.priorityClass}, Priority : {normalPriority}");
    }

    private void ApplySuspensionChange(DroneTaskSuspensionRequest request)
    {
        if (!ValidateTaskCommandTarget(request.taskEntity))
            return;

        DroneTaskStatus status = EntityManager
            .GetComponentData<DroneTaskStatus>(request.taskEntity);

        if (request.suspend)
        {
            if (status.state == DroneTaskStateEnum.Suspended)
                return;

            if (IsTerminalState(status.state))
                return;

            status.stateBeforeSuspension = status.state;
            status.state = DroneTaskStateEnum.Suspended;
        }
        else
        {
            if (status.state != DroneTaskStateEnum.Suspended)
                return;

            status.state = GetResumedState(status.stateBeforeSuspension);
        }

        EntityManager.SetComponentData(request.taskEntity, status);
        Debug.Log(
            $"Changed drone task suspension. Entity : {request.taskEntity}, State : {status.state}");
    }

    private void ApplyCancellation(DroneTaskCancelRequest request)
    {
        if (!ValidateTaskCommandTarget(request.taskEntity))
            return;

        DroneTaskStatus status = EntityManager
            .GetComponentData<DroneTaskStatus>(request.taskEntity);

        if (IsTerminalState(status.state))
            return;

        status.state = DroneTaskStateEnum.Cancelled;
        EntityManager.SetComponentData(request.taskEntity, status);
        Debug.Log($"Cancelled drone task. Entity : {request.taskEntity}");
    }

    private bool ValidateTaskCommandTarget(Entity taskEntity)
    {
        if (taskEntity == Entity.Null)
        {
            Debug.LogError("Drone task command has no target entity.");
            return false;
        }

        if (!EntityManager.Exists(taskEntity))
        {
            Debug.LogError(
                $"Drone task command target does not exist. Entity : {taskEntity}");
            return false;
        }

        if (!EntityManager.HasComponent<DroneTask>(taskEntity))
        {
            Debug.LogError(
                $"Drone task command target is not a drone task. Entity : {taskEntity}");
            return false;
        }

        if (!EntityManager.HasComponent<DroneTaskStatus>(taskEntity))
        {
            Debug.LogError(
                $"Drone task command target has no status. Entity : {taskEntity}");
            return false;
        }

        if (!EntityManager.HasComponent<DroneTaskPriority>(taskEntity))
        {
            Debug.LogError(
                $"Drone task command target has no priority. Entity : {taskEntity}");
            return false;
        }

        return true;
    }

    private static bool TryResolveNormalPriority(
        int requestedPriority,
        DroneConfig config,
        out int normalPriority)
    {
        normalPriority = requestedPriority == 0
            ? config.defaultTaskPriority
            : requestedPriority;

        if (DroneTaskPriorityUtility.IsValidNormalPriority(normalPriority))
            return true;

        Debug.LogError(
            $"Invalid normal drone task priority. Value : {normalPriority}, " +
            $"Expected : {DroneTaskPriorityUtility.MinimumNormalPriority}-" +
            $"{DroneTaskPriorityUtility.MaximumNormalPriority}");
        return false;
    }

    private static DroneTaskStateEnum GetResumedState(
        DroneTaskStateEnum stateBeforeSuspension)
    {
        if (stateBeforeSuspension == DroneTaskStateEnum.Pending ||
            stateBeforeSuspension == DroneTaskStateEnum.InProgress)
            return stateBeforeSuspension;

        return DroneTaskStateEnum.Pending;
    }

    private static bool IsTerminalState(DroneTaskStateEnum state)
    {
        return state == DroneTaskStateEnum.Completed ||
               state == DroneTaskStateEnum.Cancelled;
    }
}
