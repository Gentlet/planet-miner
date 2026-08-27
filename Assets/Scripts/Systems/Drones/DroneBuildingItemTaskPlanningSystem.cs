using Unity.Collections;
using Unity.Entities;

[UpdateAfter(typeof(DroneTaskCommandSystem))]
[UpdateAfter(typeof(DroneStationNetworkSystem))]
[UpdateBefore(typeof(DroneTaskReservationSystem))]
public partial class DroneBuildingItemTaskPlanningSystem : SystemBase
{
    private enum ReservationPlanResult : byte
    {
        ReservationCreated,
        MissingSourceItems,
        DestinationCapacityUnavailable
    }

    private EntityQuery _taskQuery;
    private EntityQuery _storageQuery;
    private DroneStationNetworkSystem _networkSystem;

    protected override void OnCreate()
    {
        _taskQuery = GetEntityQuery(
            ComponentType.ReadOnly<DroneTask>(),
            ComponentType.ReadOnly<DroneTaskStatus>(),
            ComponentType.ReadOnly<DroneTaskQuantity>(),
            ComponentType.ReadOnly<DroneBuildingItemTaskData>());
        _storageQuery = GetEntityQuery(
            ComponentType.ReadOnly<Storage>(),
            ComponentType.ReadOnly<BuildingType>(),
            ComponentType.ReadOnly<GridPosition>(),
            ComponentType.ReadOnly<StoredItemElement>());
        _networkSystem = World.GetOrCreateSystemManaged<
            DroneStationNetworkSystem>();

        RequireForUpdate<DroneConfig>();
        RequireForUpdate<CrafterConfig>();
        RequireForUpdate<ItemStorageLimitElement>();
    }

    protected override void OnUpdate()
    {
        DroneConfig droneConfig = SystemAPI.GetSingleton<DroneConfig>();
        using NativeArray<ItemStorageLimitElement> storageLimits =
            DynamicBufferCopyUtility.CreateNativeCopy(
                SystemAPI.GetSingletonBuffer<ItemStorageLimitElement>(true),
                Allocator.Temp);
        using NativeArray<CrafterRecipeElement> recipes =
            DynamicBufferCopyUtility.CreateNativeCopy(
                SystemAPI.GetSingletonBuffer<CrafterRecipeElement>(true),
                Allocator.Temp);
        using NativeArray<CrafterRecipeIngredientElement> ingredients =
            DynamicBufferCopyUtility.CreateNativeCopy(
                SystemAPI.GetSingletonBuffer<
                    CrafterRecipeIngredientElement>(true),
                Allocator.Temp);
        using NativeArray<Entity> taskEntities =
            _taskQuery.ToEntityArray(Allocator.Temp);
        using NativeArray<Entity> storageEntities =
            _storageQuery.ToEntityArray(Allocator.Temp);

        for (int i = 0; i < taskEntities.Length; i++)
        {
            PlanNextReservation(
                taskEntities[i],
                droneConfig.carryingCapacity,
                storageEntities,
                storageLimits,
                recipes,
                ingredients);
        }
    }

    private void PlanNextReservation(
        Entity taskEntity,
        int carryingCapacity,
        NativeArray<Entity> storageEntities,
        NativeArray<ItemStorageLimitElement> storageLimits,
        NativeArray<CrafterRecipeElement> recipes,
        NativeArray<CrafterRecipeIngredientElement> ingredients)
    {
        DroneTaskStatus status = EntityManager
            .GetComponentData<DroneTaskStatus>(taskEntity);
        bool isAutomaticallySuspended =
            DroneTaskAutomaticSuspensionUtility.IsAutomaticallySuspended(
                EntityManager,
                taskEntity);

        if (status.state != DroneTaskStateEnum.Pending &&
            status.state != DroneTaskStateEnum.InProgress &&
            !isAutomaticallySuspended)
            return;

        DroneTaskQuantity taskQuantity = EntityManager
            .GetComponentData<DroneTaskQuantity>(taskEntity);

        if (taskQuantity.UnreservedQuantity <= 0)
            return;

        DroneTask task = EntityManager.GetComponentData<DroneTask>(taskEntity);
        DroneBuildingItemTaskData taskData = EntityManager
            .GetComponentData<DroneBuildingItemTaskData>(taskEntity);

        if (!ValidateTarget(taskEntity, taskData.targetBuilding))
            return;

        if (!TryGetNetworkId(taskData.targetBuilding, out int networkId))
        {
            SuspendAutomaticallyIfIdle(
                taskEntity,
                taskQuantity,
                DroneTaskAutomaticSuspensionReasonEnum.OutsideDroneNetwork);
            return;
        }

        if (task.type == DroneTaskTypeEnum.InsertBuildingItem ||
            task.type == DroneTaskTypeEnum.Construction)
        {
            if (!DroneItemDestinationUtility.CanAcceptItem(
                    EntityManager,
                    taskData.targetBuilding,
                    taskData.itemType,
                    recipes,
                    ingredients))
            {
                CancelTask(taskEntity);
                return;
            }

            ReservationPlanResult planResult = PlanInsertion(
                taskEntity,
                taskData,
                taskQuantity.UnreservedQuantity,
                carryingCapacity,
                networkId,
                storageEntities,
                storageLimits,
                recipes,
                ingredients);
            ApplyPlanResult(taskEntity, taskQuantity, planResult);
            return;
        }

        if (task.type == DroneTaskTypeEnum.RemoveBuildingItem)
        {
            ReservationPlanResult planResult = PlanRemoval(
                taskEntity,
                taskData,
                taskQuantity.UnreservedQuantity,
                carryingCapacity,
                networkId,
                storageEntities,
                storageLimits,
                recipes,
                ingredients);
            ApplyPlanResult(taskEntity, taskQuantity, planResult);
            return;
        }

        CancelTask(taskEntity);
    }

    private bool ValidateTarget(Entity taskEntity, Entity targetBuilding)
    {
        if (targetBuilding == Entity.Null)
        {
            CancelTask(taskEntity);
            return false;
        }

        if (!EntityManager.Exists(targetBuilding))
        {
            CancelTask(taskEntity);
            return false;
        }

        if (!EntityManager.HasBuffer<StoredItemElement>(targetBuilding))
        {
            CancelTask(taskEntity);
            return false;
        }

        if (!EntityManager.HasComponent<GridPosition>(targetBuilding))
        {
            CancelTask(taskEntity);
            return false;
        }

        return true;
    }

    private void CreateReservationRequest(
        Entity taskEntity,
        Entity sourceOwner,
        Entity destinationOwner,
        ItemTypeEnum itemType,
        int quantity)
    {
        Entity requestEntity = EntityManager.CreateEntity();
        EntityManager.AddComponentData(
            requestEntity,
            new DroneTaskReservationRequest
            {
                taskEntity = taskEntity,
                sourceOwner = sourceOwner,
                destinationOwner = destinationOwner,
                itemType = itemType,
                quantity = quantity
            });
    }

    private void CancelTask(Entity taskEntity)
    {
        DroneTaskAutomaticSuspensionUtility.Clear(
            EntityManager,
            taskEntity);
        DroneTaskStatus status = EntityManager
            .GetComponentData<DroneTaskStatus>(taskEntity);
        status.state = DroneTaskStateEnum.Cancelled;
        EntityManager.SetComponentData(taskEntity, status);
    }

    private void ApplyPlanResult(
        Entity taskEntity,
        DroneTaskQuantity taskQuantity,
        ReservationPlanResult planResult)
    {
        if (planResult == ReservationPlanResult.ReservationCreated)
        {
            DroneTaskAutomaticSuspensionUtility.Resume(
                EntityManager,
                taskEntity);
            return;
        }

        DroneTaskAutomaticSuspensionReasonEnum reason =
            planResult == ReservationPlanResult.MissingSourceItems
                ? DroneTaskAutomaticSuspensionReasonEnum.MissingSourceItems
                : DroneTaskAutomaticSuspensionReasonEnum
                    .DestinationCapacityUnavailable;
        SuspendAutomaticallyIfIdle(taskEntity, taskQuantity, reason);
    }

    private void SuspendAutomaticallyIfIdle(
        Entity taskEntity,
        DroneTaskQuantity taskQuantity,
        DroneTaskAutomaticSuspensionReasonEnum reason)
    {
        if (taskQuantity.reservedQuantity > 0)
            return;

        DroneTaskAutomaticSuspensionUtility.Suspend(
            EntityManager,
            taskEntity,
            reason);
    }
}
