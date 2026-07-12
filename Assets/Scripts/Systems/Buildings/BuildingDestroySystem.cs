using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateBefore(typeof(BeltMoveSystem))]
public partial class BuildingDestroySystem : SystemBase
{
    private ChunkMapSystem _chunkMap;

    protected override void OnCreate()
    {
        _chunkMap = World.GetExistingSystemManaged<ChunkMapSystem>();
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

        EntityCommandBuffer ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(World.Unmanaged);

        foreach (var (request, requestEntity) in SystemAPI.Query<RefRO<BuildingDestroyRequest>>().WithEntityAccess())
        {
            if (!_chunkMap.TryGetBuilding(request.ValueRO.gridPosition, out Entity targetEntity))
            {
                ecb.DestroyEntity(requestEntity);
                continue;
            }

            if (targetEntity != Entity.Null &&
                EntityManager.Exists(targetEntity) &&
                EntityManager.HasComponent<BuildingOccupant>(targetEntity))
            {
                RestoreItems(ref ecb, targetEntity, request.ValueRO.gridPosition);
                _chunkMap.TryUnregisterBuilding(request.ValueRO.gridPosition, targetEntity);
                ecb.DestroyEntity(targetEntity);
            }

            ecb.DestroyEntity(requestEntity);
        }
    }

    private void RestoreItems(ref EntityCommandBuffer ecb, Entity buildingEntity, int2 buildingCell)
    {
        if (!EntityManager.HasBuffer<CrafterDepositedItemElement>(buildingEntity))
            return;

        DynamicBuffer<CrafterDepositedItemElement> depositedItems = EntityManager.GetBuffer<CrafterDepositedItemElement>(buildingEntity);

        for (int i = 0; i < depositedItems.Length; i++)
        {
            Entity itemEntity = depositedItems[i].itemEntity;

            if (itemEntity == Entity.Null || !EntityManager.Exists(itemEntity))
                continue;

            if (EntityManager.HasComponent<LocalTransform>(itemEntity))
            {
                LocalTransform transform = EntityManager.GetComponentData<LocalTransform>(itemEntity);
                transform.Position = new float3(buildingCell.x, buildingCell.y, transform.Position.z);
                ecb.SetComponent(itemEntity, transform);
            }

            if (EntityManager.HasComponent<GridPosition>(itemEntity))
                ecb.SetComponent(itemEntity, new GridPosition { gridPosition = buildingCell });
            else
                ecb.AddComponent(itemEntity, new GridPosition { gridPosition = buildingCell });

            if (EntityManager.HasComponent<DepositedItem>(itemEntity))
                ecb.RemoveComponent<DepositedItem>(itemEntity);

            if (EntityManager.HasComponent<Disabled>(itemEntity))
                ecb.RemoveComponent<Disabled>(itemEntity);

            _chunkMap.RegisterItem(buildingCell, itemEntity);
        }
    }
}
