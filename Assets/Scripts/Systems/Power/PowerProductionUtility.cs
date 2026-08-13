using Unity.Entities;
using Unity.Mathematics;

public static class PowerProductionUtility
{
    public static float GetProgressDeltaTime(
        EntityManager entityManager,
        Entity buildingEntity,
        float deltaTime)
    {
        if (!entityManager.HasComponent<PowerConsumer>(buildingEntity))
            return deltaTime;

        float supplyRatio = entityManager
            .GetComponentData<PowerConsumer>(buildingEntity)
            .supplyRatio;
        return deltaTime * math.saturate(supplyRatio);
    }
}
