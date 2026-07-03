using Unity.Entities;
using UnityEngine;

public partial class EntityCountDebugSystem : SystemBase
{
    private const double logIntervalSeconds = 1.0;

    private EntityQuery _allEntities;
    private EntityQuery _resources;
    private EntityQuery _chunkLoadRequests;
    private EntityQuery _buildings;
    private EntityQuery _items;
    private double _nextLogTime;

    protected override void OnCreate()
    {
        _allEntities = EntityManager.UniversalQuery;
        _resources = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<ResourceDeposit>());
        _chunkLoadRequests = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<ChunkLoadRequest>());
        _buildings = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<BuildingType>());
        _items = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<Item>());
    }

    protected override void OnUpdate()
    {
        double elapsedTime = SystemAPI.Time.ElapsedTime;

        if (elapsedTime < _nextLogTime)
            return;

        _nextLogTime = elapsedTime + logIntervalSeconds;

        Debug.Log(
            $"Entities: {_allEntities.CalculateEntityCount()}, " +
            $"Resources: {_resources.CalculateEntityCount()}, " +
            $"ChunkLoadRequests: {_chunkLoadRequests.CalculateEntityCount()}, " +
            $"Buildings: {_buildings.CalculateEntityCount()}, " +
            $"Items: {_items.CalculateEntityCount()}");
    }
}
