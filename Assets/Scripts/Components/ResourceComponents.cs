using Unity.Entities;
using Unity.Mathematics;

public enum ResourceTypeEnum : byte
{
    None,
    Iron,
    Copper,
    Coal,
    Stone,
    Count
}

public struct ResourceDeposit : IComponentData
{
    public ResourceTypeEnum type;
    public int amount;
}

public struct ResourceSpawnRequest : IComponentData
{
    public ResourceTypeEnum type;
    public int2 gridPosition;
    public int amount;
}

public struct ResourceOccupant : IComponentData
{
}

public struct ResourceOccupantRequest : IComponentData
{
}

public struct ResourcePrefabElement : IBufferElementData
{
    public ResourceTypeEnum type;
    public Entity prefab;
}

public struct ResourceChunkGenerationSettings : IComponentData
{
    public uint worldSeed;
}

public struct ResourceGenerationConfigElement : IBufferElementData
{
    public ResourceTypeEnum type;
    public float weight;
    public int minPatchRadius;
    public int maxPatchRadius;
    public float cellFillChance;
    public int minAmount;
    public int maxAmount;
}
