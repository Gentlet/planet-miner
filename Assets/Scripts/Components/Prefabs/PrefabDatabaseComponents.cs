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
