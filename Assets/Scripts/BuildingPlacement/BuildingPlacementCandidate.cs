using Unity.Mathematics;
using UnityEngine;

public class BuildingPlacementCandidate
{
    public BuildingTypeEnum type;
    public int2 gridPosition;
    public int2 position;
    public int2 size;
    public DirectionEnum dir;
    public bool canPlace;
    public ItemTypeEnum selectedItemType;

    public BuildingPlacementCandidate(
       BuildingTypeEnum type,
       int2 gridPosition,
       DirectionEnum dir,
       bool canPlace,
       ItemTypeEnum selectedItemType = ItemTypeEnum.None)
    {
        this.type = type;
        this.gridPosition = gridPosition;
        size = new int2(1, 1);
        this.dir = dir;
        this.canPlace = canPlace;
        this.selectedItemType = selectedItemType;
    }
}
