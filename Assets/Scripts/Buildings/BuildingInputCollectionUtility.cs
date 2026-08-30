using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

public readonly struct BuildingInputItem
{
    public BuildingInputItem(
        Entity entity,
        int2 sourceCell,
        float distanceSq)
    {
        Entity = entity;
        SourceCell = sourceCell;
        DistanceSq = distanceSq;
    }

    public Entity Entity { get; }
    public int2 SourceCell { get; }
    public float DistanceSq { get; }
}

public static class BuildingInputCollectionUtility
{
    public static void CollectItems(
        ChunkMapSystem chunkMap,
        EntityManager entityManager,
        int2 anchor,
        int2 size,
        DirectionEnum direction,
        List<int2> footprintCells,
        List<Entity> itemsInCell,
        HashSet<Entity> deduplication,
        List<BuildingInputItem> results)
    {
        results.Clear();
        deduplication.Clear();
        BuildingFootprintUtility.GetOccupiedCells(
            anchor,
            size,
            direction,
            footprintCells);

        for (int cellIndex = 0;
             cellIndex < footprintCells.Count;
             cellIndex++)
        {
            int2 buildingCell = footprintCells[cellIndex];
            chunkMap.GetItems(buildingCell, itemsInCell);

            for (int itemIndex = 0;
                 itemIndex < itemsInCell.Count;
                 itemIndex++)
            {
                Entity itemEntity = itemsInCell[itemIndex];

                if (!deduplication.Add(itemEntity))
                    continue;

                if (!entityManager.Exists(itemEntity))
                    continue;

                if (!entityManager.HasComponent<LocalTransform>(itemEntity))
                    continue;

                float2 itemPosition = entityManager
                    .GetComponentData<LocalTransform>(itemEntity)
                    .Position.xy;
                results.Add(new BuildingInputItem(
                    itemEntity,
                    buildingCell,
                    math.distancesq(
                        itemPosition,
                        new float2(buildingCell.x, buildingCell.y))));
            }
        }
    }

    public static void SortByDistance(List<BuildingInputItem> items)
    {
        items.Sort(CompareByDistance);
    }

    private static int CompareByDistance(
        BuildingInputItem first,
        BuildingInputItem second)
    {
        int distanceComparison =
            first.DistanceSq.CompareTo(second.DistanceSq);

        if (distanceComparison != 0)
            return distanceComparison;

        int indexComparison =
            first.Entity.Index.CompareTo(second.Entity.Index);

        if (indexComparison != 0)
            return indexComparison;

        return first.Entity.Version.CompareTo(second.Entity.Version);
    }
}
