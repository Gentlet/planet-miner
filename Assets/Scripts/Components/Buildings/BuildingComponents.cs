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

    /// <summary>
    /// 건물 회전 방향에 따른 유효 점유 크기를 반환합니다.
    /// Left / Right (90도 회전)인 경우 가로와 세로가 스왑됩니다.
    /// </summary>
    public int2 GetEffectiveSize(DirectionEnum direction)
    {
        int2 normalized = math.max(Size, new int2(1, 1));
        return (direction == DirectionEnum.Left || direction == DirectionEnum.Right)
            ? new int2(normalized.y, normalized.x)
            : normalized;
    }
}
