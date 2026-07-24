using Unity.Entities;
using Unity.Mathematics;

[UpdateAfter(typeof(CrafterSystem))]
[UpdateAfter(typeof(MiningSystem))]
public partial class BuildingDestroySystem : SystemBase
{
    private ChunkMapSystem _chunkMap;
    private ItemStorageSystem _itemStorage;

    protected override void OnCreate()
    {
        _chunkMap = World.GetExistingSystemManaged<ChunkMapSystem>();
        _itemStorage = World.GetExistingSystemManaged<ItemStorageSystem>();
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
        if (EntityManager.HasBuffer<StoredItemElement>(buildingEntity))
        {
            DynamicBuffer<StoredItemElement> storedItems =
                EntityManager.GetBuffer<StoredItemElement>(buildingEntity);
            _itemStorage.RestoreItems(ref ecb, storedItems, buildingCell);
        }

        if (EntityManager.HasBuffer<ProducedItemElement>(buildingEntity))
        {
            DynamicBuffer<ProducedItemElement> producedItems =
                EntityManager.GetBuffer<ProducedItemElement>(buildingEntity);
            _itemStorage.RestoreProducedItems(ref ecb, producedItems, buildingCell);
        }
    }
}
