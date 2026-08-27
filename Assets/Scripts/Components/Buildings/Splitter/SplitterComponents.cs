using Unity.Entities;

public struct Splitter : IComponentData
{
    public DirectionEnum nextOutputDirection;
    public Entity inputBelt;
}

public struct SplitterRetainedItemElement : IBufferElementData
{
    public Entity itemEntity;
}
