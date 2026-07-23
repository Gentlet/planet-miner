using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

public partial class ItemStorageSystem : SystemBase
{
    private ChunkMapSystem _chunkMap;

    protected override void OnCreate()
    {
        _chunkMap = World.GetExistingSystemManaged<ChunkMapSystem>();
    }

    protected override void OnUpdate()
    {
    }

    public bool TryStoreItem(
        ref EntityCommandBuffer ecb,
        DynamicBuffer<StoredItemElement> storedItems,
        Entity owner,
        int2 sourceCell,
        Entity itemEntity)
    {
        if (_chunkMap == null)
        {
            _chunkMap = World.GetExistingSystemManaged<ChunkMapSystem>();
            if (_chunkMap == null)
                return false;
        }

        if (itemEntity == Entity.Null ||
            !EntityManager.Exists(itemEntity) ||
            EntityManager.HasComponent<StoredItem>(itemEntity) ||
            EntityManager.HasComponent<Disabled>(itemEntity) ||
            !EntityManager.HasComponent<Item>(itemEntity) ||
            !EntityManager.HasComponent<LocalTransform>(itemEntity) ||
            !EntityManager.HasComponent<GridPosition>(itemEntity) ||
            !EntityManager.HasComponent<ItemCellChanged>(itemEntity))
            return false;

        if (!_chunkMap.TryUnregisterItem(sourceCell, itemEntity))
            return false;

        storedItems.Add(new StoredItemElement
        {
            itemEntity = itemEntity,
            type = EntityManager.GetComponentData<Item>(itemEntity).type
        });

        LocalTransform transform = EntityManager.GetComponentData<LocalTransform>(itemEntity);
        transform.Position = new float3(sourceCell.x, sourceCell.y, transform.Position.z);
        ecb.SetComponent(itemEntity, transform);
        ecb.SetComponent(itemEntity, new GridPosition { gridPosition = sourceCell });
        ecb.AddComponent(itemEntity, new StoredItem { owner = owner });
        ecb.AddComponent<Disabled>(itemEntity);
        return true;
    }

    public void RestoreItems(
        ref EntityCommandBuffer ecb,
        DynamicBuffer<StoredItemElement> storedItems,
        int2 targetCell)
    {
        for (int i = 0; i < storedItems.Length; i++)
        {
            Entity itemEntity = storedItems[i].itemEntity;
            LocalTransform transform = EntityManager.GetComponentData<LocalTransform>(itemEntity);
            transform.Position = new float3(targetCell.x, targetCell.y, transform.Position.z);

            ecb.SetComponent(itemEntity, transform);
            ecb.SetComponent(itemEntity, new GridPosition { gridPosition = targetCell });
            ecb.RemoveComponent<StoredItem>(itemEntity);
            ecb.RemoveComponent<Disabled>(itemEntity);
            ecb.SetComponentEnabled<ItemCellChanged>(itemEntity, true);
        }

        storedItems.Clear();
    }

    public void DestroyStoredItem(
        ref EntityCommandBuffer ecb,
        DynamicBuffer<StoredItemElement> storedItems,
        int index)
    {
        ecb.DestroyEntity(storedItems[index].itemEntity);
        storedItems.RemoveAt(index);
    }
}
