using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>실제 이동 뒤 출발 여부만 기록한다. 이동/배터리/렌더 Writer는 기존 Movement Execution이다.</summary>
[UpdateInGroup(typeof(DroneExecutionGroup))]
[UpdateAfter(typeof(DroneMovementExecutionSystem))]
public partial struct DroneStationRelocationExecutionSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.Dependency = new ObserveDepartureJob().ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    private partial struct ObserveDepartureJob : IJobEntity
    {
        private void Execute(in DroneWorker worker, in LocalTransform transform, ref DroneStationRelocation relocation)
        {
            if (!relocation.HasDeparted && relocation.Stage == DroneStationRelocationStageEnum.Travelling &&
                math.any(transform.Position.xy != relocation.DeparturePosition)) relocation.HasDeparted = true;
        }
    }
}
