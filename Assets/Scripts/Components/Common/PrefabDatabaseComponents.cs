using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 건물 프리팹 데이터베이스 엔티티 식별용 태그 컴포넌트.
/// </summary>
public struct BuildingPrefabDatabase : IComponentData
{
}

/// <summary>
/// 건물 프리팹 매핑 및 정적 Footprint 규격 버퍼 요소.
/// </summary>
[InternalBufferCapacity(16)]
public struct BuildingPrefabElement : IBufferElementData
{
    public BuildingTypeEnum Type;
    public Entity Prefab;
    public int2 FootprintSize;

    public BuildingPrefabElement(BuildingTypeEnum type, Entity prefab, int2 footprintSize)
    {
        Type = type;
        Prefab = prefab;
        FootprintSize = footprintSize;
    }
}

/// <summary>
/// 아이템 프리팹 데이터베이스 엔티티 식별용 태그 컴포넌트.
/// </summary>
public struct ItemPrefabDatabase : IComponentData
{
}

/// <summary>
/// 아이템 프리팹 매핑 버퍼 요소.
/// </summary>
[InternalBufferCapacity(16)]
public struct ItemPrefabElement : IBufferElementData
{
    public ItemTypeEnum Type;
    public Entity Prefab;

    public ItemPrefabElement(ItemTypeEnum type, Entity prefab)
    {
        Type = type;
        Prefab = prefab;
    }
}

/// <summary>
/// 자원 노드 프리팹 데이터베이스 엔티티 식별용 태그 컴포넌트.
/// </summary>
public struct ResourcePrefabDatabase : IComponentData
{
}

/// <summary>
/// 자원 노드 프리팹 매핑 버퍼 요소.
/// </summary>
[InternalBufferCapacity(8)]
public struct ResourcePrefabElement : IBufferElementData
{
    public ItemTypeEnum ResourceType;
    public Entity Prefab;

    public ResourcePrefabElement(ItemTypeEnum resourceType, Entity prefab)
    {
        ResourceType = resourceType;
        Prefab = prefab;
    }
}

/// <summary>
/// 드론 프리팹 데이터베이스 엔티티 식별용 태그 컴포넌트.
/// </summary>
public struct DronePrefabDatabase : IComponentData
{
}

/// <summary>
/// 드론 엔티티 인스턴스화 시 참조할 단일 드론 프리팹 컴포넌트.
/// </summary>
public struct DronePrefab : IComponentData
{
    public Entity Prefab;

    public DronePrefab(Entity prefab)
    {
        Prefab = prefab;
    }
}

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
