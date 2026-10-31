using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>마지막 확정 위치/배터리/수행자 상태를 다음 Decision의 관측으로 한 번 준비한다.</summary>
[UpdateInGroup(typeof(SimulationPreparationGroup))]
public partial struct DroneWorkerObservationPreparationSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<DroneMovementConfig>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.Dependency = new PrepareObservationsJob
        {
            Docks = state.GetComponentLookup<DroneDocked>(true),
            Relocations = state.GetComponentLookup<DroneStationRelocation>(true)
        }.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    private partial struct PrepareObservationsJob : IJobEntity
    {
        [Unity.Collections.ReadOnly] public ComponentLookup<DroneDocked> Docks;
        [Unity.Collections.ReadOnly] public ComponentLookup<DroneStationRelocation> Relocations;

        private void Execute(Entity entity, in DroneWorker worker, in LocalTransform transform,
            in DroneBatteryState battery, in DroneWorkerAssignment link, in DroneMovementState movement,
            in DroneCargoState cargo, in DynamicBuffer<StoredItemElement> items, ref DroneWorkerObservation observation)
        {
            bool canAccept = battery.CurrentCharge > 0f && link.Assignment == Entity.Null &&
                (movement.IsNormalReturn || (movement.ChargingStation == Entity.Null && movement.ReturnStation == Entity.Null)) &&
                cargo.Origin == DroneCargoOriginEnum.None && items.IsEmpty && !Docks.HasComponent(entity) && !Relocations.HasComponent(entity);
            if (math.any(observation.Position != transform.Position) || observation.CanAcceptTask != canAccept)
            {
                // 정지 중 매 틱 revision을 바꾸면 아직 평가 중인 같은 경로가 계속 무효화된다.
                observation.Revision = observation.Revision == uint.MaxValue ? 1u : observation.Revision + 1u;
                observation.Position = transform.Position;
                observation.CanAcceptTask = canAccept;
            }
        }
    }
}
