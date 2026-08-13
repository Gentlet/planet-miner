using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

public partial class ItemStorageSystem : SystemBase
{
    private ChunkMapSystem _chunkMap;
    private ItemTrackingSystem _itemTracking;

    protected override void OnCreate()
    {
        _chunkMap = World.GetExistingSystemManaged<ChunkMapSystem>();
        _itemTracking = World.GetExistingSystemManaged<ItemTrackingSystem>();
    }

    protected override void OnUpdate()
    {
    }

    public bool TryStoreItemImmediate(
        Entity owner,
        int2 sourceCell,
        Entity itemEntity)
    {
        if (!EnsureChunkMap())
            return false;

        if (itemEntity == Entity.Null ||
            owner == Entity.Null ||
            !EntityManager.Exists(itemEntity) ||
            !EntityManager.Exists(owner) ||
            !EntityManager.HasBuffer<StoredItemElement>(owner) ||
            EntityManager.HasComponent<StoredItem>(itemEntity) ||
            EntityManager.HasComponent<Disabled>(itemEntity) ||
            !EntityManager.HasComponent<Item>(itemEntity) ||
            !EntityManager.HasComponent<LocalTransform>(itemEntity) ||
            !EntityManager.HasComponent<GridPosition>(itemEntity) ||
            !EntityManager.HasComponent<ItemCellChanged>(itemEntity))
            return false;

        if (!_chunkMap.TryUnregisterItem(sourceCell, itemEntity))
            return false;

        LocalTransform transform = EntityManager.GetComponentData<LocalTransform>(itemEntity);
        transform.Position = new float3(sourceCell.x, sourceCell.y, transform.Position.z);
        EntityManager.SetComponentData(itemEntity, transform);
        EntityManager.SetComponentData(itemEntity, new GridPosition { gridPosition = sourceCell });
        EntityManager.SetComponentEnabled<ItemCellChanged>(itemEntity, false);
        EntityManager.AddComponentData(itemEntity, new StoredItem { owner = owner });
        EntityManager.AddComponent<Disabled>(itemEntity);

        DynamicBuffer<StoredItemElement> storedItems =
            EntityManager.GetBuffer<StoredItemElement>(owner);
        storedItems.Add(new StoredItemElement
        {
            itemEntity = itemEntity,
            type = EntityManager.GetComponentData<Item>(itemEntity).type
        });
        return true;
    }

    public bool TryRestoreItemImmediate<TElement>(
        Entity owner,
        int index,
        int2 targetCell)
        where TElement : unmanaged, IBufferElementData, IItemStorageElement
    {
        if (!EnsureItemTracking() ||
            owner == Entity.Null ||
            !EntityManager.Exists(owner) ||
            !EntityManager.HasBuffer<TElement>(owner))
            return false;

        DynamicBuffer<TElement> items =
            EntityManager.GetBuffer<TElement>(owner);

        if (index < 0 || index >= items.Length)
            return false;

        TElement itemElement = items[index];
        Entity itemEntity = itemElement.ItemEntity;

        if (itemEntity == Entity.Null ||
            !EntityManager.Exists(itemEntity) ||
            !EntityManager.HasComponent<StoredItem>(itemEntity))
        {
            items.RemoveAt(index);
            return false;
        }

        StoredItem storedItem =
            EntityManager.GetComponentData<StoredItem>(itemEntity);

        if (storedItem.owner != owner)
            return false;

        LocalTransform previousTransform =
            EntityManager.GetComponentData<LocalTransform>(itemEntity);
        GridPosition previousGridPosition =
            EntityManager.GetComponentData<GridPosition>(itemEntity);
        float3 targetPosition =
            new float3(targetCell.x, targetCell.y, previousTransform.Position.z);

        if (!_itemTracking.CanPlaceItemAt(
                itemEntity,
                targetCell,
                targetPosition))
            return false;

        items.RemoveAt(index);
        EntityManager.RemoveComponent<StoredItem>(itemEntity);
        EntityManager.RemoveComponent<Disabled>(itemEntity);

        if (_itemTracking.TryRegisterItemImmediate(
                itemEntity,
                targetCell,
                targetPosition))
            return true;

        EntityManager.SetComponentData(itemEntity, previousTransform);
        EntityManager.SetComponentData(itemEntity, previousGridPosition);
        EntityManager.AddComponentData(itemEntity, storedItem);
        EntityManager.AddComponent<Disabled>(itemEntity);

        DynamicBuffer<TElement> restoredItems =
            EntityManager.GetBuffer<TElement>(owner);
        restoredItems.Insert(
            math.min(index, restoredItems.Length),
            itemElement);
        return false;
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
            ecb.SetComponent(itemEntity, new LocalToWorld
            {
                Value = transform.ToMatrix()
            });
            ecb.SetComponent(itemEntity, new GridPosition { gridPosition = targetCell });
            ecb.RemoveComponent<StoredItem>(itemEntity);
            ecb.RemoveComponent<Disabled>(itemEntity);
            ecb.SetComponentEnabled<ItemCellChanged>(itemEntity, true);
        }

        storedItems.Clear();
    }

    public void RestoreProducedItems(
        ref EntityCommandBuffer ecb,
        DynamicBuffer<ProducedItemElement> producedItems,
        int2 targetCell)
    {
        for (int i = 0; i < producedItems.Length; i++)
        {
            Entity itemEntity = producedItems[i].itemEntity;
            LocalTransform transform = EntityManager.GetComponentData<LocalTransform>(itemEntity);
            transform.Position = new float3(targetCell.x, targetCell.y, transform.Position.z);

            ecb.SetComponent(itemEntity, transform);
            ecb.SetComponent(itemEntity, new LocalToWorld
            {
                Value = transform.ToMatrix()
            });
            ecb.SetComponent(itemEntity, new GridPosition { gridPosition = targetCell });
            ecb.RemoveComponent<StoredItem>(itemEntity);
            ecb.RemoveComponent<Disabled>(itemEntity);
            ecb.SetComponentEnabled<ItemCellChanged>(itemEntity, true);
        }

        producedItems.Clear();
    }

    public void DestroyStoredItem(
        ref EntityCommandBuffer ecb,
        DynamicBuffer<StoredItemElement> storedItems,
        int index)
    {
        ecb.DestroyEntity(storedItems[index].itemEntity);
        storedItems.RemoveAt(index);
    }

    public int GetStoredItemCount(Entity owner, ItemTypeEnum itemType)
    {
        if (owner == Entity.Null)
            return 0;

        if (!EntityManager.Exists(owner))
            return 0;

        if (!EntityManager.HasBuffer<StoredItemElement>(owner))
            return 0;

        DynamicBuffer<StoredItemElement> storedItems =
            EntityManager.GetBuffer<StoredItemElement>(owner, true);
        int count = 0;

        for (int i = 0; i < storedItems.Length; i++)
        {
            if (storedItems[i].type == itemType)
                count++;
        }

        return count;
    }

    public bool TryConsumeStoredItem(
        ref EntityCommandBuffer ecb,
        Entity owner,
        ItemTypeEnum itemType)
    {
        if (owner == Entity.Null)
            return false;

        if (!EntityManager.Exists(owner))
            return false;

        if (!EntityManager.HasBuffer<StoredItemElement>(owner))
            return false;

        DynamicBuffer<StoredItemElement> storedItems =
            EntityManager.GetBuffer<StoredItemElement>(owner);

        for (int i = 0; i < storedItems.Length; i++)
        {
            if (storedItems[i].type != itemType)
                continue;

            DestroyStoredItem(ref ecb, storedItems, i);
            return true;
        }

        return false;
    }

    private bool EnsureChunkMap()
    {
        if (_chunkMap == null)
            _chunkMap = World.GetExistingSystemManaged<ChunkMapSystem>();

        return _chunkMap != null;
    }

    private bool EnsureItemTracking()
    {
        if (_itemTracking == null)
            _itemTracking = World.GetExistingSystemManaged<ItemTrackingSystem>();

        return _itemTracking != null;
    }
}
