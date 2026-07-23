using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateAfter(typeof(ChunkMapSystem))]
[UpdateBefore(typeof(BeltMoveSystem))]
public partial class ItemTrackingSystem : SystemBase
{
    private ChunkMapSystem _chunkMap;
    private bool _hasInitializeItems;
    private readonly HashSet<int2> _cellsToSort = new();
    private readonly HashSet<Entity> _reportedFailures = new();

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

        foreach (int2 cell in _cellsToSort)
            _chunkMap.SortItemsForBelt(cell);
    }

    private void InitializeItems()
    {
        foreach (var (transform, gridPosition, entity) in
                 SystemAPI.Query<RefRO<LocalTransform>, RefRW<GridPosition>>()
                     .WithAll<Item>()
                     .WithNone<StoredItem>()
                     .WithEntityAccess())
        {
            int2 cell = transform.ValueRO.Position.ToGridCell();
            bool isRegistered = _chunkMap.TryGetRegisteredItemCell(entity, out int2 registeredCell);
            bool registered = isRegistered
                ? registeredCell.Equals(cell)
                : _chunkMap.TryRegisterItem(cell, entity);

            if (!registered)
            {
                ReportFailureOnce(entity, $"Failed to initialize item cell ownership. Entity: {entity}, Cell: {cell}");
                continue;
            }

            gridPosition.ValueRW.gridPosition = cell;
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
        foreach (var (transform, gridPosition, entity) in
                 SystemAPI.Query<RefRO<LocalTransform>, RefRW<GridPosition>>()
                     .WithAll<Item, ItemCellChanged>()
                     .WithNone<StoredItem>()
                     .WithEntityAccess())
        {
            int2 previousCell = gridPosition.ValueRO.gridPosition;
            int2 newCell = transform.ValueRO.Position.ToGridCell();

            if (!TryTransferOwnership(entity, previousCell, newCell))
                continue;

            gridPosition.ValueRW.gridPosition = newCell;
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
}
