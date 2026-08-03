using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

public partial class ChunkMapSystem
{
    public NativeParallelHashMap<int2, Entity>.ReadOnly GetBeltCellsReadOnly()
    {
        return _beltByCell.AsReadOnly();
    }

    public void CopyActiveBeltCells(List<int2> results)
    {
        results.Clear();

        foreach (int2 cell in _activeBeltCells)
            results.Add(cell);
    }

    public bool TryGetBelt(int2 cell, out Entity beltEntity)
    {
        return _beltByCell.TryGetValue(cell, out beltEntity);
    }

    public void SortItemsForBelt(int2 cell)
    {
        if (!TryGetCellData(cell, out ChunkCell cellData) ||
            !_beltByCell.TryGetValue(cell, out Entity beltEntity) ||
            !EntityManager.Exists(beltEntity) ||
            !EntityManager.HasComponent<Direction>(beltEntity))
            return;

        DirectionEnum direction = EntityManager.GetComponentData<Direction>(beltEntity).dir;

        for (int i = 1; i < cellData.Items.Count; i++)
        {
            int currentIndex = i;

            while (currentIndex > 0 && IsItemAhead(cellData.Items[currentIndex], cellData.Items[currentIndex - 1], direction))
            {
                cellData.SwapItems(currentIndex, currentIndex - 1);
                currentIndex--;
            }
        }
    }

    private bool IsItemAhead(Entity item, Entity otherItem, DirectionEnum direction)
    {
        if (!EntityManager.Exists(item) ||
            !EntityManager.Exists(otherItem) ||
            !EntityManager.HasComponent<LocalTransform>(item) ||
            !EntityManager.HasComponent<LocalTransform>(otherItem))
            return false;

        float3 itemPosition = EntityManager.GetComponentData<LocalTransform>(item).Position;
        float3 otherPosition = EntityManager.GetComponentData<LocalTransform>(otherItem).Position;

        switch (direction)
        {
            case DirectionEnum.Right:
                return itemPosition.x > otherPosition.x;
            case DirectionEnum.Left:
                return itemPosition.x < otherPosition.x;
            case DirectionEnum.Up:
                return itemPosition.y > otherPosition.y;
            case DirectionEnum.Down:
                return itemPosition.y < otherPosition.y;
            default:
                return false;
        }
    }
}
