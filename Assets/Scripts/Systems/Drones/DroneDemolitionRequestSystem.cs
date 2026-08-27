using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

[UpdateBefore(typeof(DroneTaskCommandSystem))]
public partial class DroneDemolitionRequestSystem : SystemBase
{
    private ChunkMapSystem _chunkMap;
    private EntityQuery _requestQuery;
    private EntityQuery _taskDataQuery;

    protected override void OnCreate()
    {
        _chunkMap = World.GetOrCreateSystemManaged<ChunkMapSystem>();
        _requestQuery = GetEntityQuery(
            ComponentType.ReadOnly<DroneDemolitionRequest>());
        _taskDataQuery = GetEntityQuery(
            ComponentType.ReadOnly<DroneDemolitionTaskData>());
        RequireForUpdate<DroneConfig>();
    }

    protected override void OnUpdate()
    {
        int defaultPriority = SystemAPI.GetSingleton<DroneConfig>()
            .defaultTaskPriority;
        using NativeArray<Entity> requests =
            _requestQuery.ToEntityArray(Allocator.Temp);

        for (int i = 0; i < requests.Length; i++)
        {
            Entity requestEntity = requests[i];
            DroneDemolitionRequest request = EntityManager
                .GetComponentData<DroneDemolitionRequest>(requestEntity);
            int normalPriority = DroneTaskPriorityUtility.ResolveNormalPriority(
                request.normalPriority,
                defaultPriority);
            TryCreateDemolitionTask(request.gridPosition, normalPriority);
            EntityManager.DestroyEntity(requestEntity);
        }
    }

    private bool TryCreateDemolitionTask(int2 gridPosition, int normalPriority)
    {
        if (!_chunkMap.TryGetBuilding(gridPosition, out Entity buildingEntity))
            return false;

        if (buildingEntity == Entity.Null)
            return false;

        if (!EntityManager.Exists(buildingEntity))
            return false;

        if (!EntityManager.HasComponent<BuildingOccupant>(buildingEntity))
            return false;

        if (EntityManager.HasComponent<IndestructibleBuilding>(buildingEntity))
            return false;

        if (HasActiveDemolitionTask(buildingEntity))
            return false;

        Entity taskRequest = EntityManager.CreateEntity();
        EntityManager.AddComponentData(taskRequest, new DroneTaskCreateRequest
        {
            type = DroneTaskTypeEnum.Demolition,
            priorityClass = DroneTaskPriorityClassEnum.Normal,
            normalPriority = normalPriority,
            totalQuantity = 1
        });
        EntityManager.AddComponentData(taskRequest, new DroneDemolitionTaskData
        {
            targetBuilding = buildingEntity
        });
        return true;
    }

    private bool HasActiveDemolitionTask(Entity buildingEntity)
    {
        using NativeArray<Entity> entities =
            _taskDataQuery.ToEntityArray(Allocator.Temp);

        for (int i = 0; i < entities.Length; i++)
        {
            Entity entity = entities[i];
            DroneDemolitionTaskData data = EntityManager
                .GetComponentData<DroneDemolitionTaskData>(entity);

            if (data.targetBuilding != buildingEntity)
                continue;

            if (!EntityManager.HasComponent<DroneTaskStatus>(entity))
                return true;

            DroneTaskStatus status = EntityManager
                .GetComponentData<DroneTaskStatus>(entity);

            if (status.state != DroneTaskStateEnum.Completed &&
                status.state != DroneTaskStateEnum.Cancelled)
                return true;
        }

        return false;
    }
}
