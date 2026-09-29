using Unity.Entities;

/// <summary>
/// 건물 런타임 설정 싱글톤 엔티티 식별 컴포넌트.
/// </summary>
public struct BuildingRuntimeConfig : IComponentData
{
}

/// <summary>
/// 건물 종류별 기본 런타임 설정 데이터 버퍼 요소.
/// </summary>
[InternalBufferCapacity(8)]
public struct BuildingRuntimeConfigElement : IBufferElementData
{
    public BuildingTypeEnum BuildingType;
    public float Speed;
    public int StorageCapacity;

    public BuildingRuntimeConfigElement(BuildingTypeEnum buildingType, float speed, int storageCapacity = 0)
    {
        BuildingType = buildingType;
        Speed = speed;
        StorageCapacity = storageCapacity;
    }
}
