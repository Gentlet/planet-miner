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
    private readonly List<int2> _footprintCells = new();
    private readonly List<BuildingBoundaryConnection> _boundaryConnections = new();
    private readonly List<int2> _outputCells = new();
    private readonly HashSet<Entity> _inputItemDeduplication = new();

    protected override void OnCreate()
    {
        _chunkMap = World.GetExistingSystemManaged<ChunkMapSystem>();
        _itemStorage = World.GetExistingSystemManaged<ItemStorageSystem>();
        _itemTracking = World.GetExistingSystemManaged<ItemTrackingSystem>();
        _storageQuery = GetEntityQuery(
            ComponentType.ReadOnly<Storage>(),
            ComponentType.ReadOnly<GridPosition>(),
            ComponentType.ReadOnly<Direction>(),
            ComponentType.ReadWrite<BuildingOutputCursor>(),
            ComponentType.ReadWrite<StoredItemElement>());
        RequireForUpdate<Storage>();
        RequireForUpdate<ItemStorageLimitElement>();
        RequireForUpdate<BuildingPrefabElement>();
    }

    protected override void OnUpdate()
    {
        if (!EnsureSystems())
            return;

        _itemTracking.ApplyPendingChangesImmediate();

        using NativeArray<ItemStorageLimitElement> storageLimits =
            DynamicBufferCopyUtility.CreateNativeCopy(
                SystemAPI.GetSingletonBuffer<ItemStorageLimitElement>(true),
                Allocator.Temp);
        using NativeArray<BuildingPrefabElement> buildingDefinitions =
            DynamicBufferCopyUtility.CreateNativeCopy(
                SystemAPI.GetSingletonBuffer<BuildingPrefabElement>(true),
                Allocator.Temp);
        int2 storageSize =
            buildingDefinitions.GetFootprintSize(BuildingTypeEnum.Storage);
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
                forward,
                storageSize);
            TryDepositItems(
                storageEntity,
                storageCell,
                forward,
                storageSize,
                capacity,
                storageLimits);
        }
    }

    private void TryOutputOldestItem(
        Entity storageEntity,
        int2 storageCell,
        DirectionEnum forward,
        int2 storageSize)
    {
        DynamicBuffer<StoredItemElement> storedItems =
            EntityManager.GetBuffer<StoredItemElement>(
                storageEntity,
                true);

        if (storedItems.Length == 0)
            return;

        BuildingOutputCursor cursor = EntityManager
            .GetComponentData<BuildingOutputCursor>(storageEntity);
        BuildingBeltConnectionUtility.TryOutputItem<StoredItemElement>(
            _chunkMap,
            EntityManager,
            _itemStorage,
            storageEntity,
            storageCell,
            storageSize,
            forward,
            ref cursor,
            _boundaryConnections,
            _outputCells);
        EntityManager.SetComponentData(storageEntity, cursor);
    }

    private void TryDepositItems(
        Entity storageEntity,
        int2 storageCell,
        DirectionEnum forward,
        int2 storageSize,
        int capacity,
        NativeArray<ItemStorageLimitElement> storageLimits)
    {
        BuildInputItems(storageCell, storageSize, forward);

        for (int i = 0; i < _inputItems.Count; i++)
        {
            InputItem inputItem = _inputItems[i];
            Entity itemEntity = inputItem.entity;
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
                inputItem.sourceCell,
                itemEntity);
        }
    }

    private void BuildInputItems(
        int2 storageCell,
        int2 storageSize,
        DirectionEnum forward)
    {
        _inputItems.Clear();
        _inputItemDeduplication.Clear();
        BuildingFootprintUtility.GetOccupiedCells(
            storageCell,
            storageSize,
            forward,
            _footprintCells);

        for (int cellIndex = 0;
             cellIndex < _footprintCells.Count;
             cellIndex++)
        {
            int2 buildingCell = _footprintCells[cellIndex];
            _chunkMap.GetItems(buildingCell, _itemsInCell);

            for (int itemIndex = 0;
                 itemIndex < _itemsInCell.Count;
                 itemIndex++)
            {
                Entity itemEntity = _itemsInCell[itemIndex];

                if (_inputItemDeduplication.Contains(itemEntity))
                    continue;

                float2 itemPosition = EntityManager
                    .GetComponentData<LocalTransform>(itemEntity)
                    .Position.xy;

                _inputItemDeduplication.Add(itemEntity);
                _inputItems.Add(new InputItem
                {
                    entity = itemEntity,
                    sourceCell = buildingCell,
                    distanceSq = math.distancesq(
                        itemPosition,
                        new float2(
                            buildingCell.x,
                            buildingCell.y))
                });
            }
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
        public int2 sourceCell;
        public float distanceSq;
    }
}
