using Unity.Mathematics;
using UnityEngine;

public class BuildingPlacementCandidate
{
    public BuildingTypeEnum type;
    public int2 gridPosition;
    public int2 position;
    public DirectionEnum dir;
    public bool canPlace;

    public BuildingPlacementCandidate(
       BuildingTypeEnum type,
       int2 gridPosition,
       int2 position,
       DirectionEnum dir,
       bool canPlace)
    {
        this.type = type;
        this.gridPosition = gridPosition;
        this.position = position;
        this.dir = dir;
        this.canPlace = canPlace;
    }
}
