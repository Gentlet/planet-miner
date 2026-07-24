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
        _spawnRequestQuery = GetEntityQuery(ComponentType.ReadOnly<ItemSpawnRequest>());
        RequireForUpdate<ItemPrefabElement>();
        RequireForUpdate<ItemSpawnRequest>();
    }

    protected override void OnUpdate()
    {
        using NativeArray<ItemPrefabElement> itemPrefabs =
            CopyBuffer(SystemAPI.GetSingletonBuffer<ItemPrefabElement>(true));
        using NativeArray<Entity> requestEntities =
            _spawnRequestQuery.ToEntityArray(Allocator.Temp);

        for (int i = 0; i < requestEntities.Length; i++)
        {
            Entity requestEntity = requestEntities[i];
            ItemSpawnRequest request =
                EntityManager.GetComponentData<ItemSpawnRequest>(requestEntity);

            ProcessRequest(requestEntity, request, itemPrefabs);
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

    private static NativeArray<T> CopyBuffer<T>(DynamicBuffer<T> buffer)
        where T : unmanaged, IBufferElementData
    {
        NativeArray<T> copy = new NativeArray<T>(buffer.Length, Allocator.Temp);

        for (int i = 0; i < buffer.Length; i++)
            copy[i] = buffer[i];

        return copy;
    }
}
