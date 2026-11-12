using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: 건물 통합/호환 설정의 조회와 현장 자재 요구량 초기화를 공유한다.
/// 입력·출력: BuildingId로 통합 정의와 호환 설정을 조회하고 해당 ID의 자재 요구 행을 복사한다.
/// 이용: 배치 검증·공통 건물 생성·건설 자재 초기화에서 사용한다.
/// 수명·소유권: 별도 설정 캐시는 없으며 조회는 읽기 전용, 요구 추가/해금 변경은 전달받은 버퍼에만 적용한다. 버퍼 유효 수명은 호출자가 유지한다.
/// </summary>
[BurstCompile]
public static class BuildingConfigLookupUtility
{
    public static bool TryGetConfig(
        in NativeArray<BuildingConfigElement> configs,
        int buildingId,
        out BuildingConfigElement config)
    {
        if (buildingId <= 0 || !configs.IsCreated)
        {
            config = default;
            return false;
        }
        for (int i = 0; i < configs.Length; i++)
        {
            if (configs[i].BuildingId == buildingId)
            {
                config = configs[i];
                return true;
            }
        }
        config = default;
        return false;
    }

    public static bool TryGetConfig(in DynamicBuffer<BuildingConfigElement> configs, int buildingId, out BuildingConfigElement config)
    {
        return TryGetConfig(configs.AsNativeArray(), buildingId, out config);
    }

    /// <summary>
    /// 호환용 런타임 설정 버퍼에서 지정된 건물의 스펙을 조회.
    /// </summary>
    public static bool TryGetConfig(
        in DynamicBuffer<BuildingRuntimeConfigElement> buffer,
        int buildingId,
        out BuildingRuntimeConfigElement config)
    {
        if (buildingId <= 0)
        {
            config = default;
            return false;
        }
        for (int i = 0; i < buffer.Length; i++)
        {
            if (buffer[i].BuildingId == buildingId)
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
        int buildingId,
        out int2 footprint)
    {
        if (TryGetConfig(buffer, buildingId, out BuildingConfigElement config) &&
            config.Footprint.x > 0 && config.Footprint.y > 0)
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
        int buildingId)
    {
        // 설정에 등록되지 않은 건물은 안전을 위해 미해금으로 처리
        return TryGetConfig(buffer, buildingId, out BuildingConfigElement config) && config.IsUnlocked;
    }

    /// <summary>
    /// 지정 건물 행의 해금 값을 갱신하는 API. 실제 연구 완료를 연결하는 제품 호출자는 아직 없다.
    /// </summary>
    public static bool SetBuildingUnlocked(
        ref DynamicBuffer<BuildingConfigElement> buffer,
        int buildingId,
        bool isUnlocked)
    {
        if (buildingId <= 0)
        {
            return false;
        }
        for (int i = 0; i < buffer.Length; i++)
        {
            if (buffer[i].BuildingId == buildingId)
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
        int buildingId,
        ref DynamicBuffer<ConstructionMaterialRequirementElement> targetRequirements)
    {
        return PopulateRequirements(materialDb.AsNativeArray(), buildingId, ref targetRequirements);
    }

    /// <summary>
    /// 지정된 건물이 현재 연구 해금되어 건설 가능한 상태인지 확인 (NativeArray 오버로드).
    /// </summary>
    public static bool IsBuildingUnlocked(
        in NativeArray<BuildingConfigElement> configs,
        int buildingId)
    {
        return TryGetConfig(configs, buildingId, out var config) && config.IsUnlocked;
    }

    /// <summary>
    /// 특정 건물의 건설에 필요한 자재 목록을 지정된 출력 버퍼에 추가 (NativeArray 오버로드).
    /// </summary>
    public static int PopulateRequirements(
        in NativeArray<BuildingConstructionMaterialElement> materialDb,
        int buildingId,
        ref DynamicBuffer<ConstructionMaterialRequirementElement> targetRequirements)
    {
        if (buildingId <= 0)
        {
            return 0;
        }

        int count = 0;
        for (int i = 0; i < materialDb.Length; i++)
        {
            if (materialDb[i].BuildingId == buildingId)
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
