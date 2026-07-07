using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;


public enum DirectionEnum : int
{
    Up,
    Right,
    Down,
    Left,
    Count
}


public struct GridPosition : IComponentData
{
    public int2 gridPosition;
}

public struct Direction : IComponentData
{
    public DirectionEnum dir;

    
}

public struct BuildingOccupant : IComponentData
{

}

public struct BuildingOccupantRequest : IComponentData
{

}
