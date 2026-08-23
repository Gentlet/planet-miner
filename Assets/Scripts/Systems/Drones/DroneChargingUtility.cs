using Unity.Entities;
using Unity.Mathematics;

public static class DroneChargingUtility
{
    public static bool TryGetStoredChargingDrone(
        EntityManager entityManager,
        Entity stationEntity,
        Entity droneEntity,
        out DroneBattery battery)
    {
        battery = default;

        if (droneEntity == Entity.Null)
            return false;

        if (!entityManager.Exists(droneEntity))
            return false;

        if (!entityManager.HasComponent<StoredDrone>(droneEntity))
            return false;

        StoredDrone storedDrone = entityManager.GetComponentData<StoredDrone>(
            droneEntity);

        if (storedDrone.stationEntity != stationEntity)
            return false;

        if (!entityManager.HasComponent<DroneBattery>(droneEntity))
            return false;

        if (!entityManager.HasComponent<DroneState>(droneEntity))
            return false;

        DroneState state = entityManager.GetComponentData<DroneState>(
            droneEntity);

        if (state.value != DroneStateEnum.AwaitingCharge)
            return false;

        battery = entityManager.GetComponentData<DroneBattery>(droneEntity);
        return true;
    }

    public static float GetRequestedCharge(
        DroneBattery battery,
        float chargingSpeed,
        float deltaTime)
    {
        if (chargingSpeed <= 0f)
            return 0f;

        if (deltaTime <= 0f)
            return 0f;

        float missingCharge = math.max(
            0f,
            battery.maximum - battery.current);
        return math.min(missingCharge, chargingSpeed * deltaTime);
    }

    public static float GetPowerDemand(
        float requestedCharge,
        float maximumChargePerFrame,
        float maximumPowerConsumption)
    {
        if (requestedCharge <= 0f)
            return 0f;

        if (maximumChargePerFrame <= 0f)
            return 0f;

        if (maximumPowerConsumption <= 0f)
            return 0f;

        return maximumPowerConsumption *
               math.saturate(requestedCharge / maximumChargePerFrame);
    }

    public static float GetActualCharge(
        float requestedCharge,
        float supplyRatio)
    {
        return requestedCharge * math.saturate(supplyRatio);
    }
}
