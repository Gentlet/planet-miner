using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;


public struct Belt : IComponentData
{
    public float speed;
}


public struct BeltMoveTarget : IComponentData
{
    public int2 targetCell;
    public float speed;
    public bool isMoving;
}
