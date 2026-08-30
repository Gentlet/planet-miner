using Unity.Entities;

public enum WorldTaskMarkerTypeEnum : byte
{
    Demolition,
    ItemRecovery,
    Count
}

public struct WorldTaskMarker : IComponentData
{
    public Entity sourceTask;
    public Entity targetEntity;
    public WorldTaskMarkerTypeEnum type;
}
