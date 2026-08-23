using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

[UpdateBefore(typeof(MiningSystem))]
[UpdateBefore(typeof(CrafterSystem))]
public partial class ItemSpawnSystem : SystemBase
{
    private EntityQuery _spawnRequestQuery;

    protected override void OnCreate()
    {
        _spawnRequestQuery = GetEntityQuery(new EntityQueryDesc
        {
            Any = new[]
            {
                ComponentType.ReadOnly<ItemSpawnRequest>(),
                ComponentType.ReadOnly<StartingItemSpawnRequest>()
            }
        });
        RequireForUpdate<ItemPrefabElement>();
        RequireForUpdate(_spawnRequestQuery);
    }

    protected override void OnUpdate()
    {
        using NativeArray<ItemPrefabElement> itemPrefabs =
            DynamicBufferCopyUtility.CreateNativeCopy(
                SystemAPI.GetSingletonBuffer<ItemPrefabElement>(true),
                Allocator.Temp);
        using NativeArray<Entity> requestEntities =
            _spawnRequestQuery.ToEntityArray(Allocator.Temp);

        for (int i = 0; i < requestEntities.Length; i++)
        {
            Entity requestEntity = requestEntities[i];

            if (EntityManager.HasComponent<ItemSpawnRequest>(requestEntity))
            {
                ItemSpawnRequest request = EntityManager
                    .GetComponentData<ItemSpawnRequest>(requestEntity);
                ProcessRequest(requestEntity, request, itemPrefabs);
                continue;
            }

            StartingItemSpawnRequest startingRequest = EntityManager
                .GetComponentData<StartingItemSpawnRequest>(requestEntity);
            ProcessStartingRequest(
                requestEntity,
                startingRequest,
                itemPrefabs);
        }
    }

    private void ProcessStartingRequest(
        Entity requestEntity,
        StartingItemSpawnRequest request,
        NativeArray<ItemPrefabElement> itemPrefabs)
    {
        try
        {
            if (!request.itemType.IsValid())
            {
                Debug.LogError(
                    $"Starting item spawn failed because the item type is invalid. Request: {requestEntity}, Type: {request.itemType}");
                return;
            }

            if (request.owner == Entity.Null)
            {
                Debug.LogError(
                    $"Starting item spawn failed because the owner is null. Request: {requestEntity}, Type: {request.itemType}");
                return;
            }

            if (!EntityManager.Exists(request.owner))
            {
                Debug.LogError(
                    $"Starting item spawn failed because the owner does not exist. Request: {requestEntity}, Owner: {request.owner}, Type: {request.itemType}");
                return;
            }

            if (!EntityManager.HasComponent<GridPosition>(request.owner))
            {
                Debug.LogError(
                    $"Starting item spawn failed because the owner has no grid position. Request: {requestEntity}, Owner: {request.owner}, Type: {request.itemType}");
                return;
            }

            if (!EntityManager.HasBuffer<StoredItemElement>(request.owner))
            {
                Debug.LogError(
                    $"Starting item spawn failed because the owner has no stored-item buffer. Request: {requestEntity}, Owner: {request.owner}, Type: {request.itemType}");
                return;
            }

            Entity itemPrefab = FindPrefab(itemPrefabs, request.itemType);

            if (itemPrefab == Entity.Null || !EntityManager.Exists(itemPrefab))
            {
                Debug.LogError(
                    $"Starting item spawn failed because no prefab was found. Request: {requestEntity}, Type: {request.itemType}");
                return;
            }

            int2 ownerCell = EntityManager
                .GetComponentData<GridPosition>(request.owner)
                .gridPosition;
            Entity itemEntity = SpawnStoredItem(
                itemPrefab,
                request.owner,
                ownerCell,
                request.itemType);
            DynamicBuffer<StoredItemElement> storedItems = EntityManager
                .GetBuffer<StoredItemElement>(request.owner);
            storedItems.Add(new StoredItemElement
            {
                itemEntity = itemEntity,
                type = request.itemType
            });
        }
        catch (Exception exception)
        {
            Debug.LogError(
                $"Starting item spawn failed with an exception. Request: {requestEntity}, Owner: {request.owner}, Type: {request.itemType}\n{exception}");
        }
        finally
        {
            if (EntityManager.Exists(requestEntity))
                EntityManager.DestroyEntity(requestEntity);
        }
    }

    private void ProcessRequest(
        Entity requestEntity,
        ItemSpawnRequest request,
        NativeArray<ItemPrefabElement> itemPrefabs)
    {
        try
        {
            if (!request.itemType.IsValid())
            {
                Debug.LogError(
                    $"Item spawn failed because the item type is invalid. Request: {requestEntity}, Type: {request.itemType}");
                return;
            }

            if (request.owner == Entity.Null ||
                !EntityManager.Exists(request.owner) ||
                !EntityManager.HasComponent<GridPosition>(request.owner) ||
                !EntityManager.HasBuffer<ProducedItemElement>(request.owner))
            {
                Debug.LogError(
                    $"Item spawn failed because the owner is invalid. Request: {requestEntity}, Owner: {request.owner}, Type: {request.itemType}");
                return;
            }

            Entity itemPrefab = FindPrefab(itemPrefabs, request.itemType);

            if (itemPrefab == Entity.Null || !EntityManager.Exists(itemPrefab))
            {
                Debug.LogError(
                    $"Item spawn failed because no prefab was found. Request: {requestEntity}, Type: {request.itemType}");
                return;
            }

            int2 ownerCell =
                EntityManager.GetComponentData<GridPosition>(request.owner).gridPosition;
            Entity itemEntity = SpawnStoredItem(
                itemPrefab,
                request.owner,
                ownerCell,
                request.itemType);

            DynamicBuffer<ProducedItemElement> producedItems =
                EntityManager.GetBuffer<ProducedItemElement>(request.owner);
            producedItems.Add(new ProducedItemElement
            {
                itemEntity = itemEntity,
                type = request.itemType
            });
        }
        catch (Exception exception)
        {
            Debug.LogError(
                $"Item spawn failed with an exception. Request: {requestEntity}, Owner: {request.owner}, Type: {request.itemType}\n{exception}");
        }
        finally
        {
            if (EntityManager.Exists(requestEntity))
                EntityManager.DestroyEntity(requestEntity);
        }
    }

    private Entity SpawnStoredItem(
        Entity itemPrefab,
        Entity owner,
        int2 ownerCell,
        ItemTypeEnum itemType)
    {
        Entity itemEntity = EntityManager.Instantiate(itemPrefab);
        EntityManager.SetComponentData(
            itemEntity,
            LocalTransform.FromPosition(new float3(ownerCell.x, ownerCell.y, 0f)));
        EntityManager.AddComponentData(
            itemEntity,
            new GridPosition { gridPosition = ownerCell });
        EntityManager.AddComponentData(itemEntity, new Item { type = itemType });
        EntityManager.AddComponent<ItemCellChanged>(itemEntity);
        EntityManager.SetComponentEnabled<ItemCellChanged>(itemEntity, false);
        EntityManager.AddComponentData(itemEntity, new StoredItem { owner = owner });
        EntityManager.AddComponent<Disabled>(itemEntity);
        return itemEntity;
    }

    private static Entity FindPrefab(
        NativeArray<ItemPrefabElement> itemPrefabs,
        ItemTypeEnum itemType)
    {
        for (int i = 0; i < itemPrefabs.Length; i++)
        {
            if (itemPrefabs[i].type == itemType)
                return itemPrefabs[i].prefab;
        }

        return Entity.Null;
    }

}
