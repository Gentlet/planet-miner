using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>이전 공개 경계의 정박 관계와 배터리로 충전 필요량만 준비한다.</summary>
[UpdateInGroup(typeof(SimulationPreparationGroup))]
[UpdateBefore(typeof(BuildingPowerDemandPreparationSystem))]
public partial struct DroneChargingPreparationSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<DroneChargingConfig>();
        state.RequireForUpdate<DroneBatteryConfig>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.Dependency = new PrepareChargingJob
        {
            Config = SystemAPI.GetSingleton<DroneChargingConfig>(),
            Maximum = SystemAPI.GetSingleton<DroneBatteryConfig>().MaximumCharge,
            DeltaTime = math.min(SystemAPI.Time.DeltaTime, GameConstants.MaxSimulationDeltaTime),
            Batteries = SystemAPI.GetComponentLookup<DroneBatteryState>(true),
            Disabled = SystemAPI.GetComponentLookup<Disabled>(true)
        }.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    [WithNone(typeof(PendingBuildingDemolition))]
    private partial struct PrepareChargingJob : IJobEntity
    {
        public DroneChargingConfig Config;
        public float Maximum;
        public float DeltaTime;
        [ReadOnly] public ComponentLookup<DroneBatteryState> Batteries;
        [ReadOnly] public ComponentLookup<Disabled> Disabled;

        private void Execute(in DroneStation station, in DynamicBuffer<DockedDroneElement> docks,
            ref DynamicBuffer<DroneChargingPreparationElement> preparations)
        {
            preparations.Clear();
            foreach (var dock in docks)
            {
                if (Disabled.HasComponent(dock.Drone))
                {
                    continue;
                }
                float missing = Maximum - Batteries[dock.Drone].CurrentCharge;
                float desired = math.min(math.max(0f, missing), Config.ChargePerSecond * DeltaTime);
                preparations.Add(new DroneChargingPreparationElement
                {
                    Drone = dock.Drone,
                    DesiredCharge = desired,
                    RequestedEnergy = PowerFrameUtility.RoundEnergyUp(
                        (double)desired * Config.PowerPerDrone / Config.ChargePerSecond)
                });
            }
        }
    }
}
