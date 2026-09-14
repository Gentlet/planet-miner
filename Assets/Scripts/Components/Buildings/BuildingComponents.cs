using Unity.Entities;
using Unity.Mathematics;

public enum BuildingTypeEnum : byte
{
    Belt,
    Miner,
    Crafter,
    Splitter,
    Merger,
    Storage,
    PowerPole,
    CoalGenerator,
    MainFacility,
    DroneStation,
    ResearchBuilding,
    Count
}

public struct BuildingType : IComponentData
{
    public BuildingTypeEnum type;
}

public struct BuildingSpawnRequest : IComponentData
{
    public BuildingTypeEnum type;
    public int2 gridPosition;
    public DirectionEnum dir;
    public ItemTypeEnum selectedItemType;
}

public struct ConstructionConfig : IComponentData
{
}

public struct ConstructionMaterialConfigElement : IBufferElementData
{
    public BuildingTypeEnum buildingType;
    public ItemTypeEnum itemType;
    public int quantity;
}

public struct ConstructionSiteCreateRequest : IComponentData
{
    public BuildingTypeEnum type;
    public int2 gridPosition;
    public DirectionEnum dir;
    public ItemTypeEnum selectedItemType;
    public int normalPriority;
}

public struct ConstructionSite : IComponentData
{
    public BuildingTypeEnum type;
    public DirectionEnum direction;
    public ItemTypeEnum selectedItemType;
}

public struct ConstructionSiteReservedCellElement : IBufferElementData
{
    public int2 cell;
}

public struct ConstructionMaterialRequirementElement : IBufferElementData
{
    public ItemTypeEnum itemType;
    public int quantity;
}

public struct ConstructionCancelRequest : IComponentData
{
    public int2 gridPosition;
}

public enum BuildingDestroyCauseEnum : byte
{
    External,
    UserDemolition,
    Count
}

public struct BuildingDestroyRequest : IComponentData
{
    public int2 gridPosition;
    public BuildingDestroyCauseEnum cause;
}

public struct BuildingPrefabElement : IBufferElementData
{
    public BuildingTypeEnum type;
    public Entity prefab;
    public int2 size;
}

public struct BuildingOutputCursor : IComponentData
{
    public int2 lastOutputCell;
    public bool hasOutput;
}

public struct IndestructibleBuilding : IComponentData
{
}

public struct ProducedItemElement : IBufferElementData, IItemStorageElement
{
    public Entity itemEntity;
    public ItemTypeEnum type;

    public Entity ItemEntity => itemEntity;
}
