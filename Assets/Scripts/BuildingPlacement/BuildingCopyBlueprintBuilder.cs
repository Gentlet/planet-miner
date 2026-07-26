using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;

public static class BuildingCopyBlueprintBuilder
{
    public static BuildingPlacementOperation Create(
        EntityManager entityManager,
        IReadOnlyList<Entity> buildings,
        int2 pivot)
    {
        List<BuildingPlacementCandidate> candidates = new(buildings.Count);

        for (int i = 0; i < buildings.Count; i++)
        {
            Entity building = buildings[i];
            BuildingTypeEnum type =
                entityManager.GetComponentData<BuildingType>(building).type;
            int2 sourcePosition =
                entityManager.GetComponentData<GridPosition>(building).gridPosition;
            DirectionEnum direction =
                entityManager.GetComponentData<Direction>(building).dir;
            ItemTypeEnum selectedItemType = type == BuildingTypeEnum.Crafter
                ? entityManager.GetComponentData<Crafter>(building).selectedItemType
                : ItemTypeEnum.None;

            candidates.Add(new BuildingPlacementCandidate(
                type,
                sourcePosition - pivot,
                direction,
                false,
                selectedItemType));
        }

        candidates.Sort(CompareByGridPosition);
        return new BuildingPlacementOperation(candidates);
    }

    private static int CompareByGridPosition(
        BuildingPlacementCandidate left,
        BuildingPlacementCandidate right)
    {
        int yComparison = left.gridPosition.y.CompareTo(right.gridPosition.y);
        return yComparison != 0
            ? yComparison
            : left.gridPosition.x.CompareTo(right.gridPosition.x);
    }
}
