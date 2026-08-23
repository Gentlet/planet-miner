using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

[UpdateAfter(typeof(PowerGridSystem))]
[UpdateAfter(typeof(CoalGeneratorFuelSystem))]
[UpdateBefore(typeof(MiningSystem))]
[UpdateBefore(typeof(CrafterSystem))]
public partial class DroneChargingSystem : SystemBase
{
    private EntityQuery _stationQuery;

    protected override void OnCreate()
    {
        _stationQuery = GetEntityQuery(
            ComponentType.ReadOnly<DroneStation>(),
            ComponentType.ReadOnly<BuildingOccupant>(),
            ComponentType.ReadOnly<PowerConsumer>(),
            ComponentType.ReadOnly<StoredDroneElement>());
        RequireForUpdate<DroneConfig>();
    }

    protected override void OnUpdate()
    {
        DroneConfig config = SystemAPI.GetSingleton<DroneConfig>();
        float deltaTime = SystemAPI.Time.DeltaTime;

        if (deltaTime <= 0f)
            return;

        using NativeArray<Entity> stationEntities =
            _stationQuery.ToEntityArray(Allocator.Temp);

        for (int i = 0; i < stationEntities.Length; i++)
            ChargeStoredDrones(stationEntities[i], config, deltaTime);
    }

    private void ChargeStoredDrones(
        Entity stationEntity,
        DroneConfig config,
        float deltaTime)
    {
        float supplyRatio = EntityManager.GetComponentData<PowerConsumer>(
            stationEntity).supplyRatio;
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

            if (requestedCharge <= 0f)
            {
                CompleteCharging(droneEntity, ref battery);
                continue;
            }

            float actualCharge = DroneChargingUtility.GetActualCharge(
                requestedCharge,
                supplyRatio);

            if (actualCharge <= 0f)
                continue;

            battery.current = math.min(
                battery.maximum,
                battery.current + actualCharge);
            EntityManager.SetComponentData(droneEntity, battery);

            if (battery.current >= battery.maximum)
                CompleteCharging(droneEntity, ref battery);
        }
    }

    private void CompleteCharging(
        Entity droneEntity,
        ref DroneBattery battery)
    {
        battery.current = battery.maximum;
        EntityManager.SetComponentData(droneEntity, battery);
        EntityManager.SetComponentData(
            droneEntity,
            new DroneState { value = DroneStateEnum.Stored });
    }
}
