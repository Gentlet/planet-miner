using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>이번 틱 준비 결과의 범위만 판정한다. 이전 공개 인덱스나 영속 드론 소속을 만들지 않는다.</summary>
public static class DroneTaskNetworkUtility
{
    public static Entity FindWorkerNetwork(NativeArray<DroneStationNetworkDecision> stations, float3 position)
    {
        int2 cell = (int2)math.floor(position.xy);
        for (int i = 0; i < stations.Length; i++)
        {
            if (stations[i].ActivityBounds.Contains(cell))
            {
                return stations[i].RepresentativeStation;
            }
        }
        return Entity.Null;
    }

    public static bool IsCellInNetwork(NativeArray<DroneStationNetworkDecision> stations, Entity network, int2 position)
    {
        for (int i = 0; i < stations.Length; i++)
        {
            if (stations[i].RepresentativeStation == network && stations[i].ActivityBounds.Contains(position))
            {
                return true;
            }
        }
        return false;
    }

    public static bool IsTargetInNetwork(EntityManager manager, NativeArray<DroneStationNetworkDecision> stations,
        Entity network, Entity target)
    {
        int2 position = manager.GetComponentData<GridPosition>(target).Value;
        int2 size = new int2(1, 1);
        if (manager.HasComponent<BuildingType>(target))
        {
            size = BuildingFootprintUtility.GetEffectiveSize(manager.GetComponentData<BuildingFootprint>(target).Size,
                manager.GetComponentData<Direction>(target).dir);
        }
        var bounds = new DroneStationActivityBounds
        {
            MinX = position.x,
            MinY = position.y,
            MaxX = (long)position.x + size.x - 1,
            MaxY = (long)position.y + size.y - 1
        };
        for (int i = 0; i < stations.Length; i++)
        {
            if (stations[i].RepresentativeStation == network && stations[i].ActivityBounds.Overlaps(bounds))
            {
                return true;
            }
        }
        return false;
    }
}
