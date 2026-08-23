using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[UpdateAfter(typeof(CrafterSystem))]
[UpdateAfter(typeof(MiningSystem))]
[UpdateAfter(typeof(StorageSystem))]
[UpdateAfter(typeof(CoalGeneratorFuelSystem))]
public partial class BuildingDestroySystem : SystemBase
{
    private ChunkMapSystem _chunkMap;
    private ItemStorageSystem _itemStorage;
    private PowerGridSystem _powerGrid;
    private DroneIdentityConversionSystem _droneIdentityConversion;
    private EntityQuery _constructionConfigQuery;
    private EntityQuery _destroyRequestQuery;
    private readonly List<Entity> _restoredItemEntities = new();

    protected override void OnCreate()
    {
        _chunkMap = World.GetExistingSystemManaged<ChunkMapSystem>();
        _itemStorage = World.GetExistingSystemManaged<ItemStorageSystem>();
        _powerGrid = World.GetExistingSystemManaged<PowerGridSystem>();
        _droneIdentityConversion = World.GetOrCreateSystemManaged<
            DroneIdentityConversionSystem>();
        _constructionConfigQuery = GetEntityQuery(
            ComponentType.ReadOnly<ConstructionMaterialConfigElement>());
        _destroyRequestQuery = GetEntityQuery(
            ComponentType.ReadOnly<BuildingDestroyRequest>());
        RequireForUpdate<BuildingDestroyRequest>();
    }

    protected override void OnUpdate()
    {
        if (_chunkMap == null)
        {
            _chunkMap = World.GetExistingSystemManaged<ChunkMapSystem>();
            if (_chunkMap == null)
                return;
        }

        if (_itemStorage == null)
        {
            _itemStorage = World.GetExistingSystemManaged<ItemStorageSystem>();
            if (_itemStorage == null)
                return;
        }

        if (_powerGrid == null)
        {
            _powerGrid = World.GetExistingSystemManaged<PowerGridSystem>();

            if (_powerGrid == null)
                return;
        }

        EntityCommandBuffer ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(World.Unmanaged);

        if (_constructionConfigQuery.IsEmptyIgnoreFilter)
        {
            Debug.LogError(
                "Building destruction is waiting for construction material config.");
            return;
        }

        Entity constructionConfigEntity =
            _constructionConfigQuery.GetSingletonEntity();
        using NativeArray<ConstructionMaterialConfigElement> constructionMaterials =
            DynamicBufferCopyUtility.CreateNativeCopy(
                EntityManager.GetBuffer<ConstructionMaterialConfigElement>(
                    constructionConfigEntity,
                    true),
                Allocator.Temp);

        using NativeArray<Entity> requestEntities =
            _destroyRequestQuery.ToEntityArray(Allocator.Temp);

        for (int requestIndex = 0;
             requestIndex < requestEntities.Length;
             requestIndex++)
        {
            Entity requestEntity = requestEntities[requestIndex];
            BuildingDestroyRequest destroyRequest = EntityManager
                .GetComponentData<BuildingDestroyRequest>(requestEntity);

            if (destroyRequest.cause >= BuildingDestroyCauseEnum.Count)
            {
                Debug.LogError(
                    $"Building destruction request has an invalid cause. Cause : {destroyRequest.cause}");
                ecb.DestroyEntity(requestEntity);
                continue;
            }

            if (!_chunkMap.TryGetBuilding(destroyRequest.gridPosition, out Entity targetEntity))
            {
                ecb.DestroyEntity(requestEntity);
                continue;
            }

            if (targetEntity == Entity.Null)
            {
                ecb.DestroyEntity(requestEntity);
                continue;
            }
            if (!EntityManager.Exists(targetEntity))
            {
                ecb.DestroyEntity(requestEntity);
                continue;
            }
            if (!EntityManager.HasComponent<BuildingOccupant>(targetEntity))
            {
                ecb.DestroyEntity(requestEntity);
                continue;
            }
            if (EntityManager.HasComponent<IndestructibleBuilding>(targetEntity))
            {
                ecb.DestroyEntity(requestEntity);
                continue;
            }

            int2 anchor = EntityManager
                .GetComponentData<GridPosition>(targetEntity)
                .gridPosition;
            BuildingTypeEnum buildingType = EntityManager
                .GetComponentData<BuildingType>(targetEntity)
                .type;
            bool createRecoveryTasks =
                destroyRequest.cause == BuildingDestroyCauseEnum.UserDemolition;

            if (EntityManager.HasComponent<DroneStation>(targetEntity) &&
                !_droneIdentityConversion
                    .TryRestoreStoredDronesForStationDestruction(
                        targetEntity,
                        anchor,
                        createRecoveryTasks))
            {
                Debug.LogError(
                    $"Drone station destruction was deferred because stored drones could not be restored. Station: {targetEntity}");
                continue;
            }

            RestoreOwnedItems(
                ref ecb,
                targetEntity,
                anchor,
                createRecoveryTasks);
            CreateConstructionMaterialReturns(
                buildingType,
                anchor,
                createRecoveryTasks,
                constructionMaterials);

            if (EntityManager.HasComponent<PowerPole>(targetEntity))
                _powerGrid.TryUnregisterPowerPole(targetEntity);

            _chunkMap.TryUnregisterBuilding(targetEntity);
            ecb.DestroyEntity(targetEntity);

            ecb.DestroyEntity(requestEntity);
        }
    }

    private void RestoreOwnedItems(
        ref EntityCommandBuffer ecb,
        Entity buildingEntity,
        int2 buildingCell,
        bool createRecoveryTasks)
    {
        _restoredItemEntities.Clear();

        if (EntityManager.HasBuffer<StoredItemElement>(buildingEntity))
        {
            DynamicBuffer<StoredItemElement> storedItems =
                EntityManager.GetBuffer<StoredItemElement>(buildingEntity);
            CopyOwnedItemEntities(storedItems);
            _itemStorage.RestoreItems(ref ecb, storedItems, buildingCell);
        }

        if (EntityManager.HasBuffer<ProducedItemElement>(buildingEntity))
        {
            DynamicBuffer<ProducedItemElement> producedItems =
                EntityManager.GetBuffer<ProducedItemElement>(buildingEntity);
            CopyOwnedItemEntities(producedItems);
            _itemStorage.RestoreProducedItems(ref ecb, producedItems, buildingCell);
        }

        if (!createRecoveryTasks)
            return;

        for (int i = 0; i < _restoredItemEntities.Count; i++)
        {
            DroneWorldItemRecoveryTaskUtility.TryCreateRequest(
                EntityManager,
                _restoredItemEntities[i],
                0);
        }
    }

    private void CopyOwnedItemEntities<TElement>(
        DynamicBuffer<TElement> items)
        where TElement : unmanaged, IBufferElementData, IItemStorageElement
    {
        for (int i = 0; i < items.Length; i++)
            _restoredItemEntities.Add(items[i].ItemEntity);
    }

    private void CreateConstructionMaterialReturns(
        BuildingTypeEnum buildingType,
        int2 gridPosition,
        bool createRecoveryTasks,
        NativeArray<ConstructionMaterialConfigElement> materials)
    {
        for (int i = 0; i < materials.Length; i++)
        {
            ConstructionMaterialConfigElement material = materials[i];

            if (material.buildingType != buildingType)
                continue;

            for (int quantity = 0; quantity < material.quantity; quantity++)
            {
                Entity spawnRequest = EntityManager.CreateEntity();
                EntityManager.AddComponentData(spawnRequest, new WorldItemSpawnRequest
                {
                    itemType = material.itemType,
                    gridPosition = gridPosition,
                    createRecoveryTask = createRecoveryTasks
                });
            }
        }
    }
}
