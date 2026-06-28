using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
partial struct BuildingSpawnSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<BuildingPrefabElement>();
        state.RequireForUpdate<BuildingSpawnRequest>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        DynamicBuffer<BuildingPrefabElement> prefabs = SystemAPI.GetSingletonBuffer<BuildingPrefabElement>(true);
        EntityCommandBuffer ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);

        foreach(var (request, requestEntity) in SystemAPI.Query<RefRO<BuildingSpawnRequest>>().WithEntityAccess())
        {
            Entity prefab = FindPrefab(prefabs, request.ValueRO.type);

            if(prefab == Entity.Null)
            {
                ecb.DestroyEntity(requestEntity);
                continue;
            }

            Entity instance = ecb.Instantiate(prefab);
            int2 gridPosition = request.ValueRO.gridPosition;
            DirectionEnum dir = request.ValueRO.dir;

            ecb.SetComponent(instance, LocalTransform.FromPositionRotation(new float3(gridPosition.x, gridPosition.y, 0f), quaternion.RotateZ(dir.ToDegrees())));
            ecb.AddComponent(instance, new BuildingType { type = request.ValueRO.type });
            ecb.AddComponent(instance, new GridPosition { gridPosition = gridPosition });
            ecb.AddComponent(instance, new Direction { dir = dir });
            ecb.AddComponent(instance, new GridOccupantRequest { });


            ecb.DestroyEntity(requestEntity);
        }
    }

    private static Entity FindPrefab(DynamicBuffer<BuildingPrefabElement> prefabs, BuildingTypeEnum type)
    {
        for(int i = 0; i < prefabs.Length; i++)
        {
            if (prefabs[i].type == type)
                return prefabs[i].prefab;
        }

        return Entity.Null;
    }
}
