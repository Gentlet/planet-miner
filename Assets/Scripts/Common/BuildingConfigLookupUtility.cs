using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 건물 통합 설정과 호환용 런타임 설정 버퍼를 조회하는 Burst 호환 유틸리티.
/// </summary>
[BurstCompile]
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
    /// 호환용 런타임 설정 버퍼에서 지정된 건물의 스펙을 조회.
    /// </summary>
    public static bool TryGetConfig(
        in DynamicBuffer<BuildingRuntimeConfigElement> buffer,
        BuildingTypeEnum type,
        out BuildingRuntimeConfigElement config)
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
    /// 지정된 건물의 기본 Footprint(크기)를 조회.
    /// </summary>
    public static bool TryGetFootprint(
        in DynamicBuffer<BuildingConfigElement> buffer,
        BuildingTypeEnum type,
        out int2 footprint)
    {
        if (TryGetConfig(buffer, type, out BuildingConfigElement config))
        {
            footprint = config.Footprint;
            return true;
        }

        footprint = int2.zero;
        return false;
    }

    /// <summary>
    /// 지정된 건물이 현재 연구 해금되어 건설 가능한 상태인지 확인.
    /// </summary>
    public static bool IsBuildingUnlocked(
        in DynamicBuffer<BuildingConfigElement> buffer,
        BuildingTypeEnum type)
    {
        // 설정에 등록되지 않은 건물은 안전을 위해 미해금으로 처리
        return TryGetConfig(buffer, type, out BuildingConfigElement config) && config.IsUnlocked;
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
