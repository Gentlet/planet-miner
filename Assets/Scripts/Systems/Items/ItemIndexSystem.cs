using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateAfter(typeof(BeltMoveSystem))]
public partial class ItemIndexSystem : SystemBase
{
    private ChunkMapSystem _chunkMap;

    protected override void OnCreate()
    {
        _chunkMap = World.GetExistingSystemManaged<ChunkMapSystem>();
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

        foreach (var (transform, item, entity) in SystemAPI.Query<RefRO<LocalTransform>, RefRO<Item>>().WithNone<ItemPosition>().WithEntityAccess())
        {
            int2 cell = transform.ValueRO.Position.ToGridCell();
            _chunkMap.RegisterItem(cell, entity);
            ecb.AddComponent(entity, new ItemPosition { gridPosition = cell });
        }

        foreach (var (transform, itemPosition, entity) in SystemAPI.Query<RefRO<LocalTransform>, RefRW<ItemPosition>>().WithAll<Item>().WithEntityAccess())
        {
            int2 cell = transform.ValueRO.Position.ToGridCell();

            if (cell.Equals(itemPosition.ValueRO.gridPosition))
                continue;

            _chunkMap.TryUnregisterItem(itemPosition.ValueRO.gridPosition, entity);
            _chunkMap.RegisterItem(cell, entity);
            itemPosition.ValueRW.gridPosition = cell;
        }

        foreach (var (itemPosition, entity) in SystemAPI.Query<RefRO<ItemPosition>>().WithNone<Item>().WithEntityAccess())
        {
            _chunkMap.TryUnregisterItem(itemPosition.ValueRO.gridPosition, entity);
            ecb.RemoveComponent<ItemPosition>(entity);
        }

        foreach (var (item, itemPosition, entity) in SystemAPI.Query<RefRO<Item>, RefRO<ItemPosition>>().WithNone<LocalTransform>().WithEntityAccess())
        {
            _chunkMap.TryUnregisterItem(itemPosition.ValueRO.gridPosition, entity);
            ecb.RemoveComponent<ItemPosition>(entity);
        }
    }
}
