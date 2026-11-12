using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: 건물 프리팹을 BuildingId로, 아이템과 자원 프리팹을 종류로 조회한다.
/// 입력·출력: 누락/Null이면 false와 Null을 반환한다. 건물 점유 크기는 별도의 BuildingConfig 조회 계약을 따른다.
/// 이용·수명: 생성 시스템과 BuildingLifecycleUtility가 사용하는 순수 조회다. 생존·Prefab/필수 구성 검증은 Initialization이 담당하며 별도 캐시를 만들지 않는다.
/// </summary>
[BurstCompile]
public static class PrefabLookupUtility
{
    public static bool TryGetBuildingPrefab(
        in DynamicBuffer<BuildingPrefabElement> buffer,
        int buildingId,
        out Entity prefab)
    {
        if (buildingId <= 0)
        {
            prefab = Entity.Null;
            return false;
        }

        for (int i = 0; i < buffer.Length; i++)
        {
            var entry = buffer[i];
            if (entry.BuildingId == buildingId)
            {
                prefab = entry.Prefab;
                return prefab != Entity.Null;
            }
        }
        prefab = Entity.Null;
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
