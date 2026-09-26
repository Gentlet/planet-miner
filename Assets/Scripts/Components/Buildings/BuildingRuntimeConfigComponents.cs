using Unity.Burst;
using Unity.Collections;
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

/// <summary>
/// 건물 런타임 설정 조회를 위한 Burst 호환 순수 유틸리티.
/// </summary>
[BurstCompile]
public static class BuildingRuntimeConfigLookupUtility
{
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
}
