using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: 게시된 건물·아이템·자원 프리팹 DB 버퍼에서 종류가 일치하는 첫 참조를 조회한다.
/// 입력·출력: 누락/Null이면 false와 Null을 반환한다. 건물 조회는 방향 적용 전 기본 footprint도 반환한다.
/// 이용·수명: 생성 시스템과 BuildingLifecycleUtility가 사용하는 순수 조회다. 생존·Prefab/필수 구성 검증은 Initialization이 담당하며 별도 캐시를 만들지 않는다.
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
