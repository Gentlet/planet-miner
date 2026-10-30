using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>확정된 전력 배정 안에서만 충전하고 다음 Decision에 실제 배터리를 전달한다.</summary>
[UpdateInGroup(typeof(DroneExecutionGroup))]
[UpdateAfter(typeof(DroneMovementExecutionSystem))]
public partial struct DroneChargingExecutionSystem : ISystem
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
        state.Dependency = new ChargeDockedDronesJob
        {
            Config = SystemAPI.GetSingleton<DroneChargingConfig>(),
            Maximum = SystemAPI.GetSingleton<DroneBatteryConfig>().MaximumCharge,
            Batteries = SystemAPI.GetComponentLookup<DroneBatteryState>(),
            Activities = SystemAPI.GetComponentLookup<DroneActivityState>()
        }.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    [WithNone(typeof(PendingBuildingDemolition))]
    private partial struct ChargeDockedDronesJob : IJobEntity
    {
        public DroneChargingConfig Config;
        public float Maximum;
        // 정박 관계는 Station Lifecycle만 변경하며, 수행자는 한 정거장에만 속한다.
        // 그 관계는 충전 Execution 이후의 StateApply까지 유지되어 정거장 Job끼리 쓰기가 겹치지 않는다.
        [NativeDisableParallelForRestriction] public ComponentLookup<DroneBatteryState> Batteries;
        [NativeDisableParallelForRestriction] public ComponentLookup<DroneActivityState> Activities;

        private void Execute(in DroneStation station, in PowerSupplyResult supply,
            in DynamicBuffer<DroneChargingPreparationElement> preparations)
        {
            double remainingEnergy = supply.AllocatedEnergy;
            double requestedEnergy = 0d;
            foreach (var preparation in preparations) requestedEnergy += preparation.RequestedEnergy;
            double supplyRatio = requestedEnergy > 0d ? math.min(supply.SupplyRatio, remainingEnergy / requestedEnergy) : 0d;
            foreach (var preparation in preparations)
            {
                var battery = Batteries[preparation.Drone];
                double energyBudget = math.min(remainingEnergy, preparation.RequestedEnergy * supplyRatio);
                double newCharge = math.min(preparation.DesiredCharge, energyBudget * Config.ChargePerSecond / Config.PowerPerDrone);
                float currentChargeBefore = battery.CurrentCharge;
                double fractionalChargeBefore = battery.FractionalCharge;
                DroneBatteryUtility.AddFundedCharge(ref battery, newCharge, Maximum);
                remainingEnergy -= energyBudget;
                bool storedChargeIncreased = (double)battery.CurrentCharge - currentChargeBefore + battery.FractionalCharge - fractionalChargeBefore > 0d;
                Batteries[preparation.Drone] = battery;
                Activities[preparation.Drone] = new DroneActivityState
                {
                    Activity = battery.CurrentCharge >= Maximum ? DroneActivityKind.Idle :
                        storedChargeIncreased ? DroneActivityKind.Charging : DroneActivityKind.WaitingForCharge
                };
            }
        }
    }
}
