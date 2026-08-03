using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

public partial class ChunkMapSystem
{
    public int GetItemCount(int2 cell)
    {
        if (!TryGetCellData(cell, out ChunkCell cellData))
            return 0;

        return cellData.Items.Count;
    }

    public void GetItems(int2 cell, List<Entity> results)
    {
        results.Clear();

        if (!TryGetCellData(cell, out ChunkCell cellData))
            return;

        for (int i = 0; i < cellData.Items.Count; i++)
            results.Add(cellData.Items[i]);
    }

    public bool TryGetRegisteredItemCell(Entity item, out int2 cell)
    {
        return _itemCellByEntity.TryGetValue(item, out cell);
    }

    public bool TryRegisterItem(int2 cell, Entity item)
    {
        if (item == Entity.Null ||
            !EntityManager.Exists(item) ||
            !EntityManager.HasComponent<Item>(item) ||
            !EntityManager.HasComponent<LocalTransform>(item) ||
            !EntityManager.HasComponent<GridPosition>(item) ||
            !EntityManager.HasComponent<ItemCellChanged>(item) ||
            _itemCellByEntity.ContainsKey(item))
            return false;

        ChunkCell cellData = GetOrCreateCellData(cell);
        if (!cellData.TryAddItem(item))
            return false;

        _itemCellByEntity.Add(item, cell);

        if (_beltByCell.ContainsKey(cell))
            _activeBeltCells.Add(cell);

        return true;
    }

    public bool TryUnregisterItem(int2 cell, Entity item)
    {
        if (!_itemCellByEntity.TryGetValue(item, out int2 registeredCell) ||
            !registeredCell.Equals(cell) ||
            !TryGetCellData(cell, out ChunkCell cellData) ||
            !cellData.RemoveItem(item))
            return false;

        _itemCellByEntity.Remove(item);

        if (cellData.Items.Count == 0)
            _activeBeltCells.Remove(cell);

        return true;
    }
}
