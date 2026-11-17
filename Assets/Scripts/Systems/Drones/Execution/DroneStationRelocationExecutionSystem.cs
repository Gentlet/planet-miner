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
        state.Dependency = new ObserveDepartureJob
        {
            Members = SystemAPI.GetBufferLookup<DroneStationRelocationMemberElement>()
        }.Schedule(state.Dependency);
    }

    [BurstCompile]
    private partial struct ObserveDepartureJob : IJobEntity
    {
        public BufferLookup<DroneStationRelocationMemberElement> Members;

        private void Execute(in DroneWorker worker, in LocalTransform transform, in DroneStationRelocation link)
        {
            if (!Members.TryGetBuffer(link.Task, out var roster) || (uint)link.MemberIndex >= (uint)roster.Length) return;
            var relocation = roster[link.MemberIndex];
            if (relocation.Finished || relocation.HasDeparted || relocation.Stage != DroneStationRelocationStageEnum.Travelling)
                return;
            if (math.any(transform.Position.xy != relocation.DeparturePosition))
            {
                relocation.HasDeparted = true;
                roster[link.MemberIndex] = relocation;
            }
        }
    }
}
