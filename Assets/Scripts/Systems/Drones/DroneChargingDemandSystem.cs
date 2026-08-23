using Unity.Collections;
using Unity.Entities;

[UpdateAfter(typeof(DroneStationStorageSystem))]
[UpdateBefore(typeof(PowerGridSystem))]
public partial class DroneChargingDemandSystem : SystemBase
{
    private EntityQuery _stationQuery;

    protected override void OnCreate()
    {
        _stationQuery = GetEntityQuery(
            ComponentType.ReadOnly<DroneStation>(),
            ComponentType.ReadOnly<BuildingOccupant>(),
            ComponentType.ReadWrite<PowerConsumer>(),
            ComponentType.ReadOnly<StoredDroneElement>());
        RequireForUpdate<DroneConfig>();
    }

    protected override void OnUpdate()
    {
        DroneConfig config = SystemAPI.GetSingleton<DroneConfig>();
        float deltaTime = SystemAPI.Time.DeltaTime;
        using NativeArray<Entity> stationEntities =
            _stationQuery.ToEntityArray(Allocator.Temp);

        for (int i = 0; i < stationEntities.Length; i++)
        {
            UpdateStationDemand(
                stationEntities[i],
                config,
                deltaTime);
        }
    }

    private void UpdateStationDemand(
        Entity stationEntity,
        DroneConfig config,
        float deltaTime)
    {
        PowerConsumer consumer = EntityManager.GetComponentData<PowerConsumer>(
            stationEntity);
        consumer.maximumConsumption = CalculateStationDemand(
            stationEntity,
            config,
            deltaTime);
        EntityManager.SetComponentData(stationEntity, consumer);
    }

    private float CalculateStationDemand(
        Entity stationEntity,
        DroneConfig config,
        float deltaTime)
    {
        float maximumChargePerFrame = config.chargingSpeed * deltaTime;

        if (maximumChargePerFrame <= 0f)
            return 0f;

        float demand = 0f;
        DynamicBuffer<StoredDroneElement> storedDrones = EntityManager
            .GetBuffer<StoredDroneElement>(stationEntity, true);

        for (int i = 0; i < storedDrones.Length; i++)
        {
            Entity droneEntity = storedDrones[i].droneEntity;

            if (!DroneChargingUtility.TryGetStoredChargingDrone(
                    EntityManager,
                    stationEntity,
                    droneEntity,
                    out DroneBattery battery))
                continue;

            float requestedCharge = DroneChargingUtility.GetRequestedCharge(
                battery,
                config.chargingSpeed,
                deltaTime);
            demand += DroneChargingUtility.GetPowerDemand(
                requestedCharge,
                maximumChargePerFrame,
                config.chargingPowerConsumptionPerDrone);
        }

        return demand;
    }
}
