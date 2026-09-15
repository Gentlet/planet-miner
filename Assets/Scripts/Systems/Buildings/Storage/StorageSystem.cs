using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

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
    private readonly List<BuildingInputItem> _inputItems = new();
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
            ComponentType.ReadOnly<BuildingFootprint>(),
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
            DynamicBufferCopyUtility.CreateNativeCopy(
                SystemAPI.GetSingletonBuffer<ItemStorageLimitElement>(true),
                Allocator.Temp);
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
            int2 storageSize = EntityManager
                .GetComponentData<BuildingFootprint>(storageEntity)
                .size;

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
        if (!EntityManager.HasComponent<BuildingOutputCursor>(storageEntity))
            return;

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
            BuildingInputItem inputItem = _inputItems[i];
            Entity itemEntity = inputItem.Entity;
            DynamicBuffer<StoredItemElement> storedItems =
                EntityManager.GetBuffer<StoredItemElement>(
                    storageEntity,
                    true);

            if (!CanDepositItem(
                    storageEntity,
                    itemEntity,
                    storedItems,
                    capacity,
                    storageLimits))
                continue;

            _itemStorage.TryStoreItemImmediate(
                storageEntity,
                inputItem.SourceCell,
                itemEntity);
        }
    }

    private void BuildInputItems(
        int2 storageCell,
        int2 storageSize,
        DirectionEnum forward)
    {
        BuildingInputCollectionUtility.CollectItems(
            _chunkMap,
            EntityManager,
            storageCell,
            storageSize,
            forward,
            _footprintCells,
            _itemsInCell,
            _inputItemDeduplication,
            _inputItems);
        BuildingInputCollectionUtility.SortByDistance(_inputItems);
    }

    private bool CanDepositItem(
        Entity storageEntity,
        Entity itemEntity,
        DynamicBuffer<StoredItemElement> storedItems,
        int capacity,
        NativeArray<ItemStorageLimitElement> storageLimits)
    {
        if (!EntityManager.Exists(itemEntity))
            return false;

        if (!EntityManager.HasComponent<Item>(itemEntity))
            return false;

        if (EntityManager.HasComponent<StoredItem>(itemEntity))
            return false;

        ItemTypeEnum itemType =
            EntityManager.GetComponentData<Item>(itemEntity).type;
        bool hasReservedCapacity = EntityManager
            .HasBuffer<DroneReservedStorageCapacityElement>(storageEntity);
        DynamicBuffer<DroneReservedStorageCapacityElement> reservedCapacity =
            hasReservedCapacity
                ? EntityManager.GetBuffer<DroneReservedStorageCapacityElement>(
                    storageEntity,
                    true)
                : default;

        return StorageCapacityUtility.CanStoreAdditionalItems(
            storedItems,
            reservedCapacity,
            hasReservedCapacity,
            capacity,
            storageLimits,
            itemType,
            1,
            DroneStationStorageUtility.GetStoredDroneCount(
                EntityManager,
                storageEntity),
            DroneStationStorageUtility.GetDedicatedDroneSlotCapacity(
                EntityManager,
                storageEntity));
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
}
