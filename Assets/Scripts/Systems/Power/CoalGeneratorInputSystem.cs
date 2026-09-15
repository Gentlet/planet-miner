using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

[UpdateAfter(typeof(StorageSystem))]
[UpdateBefore(typeof(PowerGridSystem))]
public partial class CoalGeneratorInputSystem : SystemBase
{
    private ChunkMapSystem _chunkMap;
    private ItemStorageSystem _itemStorage;
    private ItemTrackingSystem _itemTracking;
    private EntityQuery _generatorQuery;
    private readonly List<Entity> _itemsInCell = new();
    private readonly List<int2> _footprintCells = new();
    private readonly List<BuildingInputItem> _inputItems = new();
    private readonly HashSet<Entity> _inputItemDeduplication = new();

    protected override void OnCreate()
    {
        _chunkMap = World.GetExistingSystemManaged<ChunkMapSystem>();
        _itemStorage = World.GetExistingSystemManaged<ItemStorageSystem>();
        _itemTracking = World.GetExistingSystemManaged<ItemTrackingSystem>();
        _generatorQuery = GetEntityQuery(
            ComponentType.ReadOnly<CoalGenerator>(),
            ComponentType.ReadOnly<GridPosition>(),
            ComponentType.ReadOnly<BuildingFootprint>(),
            ComponentType.ReadOnly<Direction>(),
            ComponentType.ReadOnly<StoredItemElement>());
        RequireForUpdate<CoalGenerator>();
        RequireForUpdate<CoalGeneratorConfig>();
    }

    protected override void OnUpdate()
    {
        if (!EnsureSystems())
            return;

        _itemTracking.ApplyPendingChangesImmediate();

        CoalGeneratorConfig config =
            SystemAPI.GetSingleton<CoalGeneratorConfig>();
        using NativeArray<Entity> generators =
            _generatorQuery.ToEntityArray(Allocator.Temp);

        for (int i = 0; i < generators.Length; i++)
        {
            Entity generatorEntity = generators[i];
            int storedCoalCount = _itemStorage.GetStoredItemCount(
                generatorEntity,
                ItemTypeEnum.Coal);

            if (storedCoalCount >= config.fuelStorageCapacity)
                continue;

            int2 anchor = EntityManager
                .GetComponentData<GridPosition>(generatorEntity)
                .gridPosition;
            DirectionEnum forward = EntityManager
                .GetComponentData<Direction>(generatorEntity)
                .dir;
            int2 footprintSize = EntityManager
                .GetComponentData<BuildingFootprint>(generatorEntity)
                .size;
            BuildInputItems(anchor, footprintSize, forward);

            for (int itemIndex = 0;
                 itemIndex < _inputItems.Count;
                 itemIndex++)
            {
                if (storedCoalCount >= config.fuelStorageCapacity)
                    break;

                BuildingInputItem inputItem = _inputItems[itemIndex];

                if (!CanStoreCoal(inputItem.Entity))
                    continue;

                if (!_itemStorage.TryStoreItemImmediate(
                        generatorEntity,
                        inputItem.SourceCell,
                        inputItem.Entity))
                    continue;

                storedCoalCount++;
            }
        }
    }

    private void BuildInputItems(
        int2 anchor,
        int2 size,
        DirectionEnum forward)
    {
        BuildingInputCollectionUtility.CollectItems(
            _chunkMap,
            EntityManager,
            anchor,
            size,
            forward,
            _footprintCells,
            _itemsInCell,
            _inputItemDeduplication,
            _inputItems);
        BuildingInputCollectionUtility.SortByDistance(_inputItems);
    }

    private bool CanStoreCoal(Entity itemEntity)
    {
        if (!EntityManager.Exists(itemEntity))
            return false;

        if (!EntityManager.HasComponent<Item>(itemEntity))
            return false;

        if (EntityManager.HasComponent<StoredItem>(itemEntity))
            return false;

        return EntityManager.GetComponentData<Item>(itemEntity).type ==
               ItemTypeEnum.Coal;
    }

    private bool EnsureSystems()
    {
        if (_chunkMap == null)
            _chunkMap = World.GetExistingSystemManaged<ChunkMapSystem>();

        if (_itemStorage == null)
            _itemStorage = World.GetExistingSystemManaged<ItemStorageSystem>();

        if (_itemTracking == null)
            _itemTracking = World.GetExistingSystemManaged<ItemTrackingSystem>();

        if (_chunkMap == null)
            return false;

        if (_itemStorage == null)
            return false;

        return _itemTracking != null;
    }
}
