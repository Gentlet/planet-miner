using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 건물 통합 설정 싱글톤 컴포넌트 (Unmanaged).
/// 런타임 스펙, 연구 해금 상태, 건설 자재 요구량을 단일 엔티티에서 일원화하여 관리.
/// </summary>
public struct BuildingConfig : IComponentData
{
}

/// <summary>
/// 건물별 런타임 스펙 및 연구 해금 정보 버퍼 엘리먼트 (Unmanaged).
/// </summary>
public struct BuildingConfigElement : IBufferElementData
{
    public BuildingTypeEnum BuildingType;
    public float Speed;
    public int StorageCapacity;
    public bool IsUnlocked;
    public FixedString32Bytes RequiredResearch;
    public int2 Footprint;

    public BuildingConfigElement(
        BuildingTypeEnum buildingType,
        float speed,
        int storageCapacity,
        bool isUnlocked,
        FixedString32Bytes requiredResearch = default,
        int2 footprint = default)
    {
        BuildingType = buildingType;
        Speed = speed;
        StorageCapacity = storageCapacity;
        IsUnlocked = isUnlocked;
        RequiredResearch = requiredResearch;
        Footprint = footprint.Equals(int2.zero) ? new int2(1, 1) : footprint;
    }
}

/// <summary>
/// 건물별 건설에 필요한 자재 요구량 정의 버퍼 엘리먼트 (Unmanaged).
/// </summary>
public struct BuildingConstructionMaterialElement : IBufferElementData
{
    public BuildingTypeEnum BuildingType;
    public ItemTypeEnum ItemType;
    public int Quantity;

    public BuildingConstructionMaterialElement(BuildingTypeEnum buildingType, ItemTypeEnum itemType, int quantity)
    {
        BuildingType = buildingType;
        ItemType = itemType;
        Quantity = quantity;
    }
}
