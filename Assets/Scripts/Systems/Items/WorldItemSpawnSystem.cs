using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

[UpdateAfter(typeof(BuildingDestroySystem))]
public partial class WorldItemSpawnSystem : SystemBase
{
    private const int MaximumSpawnSearchRadius = 16;

    private EntityQuery _requestQuery;
    private ItemTrackingSystem _itemTracking;

    protected override void OnCreate()
    {
        _requestQuery = GetEntityQuery(
            ComponentType.ReadOnly<WorldItemSpawnRequest>());
        _itemTracking = World.GetOrCreateSystemManaged<ItemTrackingSystem>();
        RequireForUpdate<ItemPrefabElement>();
    }

    protected override void OnUpdate()
    {
        using NativeArray<ItemPrefabElement> prefabs =
            DynamicBufferCopyUtility.CreateNativeCopy(
                SystemAPI.GetSingletonBuffer<ItemPrefabElement>(true),
                Allocator.Temp);
        using NativeArray<Entity> requests =
            _requestQuery.ToEntityArray(Allocator.Temp);

        for (int i = 0; i < requests.Length; i++)
            ProcessRequest(requests[i], prefabs);
    }

    private void ProcessRequest(
        Entity requestEntity,
        NativeArray<ItemPrefabElement> prefabs)
    {
        WorldItemSpawnRequest request = EntityManager
            .GetComponentData<WorldItemSpawnRequest>(requestEntity);

        if (!request.itemType.IsValid())
        {
            Debug.LogError(
                $"World item spawn request has an invalid item type. Type : {request.itemType}");
            EntityManager.DestroyEntity(requestEntity);
            return;
        }

        Entity prefab = FindPrefab(prefabs, request.itemType);

        if (prefab == Entity.Null)
        {
            Debug.LogError(
                $"World item spawn request has no matching prefab. Type : {request.itemType}");
            return;
        }

        Entity itemEntity = EntityManager.Instantiate(prefab);
        AddWorldItemComponents(itemEntity, request);

        if (!TryRegisterNear(itemEntity, request.gridPosition))
        {
            EntityManager.DestroyEntity(itemEntity);
            return;
        }

        if (request.createRecoveryTask)
        {
            DroneWorldItemRecoveryTaskUtility.TryCreateRequest(
                EntityManager,
                itemEntity,
                0);
        }

        EntityManager.DestroyEntity(requestEntity);
    }

    private void AddWorldItemComponents(
        Entity itemEntity,
        WorldItemSpawnRequest request)
    {
        EntityManager.SetComponentData(
            itemEntity,
            LocalTransform.FromPosition(new float3(
                request.gridPosition.x,
                request.gridPosition.y,
                0f)));
        EntityManager.AddComponentData(itemEntity, new GridPosition
        {
            gridPosition = request.gridPosition
        });
        EntityManager.AddComponentData(
            itemEntity,
            new Item { type = request.itemType });
        EntityManager.AddComponent<ItemCellChanged>(itemEntity);
        EntityManager.SetComponentEnabled<ItemCellChanged>(itemEntity, false);
    }

    private bool TryRegisterNear(Entity itemEntity, int2 originCell)
    {
        for (int radius = 0; radius <= MaximumSpawnSearchRadius; radius++)
        {
            for (int y = -radius; y <= radius; y++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    if (math.max(math.abs(x), math.abs(y)) != radius)
                        continue;

                    int2 targetCell = originCell + new int2(x, y);
                    float3 targetPosition = new(
                        targetCell.x,
                        targetCell.y,
                        0f);

                    if (_itemTracking.TryRegisterItemImmediate(
                            itemEntity,
                            targetCell,
                            targetPosition))
                        return true;
                }
            }
        }

        return false;
    }

    private static Entity FindPrefab(
        NativeArray<ItemPrefabElement> prefabs,
        ItemTypeEnum itemType)
    {
        for (int i = 0; i < prefabs.Length; i++)
        {
            if (prefabs[i].type == itemType)
                return prefabs[i].prefab;
        }

        return Entity.Null;
    }
}
