using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

internal struct BuildingInfoPowerGrid
{
    public bool IsConnected;
    public bool HasPowerSample;
    public double MaximumGeneration;
    public double MaximumConsumption;
    public double CurrentGeneration;
    public double CurrentConsumption;

    public static BuildingInfoPowerGrid Read(World world, Entity pole)
    {
        var manager = world.EntityManager;
        if (!manager.HasComponent<PowerGridConnection>(pole) || manager.HasComponent<PendingBuildingDemolition>(pole))
        {
            return default;
        }

        Entity grid = manager.GetComponentData<PowerGridConnection>(pole).Grid;
        if (grid == Entity.Null || !manager.HasComponent<PowerGrid>(grid))
        {
            return default;
        }

        var summary = new BuildingInfoPowerGrid { IsConnected = true };
        using var definitions = manager.CreateEntityQuery(typeof(BuildingConfig), typeof(BuildingConfigElement));
        if (!definitions.HasSingleton<BuildingConfig>())
        {
            return summary;
        }

        var boundary = world.GetExistingSystemManaged<EndPowerEntityCommandBufferSystem>();
        float deltaTime = boundary != null ? boundary.LastCommittedDeltaTime : 0f;
        summary.HasPowerSample = deltaTime > 0f;
        var configs = manager.GetBuffer<BuildingConfigElement>(definitions.GetSingletonEntity(), true);
        using var charging = manager.CreateEntityQuery(typeof(DroneChargingConfig));
        float powerPerDrone = charging.HasSingleton<DroneChargingConfig>() ? charging.GetSingleton<DroneChargingConfig>().PowerPerDrone : 0f;
        using var participants = manager.CreateEntityQuery(new EntityQueryDesc
        {
            All = new[] { ComponentType.ReadOnly<PowerGridConnection>(), ComponentType.ReadOnly<BuildingIdentity>() },
            Any = new[] { ComponentType.ReadOnly<PowerGenerator>(), ComponentType.ReadOnly<PowerConsumer>() },
            None = new[] { ComponentType.ReadOnly<PendingBuildingDemolition>() },
            Options = EntityQueryOptions.IgnoreComponentEnabledState
        });
        using var entities = participants.ToEntityArray(Allocator.Temp);
        foreach (Entity entity in entities)
        {
            if (manager.GetComponentData<PowerGridConnection>(entity).Grid != grid)
            {
                continue;
            }

            int buildingId = manager.GetComponentData<BuildingIdentity>(entity).BuildingId;
            if (!BuildingConfigLookupUtility.TryGetConfig(configs, buildingId, out var config))
            {
                continue;
            }

            if (manager.HasComponent<PowerGenerator>(entity))
            {
                summary.MaximumGeneration += config.Power.Generation.MaximumGeneration;
                if (summary.HasPowerSample && manager.HasComponent<PowerGeneratorSupplyPlan>(entity))
                {
                    summary.CurrentGeneration += (double)manager.GetComponentData<PowerGeneratorSupplyPlan>(entity).AllocatedEnergy / deltaTime;
                }
            }

            if (manager.HasComponent<PowerConsumer>(entity))
            {
                float chargingLimit = 0f;
                if (config.DroneCharging && manager.HasBuffer<DroneChargingPreparationElement>(entity))
                {
                    foreach (var request in manager.GetBuffer<DroneChargingPreparationElement>(entity, true))
                    {
                        if (request.RequestedEnergy > 0f)
                        {
                            chargingLimit += powerPerDrone;
                        }
                    }
                }
                summary.MaximumConsumption += math.max(config.Power.Work.MaximumConsumption, chargingLimit);
                if (summary.HasPowerSample && manager.HasComponent<PowerSupplyResult>(entity))
                {
                    summary.CurrentConsumption += (double)manager.GetComponentData<PowerSupplyResult>(entity).AllocatedEnergy / deltaTime;
                }
            }
        }
        return summary;
    }
}
