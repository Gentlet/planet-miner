using Unity.Entities;
using Unity.Mathematics;

/// <summary>이동/인계/격리 입력이 공유하는 목표점 계산이다. 건물은 회전 footprint 중심, 월드 실물은 격자점이다.</summary>
public static class DroneTargetPointUtility
{
    public static bool TryGetTargetPoint(EntityManager manager, Entity entity, out float2 point)
    {
        point = default;
        if (!manager.HasComponent<GridPosition>(entity))
        {
            return false;
        }
        point = manager.GetComponentData<GridPosition>(entity).Value;
        if (manager.HasComponent<BuildingFootprint>(entity) && manager.HasComponent<Direction>(entity))
        {
            point += (float2)manager.GetComponentData<BuildingFootprint>(entity)
                .GetEffectiveSize(manager.GetComponentData<Direction>(entity).dir) * 0.5f;
        }
        return math.all(math.isfinite(point));
    }

    public static bool TryGetTargetPoint(Entity entity, ComponentLookup<GridPosition> positions,
        ComponentLookup<BuildingFootprint> footprints, ComponentLookup<Direction> directions, out float2 point)
    {
        point = default;
        if (!positions.HasComponent(entity))
        {
            return false;
        }
        point = positions[entity].Value;
        if (footprints.HasComponent(entity) && directions.HasComponent(entity))
        {
            point += (float2)footprints[entity].GetEffectiveSize(directions[entity].dir) * 0.5f;
        }
        return math.all(math.isfinite(point));
    }

}
