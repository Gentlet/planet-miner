using Unity.Collections;
using Unity.Entities;
using UnityEngine;

[UpdateBefore(typeof(DroneTaskCommandSystem))]
public partial class ConstructionSiteCreationSystem : SystemBase
{
    private EntityQuery _requestQuery;
    private ChunkMapSystem _chunkMap;

    protected override void OnCreate()
    {
        _requestQuery = GetEntityQuery(
            ComponentType.ReadOnly<ConstructionSiteCreateRequest>(),
            ComponentType.ReadOnly<ConstructionSiteReservedCellElement>());
        _chunkMap = World.GetOrCreateSystemManaged<ChunkMapSystem>();
        RequireForUpdate<ConstructionConfig>();
        RequireForUpdate<DroneConfig>();
    }

    protected override void OnUpdate()
    {
        using NativeArray<ConstructionMaterialConfigElement> configs =
            DynamicBufferCopyUtility.CreateNativeCopy(
                SystemAPI.GetSingletonBuffer<ConstructionMaterialConfigElement>(true),
                Allocator.Temp);
        DroneConfig droneConfig = SystemAPI.GetSingleton<DroneConfig>();
        using NativeArray<Entity> requests =
            _requestQuery.ToEntityArray(Allocator.Temp);

        for (int i = 0; i < requests.Length; i++)
            CreateConstructionSite(requests[i], configs, droneConfig);
    }

    private void CreateConstructionSite(
        Entity requestEntity,
        NativeArray<ConstructionMaterialConfigElement> configs,
        DroneConfig droneConfig)
    {
        ConstructionSiteCreateRequest request = EntityManager
            .GetComponentData<ConstructionSiteCreateRequest>(requestEntity);
        int normalPriority = DroneTaskPriorityUtility.ResolveNormalPriority(
            request.normalPriority,
            droneConfig.defaultTaskPriority);
        DynamicBuffer<ConstructionSiteReservedCellElement> reservedCells =
            EntityManager.GetBuffer<ConstructionSiteReservedCellElement>(requestEntity);

        if (!HasMaterialConfig(configs, request.type))
        {
            RejectRequest(requestEntity, reservedCells,
                $"Construction request has no material config. Type : {request.type}");
            return;
        }

        EntityManager.AddComponentData(requestEntity, new ConstructionSite
        {
            type = request.type,
            direction = request.dir,
            selectedItemType = request.selectedItemType
        });
        EntityManager.AddComponentData(requestEntity, new GridPosition
        {
            gridPosition = request.gridPosition
        });
        EntityManager.AddBuffer<StoredItemElement>(requestEntity);
        EntityManager.AddBuffer<DroneReservedStorageCapacityElement>(requestEntity);
        DynamicBuffer<ConstructionMaterialRequirementElement> requirements =
            EntityManager.AddBuffer<ConstructionMaterialRequirementElement>(requestEntity);
        reservedCells = EntityManager.GetBuffer<ConstructionSiteReservedCellElement>(
            requestEntity);

        if (!_chunkMap.TryRegisterConstructionSite(requestEntity, reservedCells))
        {
            Debug.LogError(
                $"Construction site registration failed. Type : {request.type}, Anchor : {request.gridPosition}");
            _chunkMap.UnregisterConstructionSite(requestEntity, reservedCells, true);
            EntityManager.DestroyEntity(requestEntity);
            return;
        }

        for (int i = 0; i < configs.Length; i++)
        {
            ConstructionMaterialConfigElement config = configs[i];

            if (config.buildingType != request.type)
                continue;

            requirements.Add(new ConstructionMaterialRequirementElement
            {
                itemType = config.itemType,
                quantity = config.quantity
            });
        }

        EntityManager.RemoveComponent<ConstructionSiteCreateRequest>(requestEntity);

        for (int i = 0; i < configs.Length; i++)
        {
            if (configs[i].buildingType != request.type)
                continue;

            CreateConstructionTaskRequest(
                requestEntity,
                configs[i],
                normalPriority);
        }
    }

    private void RejectRequest(
        Entity requestEntity,
        DynamicBuffer<ConstructionSiteReservedCellElement> reservedCells,
        string message)
    {
        Debug.LogError(message);
        _chunkMap.UnregisterConstructionSite(requestEntity, reservedCells, true);
        EntityManager.DestroyEntity(requestEntity);
    }

    private static bool HasMaterialConfig(
        NativeArray<ConstructionMaterialConfigElement> configs,
        BuildingTypeEnum buildingType)
    {
        for (int i = 0; i < configs.Length; i++)
        {
            if (configs[i].buildingType == buildingType)
                return true;
        }

        return false;
    }

    private void CreateConstructionTaskRequest(
        Entity siteEntity,
        ConstructionMaterialConfigElement material,
        int normalPriority)
    {
        Entity taskRequest = EntityManager.CreateEntity();
        EntityManager.AddComponentData(taskRequest, new DroneTaskCreateRequest
        {
            type = DroneTaskTypeEnum.Construction,
            priorityClass = DroneTaskPriorityClassEnum.Normal,
            normalPriority = normalPriority,
            totalQuantity = material.quantity
        });
        EntityManager.AddComponentData(taskRequest, new DroneBuildingItemTaskData
        {
            targetBuilding = siteEntity,
            itemType = material.itemType
        });
    }
}
