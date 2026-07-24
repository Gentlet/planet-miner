using System;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

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

            ecb.SetComponent(instance, LocalTransform.FromPositionRotation(new float3(gridPosition.x, gridPosition.y, 0f), quaternion.RotateZ(Mathf.Deg2Rad * dir.ToDegrees())));
            ecb.AddComponent(instance, new BuildingType { type = request.ValueRO.type });
            ecb.AddComponent(instance, new GridPosition { gridPosition = gridPosition });
            ecb.AddComponent(instance, new Direction { dir = dir });
            ecb.AddComponent(instance, new BuildingOccupantRequest { });

            switch (request.ValueRO.type)
            {
                case BuildingTypeEnum.Belt:
                    ecb.AddComponent(instance, new Belt { speed = 10f });
                    break;
                case BuildingTypeEnum.Miner:
                    ecb.AddComponent(instance, new Miner { speed = 0.1f });
                    ecb.AddBuffer<ProducedItemElement>(instance);
                    break;
                case BuildingTypeEnum.Crafter:
                    ItemTypeEnum selectedItemType = request.ValueRO.selectedItemType;
                    ecb.AddComponent(instance, new Crafter
                    {
                        speed = 1f,
                        selectedItemType = selectedItemType,
                        progress = 0f,
                        state = selectedItemType.IsValid() ? CrafterStateEnum.Idle : CrafterStateEnum.NoRecipe
                    });
                    ecb.AddBuffer<StoredItemElement>(instance);
                    ecb.AddBuffer<ProducedItemElement>(instance);
                    break;
                default:
                    break;
            }


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
