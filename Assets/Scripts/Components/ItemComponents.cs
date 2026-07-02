using Unity.Entities;
using Unity.Mathematics;

public struct Item : IComponentData
{
    
}

public struct ItemPosition : ICleanupComponentData
{
    public int2 gridPosition;
}
