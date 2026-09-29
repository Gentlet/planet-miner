using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 프리팹 데이터베이스 버퍼 조회를 위한 Burst 호환 순수 유틸리티.
/// </summary>
[BurstCompile]
public static class PrefabLookupUtility
{
    public static bool TryGetBuildingPrefab(
        in DynamicBuffer<BuildingPrefabElement> buffer,
        BuildingTypeEnum type,
        out Entity prefab,
        out int2 footprintSize)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            if (buffer[i].Type == type)
            {
                prefab = buffer[i].Prefab;
                footprintSize = buffer[i].FootprintSize;
                return prefab != Entity.Null;
            }
        }

        prefab = Entity.Null;
        footprintSize = int2.zero;
        return false;
    }

    public static bool TryGetItemPrefab(
        in DynamicBuffer<ItemPrefabElement> buffer,
        ItemTypeEnum type,
        out Entity prefab)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            if (buffer[i].Type == type)
            {
                prefab = buffer[i].Prefab;
                return prefab != Entity.Null;
            }
        }

        prefab = Entity.Null;
        return false;
    }

    public static bool TryGetResourcePrefab(
        in DynamicBuffer<ResourcePrefabElement> buffer,
        ItemTypeEnum resourceType,
        out Entity prefab)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            if (buffer[i].ResourceType == resourceType)
            {
                prefab = buffer[i].Prefab;
                return prefab != Entity.Null;
            }
        }

        prefab = Entity.Null;
        return false;
    }
}
