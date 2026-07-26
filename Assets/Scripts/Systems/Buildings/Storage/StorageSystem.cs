using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateAfter(typeof(BeltMoveSystem))]
[UpdateAfter(typeof(MergerSystem))]
[UpdateBefore(typeof(MiningSystem))]
public partial class StorageSystem : SystemBase
{
    private ChunkMapSystem _chunkMap;
    private ItemStorageSystem _itemStorage;
    private ItemTrackingSystem _itemTracking;
    private EntityQuery _storageQuery;
    private readonly List<Entity> _itemsInCell = new();
    private readonly List<InputItem> _inputItems = new();

    protected override void OnCreate()
    {
        _chunkMap = World.GetExistingSystemManaged<ChunkMapSystem>();
        _itemStorage = World.GetExistingSystemManaged<ItemStorageSystem>();
        _itemTracking = World.GetExistingSystemManaged<ItemTrackingSystem>();
        _storageQuery = GetEntityQuery(
            ComponentType.ReadOnly<Storage>(),
            ComponentType.ReadOnly<GridPosition>(),
            ComponentType.ReadOnly<Direction>(),
            ComponentType.ReadWrite<StoredItemElement>());
        RequireForUpdate<Storage>();
        RequireForUpdate<ItemStorageLimitElement>();
    }

    protected override void OnUpdate()
    {
        if (!EnsureSystems())
            return;

        _itemTracking.ApplyPendingChangesImmediate();

        using NativeArray<ItemStorageLimitElement> storageLimits =
            CopyBuffer(
                SystemAPI.GetSingletonBuffer<ItemStorageLimitElement>(true));
        using NativeArray<Entity> storages =
            _storageQuery.ToEntityArray(Allocator.Temp);

        for (int i = 0; i < storages.Length; i++)
        {
            Entity storageEntity = storages[i];
            int2 storageCell =
                EntityManager.GetComponentData<GridPosition>(storageEntity)
                    .gridPosition;
            DirectionEnum forward =
                EntityManager.GetComponentData<Direction>(storageEntity).dir;
            int capacity =
                EntityManager.GetComponentData<Storage>(storageEntity).capacity;

            TryOutputOldestItem(
                storageEntity,
                storageCell,
                forward);
            TryDepositItems(
                storageEntity,
                storageCell,
                forward,
                capacity,
                storageLimits);
        }
    }

    private void TryOutputOldestItem(
        Entity storageEntity,
        int2 storageCell,
        DirectionEnum forward)
    {
        DynamicBuffer<StoredItemElement> storedItems =
            EntityManager.GetBuffer<StoredItemElement>(
                storageEntity,
                true);

        if (storedItems.Length == 0)
            return;

        int2 outputCell = storageCell + forward.ToInt2();

        if (!_chunkMap.TryGetBelt(outputCell, out _))
            return;

        _itemStorage.TryRestoreItemImmediate<StoredItemElement>(
            storageEntity,
            0,
            outputCell);
    }

    private void TryDepositItems(
        Entity storageEntity,
        int2 storageCell,
        DirectionEnum forward,
        int capacity,
        NativeArray<ItemStorageLimitElement> storageLimits)
    {
        BuildInputItems(storageCell, forward);

        for (int i = 0; i < _inputItems.Count; i++)
        {
            Entity itemEntity = _inputItems[i].entity;
            DynamicBuffer<StoredItemElement> storedItems =
                EntityManager.GetBuffer<StoredItemElement>(
                    storageEntity,
                    true);

            if (!CanDepositItem(
                    itemEntity,
                    storedItems,
                    capacity,
                    storageLimits))
                continue;

            _itemStorage.TryStoreItemImmediate(
                storageEntity,
                storageCell,
                itemEntity);
        }
    }

    private void BuildInputItems(int2 storageCell, DirectionEnum forward)
    {
        _inputItems.Clear();
        _chunkMap.GetItems(storageCell, _itemsInCell);

        float2 cellCenter = new float2(storageCell.x, storageCell.y);
        DirectionEnum back = forward.NextDirection().NextDirection();
        DirectionEnum left = back.NextDirection();
        DirectionEnum right = forward.NextDirection();
        float2 forwardOffset = forward.ToInt2();
        float2 backOffset = back.ToInt2();
        float2 leftOffset = left.ToInt2();
        float2 rightOffset = right.ToInt2();

        for (int i = 0; i < _itemsInCell.Count; i++)
        {
            Entity itemEntity = _itemsInCell[i];
            float3 position =
                EntityManager.GetComponentData<LocalTransform>(itemEntity).Position;
            float2 relativePosition = position.xy - cellCenter;
            float inputScore = math.max(
                math.dot(relativePosition, backOffset),
                math.max(
                    math.dot(relativePosition, leftOffset),
                    math.dot(relativePosition, rightOffset)));

            if (math.dot(relativePosition, forwardOffset) > inputScore)
                continue;

            _inputItems.Add(new InputItem
            {
                entity = itemEntity,
                distanceSq = math.lengthsq(relativePosition)
            });
        }

        _inputItems.Sort(CompareInputItems);
    }

    private bool CanDepositItem(
        Entity itemEntity,
        DynamicBuffer<StoredItemElement> storedItems,
        int capacity,
        NativeArray<ItemStorageLimitElement> storageLimits)
    {
        if (!EntityManager.Exists(itemEntity) ||
            !EntityManager.HasComponent<Item>(itemEntity) ||
            EntityManager.HasComponent<StoredItem>(itemEntity))
            return false;

        if (capacity <= 0)
            return false;

        ItemTypeEnum itemType =
            EntityManager.GetComponentData<Item>(itemEntity).type;
        int stackLimit = storageLimits.GetStorageLimit(itemType);

        if (stackLimit <= 0)
            return false;

        int usedSlotCount = storedItems.GetUsedSlotCount(storageLimits);

        if (usedSlotCount > capacity)
            return false;

        int storedItemCount = storedItems.CountItems(itemType);
        return storedItemCount % stackLimit != 0 ||
               usedSlotCount < capacity;
    }

    private static NativeArray<T> CopyBuffer<T>(
        DynamicBuffer<T> buffer)
        where T : unmanaged, IBufferElementData
    {
        NativeArray<T> copy =
            new NativeArray<T>(buffer.Length, Allocator.Temp);

        for (int i = 0; i < buffer.Length; i++)
            copy[i] = buffer[i];

        return copy;
    }

    private static int CompareInputItems(InputItem first, InputItem second)
    {
        int distanceComparison =
            first.distanceSq.CompareTo(second.distanceSq);

        if (distanceComparison != 0)
            return distanceComparison;

        int indexComparison =
            first.entity.Index.CompareTo(second.entity.Index);
        return indexComparison != 0
            ? indexComparison
            : first.entity.Version.CompareTo(second.entity.Version);
    }

    private bool EnsureSystems()
    {
        if (_chunkMap == null)
            _chunkMap = World.GetExistingSystemManaged<ChunkMapSystem>();

        if (_itemStorage == null)
            _itemStorage = World.GetExistingSystemManaged<ItemStorageSystem>();

        if (_itemTracking == null)
            _itemTracking = World.GetExistingSystemManaged<ItemTrackingSystem>();

        return _chunkMap != null &&
               _itemStorage != null &&
               _itemTracking != null;
    }

    private struct InputItem
    {
        public Entity entity;
        public float distanceSq;
    }
}
