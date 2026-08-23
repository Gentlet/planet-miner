using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

[UpdateBefore(typeof(DroneTaskCommandSystem))]
[UpdateBefore(typeof(DroneTaskReservationSystem))]
[UpdateBefore(typeof(DroneCargoTransferSystem))]
[UpdateBefore(typeof(ConstructionCompletionSystem))]
public partial class ConstructionCancelSystem : SystemBase
{
    private ChunkMapSystem _chunkMap;
    private ItemStorageSystem _itemStorage;
    private EntityQuery _taskQuery;

    protected override void OnCreate()
    {
        _chunkMap = World.GetOrCreateSystemManaged<ChunkMapSystem>();
        _itemStorage = World.GetOrCreateSystemManaged<ItemStorageSystem>();
        _taskQuery = GetEntityQuery(
            ComponentType.ReadOnly<DroneTask>(),
            ComponentType.ReadWrite<DroneTaskStatus>(),
            ComponentType.ReadOnly<DroneBuildingItemTaskData>());
        RequireForUpdate<ConstructionCancelRequest>();
    }

    protected override void OnUpdate()
    {
        int defaultPriority = SystemAPI.HasSingleton<DroneConfig>()
            ? SystemAPI.GetSingleton<DroneConfig>().defaultTaskPriority
            : 5;
        using NativeArray<Entity> requests = GetEntityQuery(
                ComponentType.ReadOnly<ConstructionCancelRequest>())
            .ToEntityArray(Allocator.Temp);

        for (int i = 0; i < requests.Length; i++)
        {
            Entity requestEntity = requests[i];
            ConstructionCancelRequest request = EntityManager
                .GetComponentData<ConstructionCancelRequest>(requestEntity);

            if (_chunkMap.TryGetConstructionSite(
                    request.gridPosition,
                    out Entity siteEntity))
                CancelSite(siteEntity, defaultPriority);

            EntityManager.DestroyEntity(requestEntity);
        }
    }

    private void CancelSite(Entity siteEntity, int defaultPriority)
    {
        if (!EntityManager.Exists(siteEntity))
            return;

        if (!EntityManager.HasComponent<ConstructionSite>(siteEntity))
            return;

        CancelConstructionTasks(siteEntity);
        RestoreArrivedMaterials(siteEntity, defaultPriority);

        DynamicBuffer<ConstructionSiteReservedCellElement> reservedCells =
            EntityManager.GetBuffer<ConstructionSiteReservedCellElement>(
                siteEntity,
                true);
        _chunkMap.UnregisterConstructionSite(
            siteEntity,
            reservedCells,
            true);
        EntityManager.DestroyEntity(siteEntity);
    }

    private void CancelConstructionTasks(Entity siteEntity)
    {
        using NativeArray<Entity> tasks =
            _taskQuery.ToEntityArray(Allocator.Temp);

        for (int i = 0; i < tasks.Length; i++)
        {
            Entity taskEntity = tasks[i];
            DroneTask task = EntityManager.GetComponentData<DroneTask>(taskEntity);
            DroneBuildingItemTaskData data = EntityManager
                .GetComponentData<DroneBuildingItemTaskData>(taskEntity);

            if (task.type != DroneTaskTypeEnum.Construction)
                continue;

            if (data.targetBuilding != siteEntity)
                continue;

            DroneTaskStatus status = EntityManager
                .GetComponentData<DroneTaskStatus>(taskEntity);

            if (status.state == DroneTaskStateEnum.Completed ||
                status.state == DroneTaskStateEnum.Cancelled)
                continue;

            status.state = DroneTaskStateEnum.Cancelled;
            EntityManager.SetComponentData(taskEntity, status);
        }
    }

    private void RestoreArrivedMaterials(Entity siteEntity, int defaultPriority)
    {
        int2 anchor = EntityManager.GetComponentData<GridPosition>(siteEntity)
            .gridPosition;

        while (EntityManager.Exists(siteEntity) &&
               EntityManager.GetBuffer<StoredItemElement>(siteEntity).Length > 0)
        {
            DynamicBuffer<StoredItemElement> storedItems =
                EntityManager.GetBuffer<StoredItemElement>(siteEntity, true);
            Entity itemEntity = storedItems[0].itemEntity;

            if (!_itemStorage.TryRestoreItemImmediate<StoredItemElement>(
                    siteEntity,
                    0,
                    anchor))
                break;

            DroneWorldItemRecoveryTaskUtility.TryCreateRequest(
                EntityManager,
                itemEntity,
                defaultPriority);
        }
    }
}
