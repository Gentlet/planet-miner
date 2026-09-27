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

    public BuildingConfigElement(
        BuildingTypeEnum buildingType,
        float speed,
        int storageCapacity,
        bool isUnlocked,
        FixedString32Bytes requiredResearch = default)
    {
        BuildingType = buildingType;
        Speed = speed;
        StorageCapacity = storageCapacity;
        IsUnlocked = isUnlocked;
        RequiredResearch = requiredResearch;
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

/// <summary>
/// BuildingConfig 엔티티 버퍼 조회를 위한 Burst 호환 고속 유틸리티.
/// </summary>
public static class BuildingConfigLookupUtility
{
    /// <summary>
    /// 지정된 건물의 스펙 및 해금 정보를 조회.
    /// </summary>
    public static bool TryGetConfig(
        in DynamicBuffer<BuildingConfigElement> buffer,
        BuildingTypeEnum type,
        out BuildingConfigElement config)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            if (buffer[i].BuildingType == type)
            {
                config = buffer[i];
                return true;
            }
        }

        config = default;
        return false;
    }

    /// <summary>
    /// 지정된 건물이 현재 연구 해금되어 건설 가능한 상태인지 확인.
    /// </summary>
    public static bool IsBuildingUnlocked(
        in DynamicBuffer<BuildingConfigElement> buffer,
        BuildingTypeEnum type)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            if (buffer[i].BuildingType == type)
            {
                return buffer[i].IsUnlocked;
            }
        }

        // 설정에 등록되지 않은 건물은 안전을 위해 미해금으로 처리
        return false;
    }

    /// <summary>
    /// 연구 완료 시 특정 건물의 해금 상태를 갱신.
    /// </summary>
    public static bool SetBuildingUnlocked(
        ref DynamicBuffer<BuildingConfigElement> buffer,
        BuildingTypeEnum type,
        bool isUnlocked)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            if (buffer[i].BuildingType == type)
            {
                var elem = buffer[i];
                elem.IsUnlocked = isUnlocked;
                buffer[i] = elem;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 특정 건물의 건설에 필요한 자재 목록을 지정된 출력 버퍼에 추가.
    /// </summary>
    public static int PopulateRequirements(
        in DynamicBuffer<BuildingConstructionMaterialElement> materialDb,
        BuildingTypeEnum type,
        ref DynamicBuffer<ConstructionMaterialRequirementElement> targetRequirements)
    {
        int count = 0;
        for (int i = 0; i < materialDb.Length; i++)
        {
            if (materialDb[i].BuildingType == type)
            {
                targetRequirements.Add(new ConstructionMaterialRequirementElement(
                    materialDb[i].ItemType,
                    materialDb[i].Quantity,
                    0));
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// 지정된 건물이 현재 연구 해금되어 건설 가능한 상태인지 확인 (NativeArray 오버로드).
    /// </summary>
    public static bool IsBuildingUnlocked(
        in NativeArray<BuildingConfigElement> configs,
        BuildingTypeEnum type)
    {
        for (int i = 0; i < configs.Length; i++)
        {
            if (configs[i].BuildingType == type)
            {
                return configs[i].IsUnlocked;
            }
        }

        return false;
    }

    /// <summary>
    /// 특정 건물의 건설에 필요한 자재 목록을 지정된 출력 버퍼에 추가 (NativeArray 오버로드).
    /// </summary>
    public static int PopulateRequirements(
        in NativeArray<BuildingConstructionMaterialElement> materialDb,
        BuildingTypeEnum type,
        ref DynamicBuffer<ConstructionMaterialRequirementElement> targetRequirements)
    {
        int count = 0;
        for (int i = 0; i < materialDb.Length; i++)
        {
            if (materialDb[i].BuildingType == type)
            {
                targetRequirements.Add(new ConstructionMaterialRequirementElement(
                    materialDb[i].ItemType,
                    materialDb[i].Quantity,
                    0));
                count++;
            }
        }

        return count;
    }
}
