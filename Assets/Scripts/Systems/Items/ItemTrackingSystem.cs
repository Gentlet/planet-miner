using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateAfter(typeof(ChunkMapSystem))]
[UpdateBefore(typeof(BeltMoveSystem))]
public partial class ItemTrackingSystem : SystemBase
{
    private ChunkMapSystem _chunkMap;
    private bool _hasInitializeItems;
    private EntityQuery _itemsQuery;
    private EntityQuery _changedItemsQuery;
    private readonly HashSet<int2> _cellsToSort = new();
    private readonly HashSet<Entity> _reportedFailures = new();
    private readonly List<Entity> _itemsInCell = new();

    protected override void OnCreate()
    {
        _chunkMap = World.GetExistingSystemManaged<ChunkMapSystem>();
        _itemsQuery = GetEntityQuery(new EntityQueryDesc
        {
            All = new[]
            {
                ComponentType.ReadOnly<Item>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadWrite<GridPosition>()
            },
            None = new[] { ComponentType.ReadOnly<StoredItem>() }
        });
        _changedItemsQuery = GetEntityQuery(new EntityQueryDesc
        {
            All = new[]
            {
                ComponentType.ReadOnly<Item>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadWrite<GridPosition>(),
                ComponentType.ReadOnly<ItemCellChanged>()
            },
            None = new[] { ComponentType.ReadOnly<StoredItem>() }
        });
    }

    protected override void OnUpdate()
    {
        if (!EnsureChunkMap())
            return;

        _cellsToSort.Clear();

        if (!_hasInitializeItems)
        {
            InitializeItems();
            _hasInitializeItems = true;
        }
        else
        {
            TrackChangedItems();
        }

        SortChangedCells();
    }

    public void ApplyPendingChangesImmediate()
    {
        if (!EnsureChunkMap())
            return;

        Dependency.Complete();
        _cellsToSort.Clear();

        if (!_hasInitializeItems)
        {
            InitializeItems();
            _hasInitializeItems = true;
        }
        else
        {
            TrackChangedItems();
        }

        SortChangedCells();
    }

    public bool CanPlaceItemAt(Entity itemEntity, int2 targetCell, float3 targetPosition)
    {
        if (!EnsureChunkMap())
            return false;

        float spacingSq = GameConstants.itemSpacing * GameConstants.itemSpacing;
        _chunkMap.GetItems(targetCell, _itemsInCell);

        for (int i = 0; i < _itemsInCell.Count; i++)
        {
            Entity otherItem = _itemsInCell[i];

            if (otherItem == itemEntity ||
                !EntityManager.Exists(otherItem) ||
                !EntityManager.HasComponent<LocalTransform>(otherItem))
                continue;

            float3 otherPosition = EntityManager.GetComponentData<LocalTransform>(otherItem).Position;

            if (math.distancesq(targetPosition.xy, otherPosition.xy) < spacingSq)
                return false;
        }

        return true;
    }

    public bool TryMoveItemImmediate(Entity itemEntity, int2 targetCell, float3 targetPosition)
    {
        if (!IsValidWorldItem(itemEntity) ||
            !_chunkMap.TryGetRegisteredItemCell(itemEntity, out int2 sourceCell) ||
            !CanPlaceItemAt(itemEntity, targetCell, targetPosition))
            return false;

        LocalTransform previousTransform = EntityManager.GetComponentData<LocalTransform>(itemEntity);
        GridPosition previousGridPosition = EntityManager.GetComponentData<GridPosition>(itemEntity);

        if (sourceCell.Equals(targetCell))
        {
            previousTransform.Position = targetPosition;
            EntityManager.SetComponentData(itemEntity, previousTransform);
            EntityManager.SetComponentData(itemEntity, new GridPosition { gridPosition = targetCell });
            EntityManager.SetComponentEnabled<ItemCellChanged>(itemEntity, false);
            _chunkMap.SortItemsForBelt(targetCell);
            return true;
        }

        if (!_chunkMap.TryUnregisterItem(sourceCell, itemEntity))
            return false;

        LocalTransform targetTransform = previousTransform;
        targetTransform.Position = targetPosition;
        EntityManager.SetComponentData(itemEntity, targetTransform);
        EntityManager.SetComponentData(itemEntity, new GridPosition { gridPosition = targetCell });

        if (_chunkMap.TryRegisterItem(targetCell, itemEntity))
        {
            EntityManager.SetComponentEnabled<ItemCellChanged>(itemEntity, false);
            _chunkMap.SortItemsForBelt(targetCell);
            _reportedFailures.Remove(itemEntity);
            return true;
        }

        EntityManager.SetComponentData(itemEntity, previousTransform);
        EntityManager.SetComponentData(itemEntity, previousGridPosition);
        bool restored = _chunkMap.TryRegisterItem(sourceCell, itemEntity);
        ReportFailureOnce(itemEntity,
            $"Failed immediate item move. Entity: {itemEntity}, Target: {targetCell}, Source Restored: {restored}");
        return false;
    }

    public bool TryRegisterItemImmediate(Entity itemEntity, int2 targetCell, float3 targetPosition)
    {
        if (!IsValidWorldItem(itemEntity) ||
            _chunkMap.TryGetRegisteredItemCell(itemEntity, out _))
            return false;

        LocalTransform previousTransform = EntityManager.GetComponentData<LocalTransform>(itemEntity);
        GridPosition previousGridPosition = EntityManager.GetComponentData<GridPosition>(itemEntity);
        LocalTransform targetTransform = previousTransform;
        targetTransform.Position = targetPosition;

        EntityManager.SetComponentData(itemEntity, targetTransform);
        EntityManager.SetComponentData(itemEntity, new GridPosition { gridPosition = targetCell });

        if (_chunkMap.TryRegisterItem(targetCell, itemEntity))
        {
            EntityManager.SetComponentEnabled<ItemCellChanged>(itemEntity, false);
            _chunkMap.SortItemsForBelt(targetCell);
            _reportedFailures.Remove(itemEntity);
            return true;
        }

        EntityManager.SetComponentData(itemEntity, previousTransform);
        EntityManager.SetComponentData(itemEntity, previousGridPosition);
        ReportFailureOnce(itemEntity,
            $"Failed immediate item registration. Entity: {itemEntity}, Target: {targetCell}");
        return false;
    }

    private void InitializeItems()
    {
        using NativeArray<Entity> items = _itemsQuery.ToEntityArray(Allocator.Temp);

        for (int i = 0; i < items.Length; i++)
        {
            Entity entity = items[i];
            LocalTransform transform = EntityManager.GetComponentData<LocalTransform>(entity);
            int2 cell = transform.Position.ToGridCell();
            bool isRegistered = _chunkMap.TryGetRegisteredItemCell(entity, out int2 registeredCell);
            bool registered = isRegistered
                ? registeredCell.Equals(cell)
                : _chunkMap.TryRegisterItem(cell, entity);

            if (!registered)
            {
                ReportFailureOnce(entity, $"Failed to initialize item cell ownership. Entity: {entity}, Cell: {cell}");
                continue;
            }

            EntityManager.SetComponentData(entity, new GridPosition { gridPosition = cell });
            _cellsToSort.Add(cell);

            if (EntityManager.HasComponent<ItemCellChanged>(entity))
                EntityManager.SetComponentEnabled<ItemCellChanged>(entity, false);
            else
                ReportFailureOnce(entity, $"Item is missing ItemCellChanged. Entity: {entity}");

            _reportedFailures.Remove(entity);
        }
    }

    private void TrackChangedItems()
    {
        using NativeArray<Entity> changedItems = _changedItemsQuery.ToEntityArray(Allocator.Temp);

        for (int i = 0; i < changedItems.Length; i++)
        {
            Entity entity = changedItems[i];
            LocalTransform transform = EntityManager.GetComponentData<LocalTransform>(entity);
            GridPosition gridPosition = EntityManager.GetComponentData<GridPosition>(entity);
            int2 previousCell = gridPosition.gridPosition;
            int2 newCell = transform.Position.ToGridCell();

            if (!TryTransferOwnership(entity, previousCell, newCell))
                continue;

            EntityManager.SetComponentData(entity, new GridPosition { gridPosition = newCell });
            _cellsToSort.Add(newCell);
            EntityManager.SetComponentEnabled<ItemCellChanged>(entity, false);
            _reportedFailures.Remove(entity);
        }
    }

    private bool TryTransferOwnership(Entity entity, int2 previousCell, int2 newCell)
    {
        if (!_chunkMap.TryGetRegisteredItemCell(entity, out int2 registeredCell))
        {
            if (_chunkMap.TryRegisterItem(newCell, entity))
                return true;

            ReportFailureOnce(entity, $"Failed to register an untracked item. Entity: {entity}, Cell: {newCell}");
            return false;
        }

        if (!registeredCell.Equals(previousCell))
        {
            ReportFailureOnce(entity,
                $"Item ownership does not match GridPosition. Entity: {entity}, Owner: {registeredCell}, GridPosition: {previousCell}");
            return false;
        }

        if (previousCell.Equals(newCell))
            return true;

        if (!_chunkMap.TryUnregisterItem(previousCell, entity))
        {
            ReportFailureOnce(entity, $"Failed to remove item from its previous cell. Entity: {entity}, Cell: {previousCell}");
            return false;
        }

        if (_chunkMap.TryRegisterItem(newCell, entity))
            return true;

        bool restored = _chunkMap.TryRegisterItem(previousCell, entity);
        ReportFailureOnce(entity,
            $"Failed to register item in its new cell. Entity: {entity}, New Cell: {newCell}, Previous Ownership Restored: {restored}");
        return false;
    }

    private void ReportFailureOnce(Entity entity, string message)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (_reportedFailures.Add(entity))
            UnityEngine.Debug.LogWarning(message);
#endif
    }

    private bool EnsureChunkMap()
    {
        if (_chunkMap != null)
            return true;

        _chunkMap = World.GetExistingSystemManaged<ChunkMapSystem>();
        return _chunkMap != null;
    }

    private bool IsValidWorldItem(Entity itemEntity)
    {
        return EnsureChunkMap() &&
               itemEntity != Entity.Null &&
               EntityManager.Exists(itemEntity) &&
               !EntityManager.HasComponent<StoredItem>(itemEntity) &&
               !EntityManager.HasComponent<Disabled>(itemEntity) &&
               EntityManager.HasComponent<Item>(itemEntity) &&
               EntityManager.HasComponent<LocalTransform>(itemEntity) &&
               EntityManager.HasComponent<GridPosition>(itemEntity) &&
               EntityManager.HasComponent<ItemCellChanged>(itemEntity);
    }

    private void SortChangedCells()
    {
        foreach (int2 cell in _cellsToSort)
            _chunkMap.SortItemsForBelt(cell);
    }
}
