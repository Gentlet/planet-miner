using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 월드에 존재하는 건물의 종류를 정의하는 열거형.
/// </summary>
public enum BuildingTypeEnum : byte
{
    None,
    Storage,
    Miner,
    Crafter,
    PowerPole,
    CoalGenerator,
    ResearchBuilding,
    MainFacility,
    DroneStation,
    Splitter,
    Merger,
    Belt,
    ConstructionSite,
    Count
}

/// <summary>
/// 건물의 종류 식별 컴포넌트.
/// </summary>
public struct BuildingType : IComponentData
{
    public BuildingTypeEnum Type;

    public BuildingType(BuildingTypeEnum type)
    {
        Type = type;
    }

    public static implicit operator BuildingTypeEnum(BuildingType b) => b.Type;
    public static implicit operator BuildingType(BuildingTypeEnum type) => new BuildingType(type);
}

/// <summary>
/// 건물이 차지하는 기본 그리드 타일 크기 컴포넌트.
/// </summary>
public struct BuildingFootprint : IComponentData
{
    public int2 Size;

    public BuildingFootprint(int width, int height)
    {
        Size = new int2(width, height);
    }

    public BuildingFootprint(int2 size)
    {
        Size = size;
    }
}

/// <summary>
/// 철거 불가 건물(예: MainFacility)임을 나타내는 태그 컴포넌트.
/// 이 태그가 부착된 건물에 대한 DemolishBuildingRequest는 BuildingDemolitionCommandSystem이 거부한다.
/// </summary>
public struct IndestructibleBuilding : IComponentData
{
}
