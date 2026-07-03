using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

public partial class ResourceSpawnSystem : SystemBase
{
    private EntityQuery _prefabDatabaseQuery;

    protected override void OnCreate()
    {
        _prefabDatabaseQuery = GetEntityQuery(ComponentType.ReadOnly<ResourcePrefabElement>());
        RequireForUpdate<ResourceSpawnRequest>();
    }

    protected override void OnUpdate()
    {
        EntityCommandBuffer ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(World.Unmanaged);
        DynamicBuffer<ResourcePrefabElement> prefabs = default;
        bool hasPrefabDatabase = !_prefabDatabaseQuery.IsEmptyIgnoreFilter;

        if (hasPrefabDatabase)
            prefabs = EntityManager.GetBuffer<ResourcePrefabElement>(_prefabDatabaseQuery.GetSingletonEntity(), true);

        foreach (var (request, requestEntity) in SystemAPI.Query<RefRO<ResourceSpawnRequest>>().WithEntityAccess())
        {
            ResourceSpawnRequest spawn = request.ValueRO;

            if (spawn.type <= ResourceTypeEnum.None || spawn.type >= ResourceTypeEnum.Count || spawn.amount <= 0)
            {
                ecb.DestroyEntity(requestEntity);
                continue;
            }

            Entity prefab = hasPrefabDatabase ? FindPrefab(prefabs, spawn.type) : Entity.Null;
            Entity instance = prefab == Entity.Null ? ecb.CreateEntity() : ecb.Instantiate(prefab);
            LocalTransform transform = LocalTransform.FromPosition(new float3(spawn.gridPosition.x, spawn.gridPosition.y, 0f));

            AddOrSetComponent(ecb, instance, prefab, transform);
            AddOrSetComponent(ecb, instance, prefab, new GridPosition { gridPosition = spawn.gridPosition });
            AddOrSetComponent(ecb, instance, prefab, new ResourceDeposit { type = spawn.type, amount = spawn.amount });

            if (prefab == Entity.Null || !EntityManager.HasComponent<ResourceOccupantRequest>(prefab))
                ecb.AddComponent<ResourceOccupantRequest>(instance);

            ecb.DestroyEntity(requestEntity);
        }
    }

    private void AddOrSetComponent<T>(EntityCommandBuffer ecb, Entity entity, Entity prefab, T component)
        where T : unmanaged, IComponentData
    {
        if (prefab != Entity.Null && EntityManager.HasComponent<T>(prefab))
        {
            ecb.SetComponent(entity, component);
            return;
        }

        ecb.AddComponent(entity, component);
    }

    private static Entity FindPrefab(DynamicBuffer<ResourcePrefabElement> prefabs, ResourceTypeEnum type)
    {
        for (int i = 0; i < prefabs.Length; i++)
        {
            if (prefabs[i].type == type)
                return prefabs[i].prefab;
        }

        return Entity.Null;
    }
}
