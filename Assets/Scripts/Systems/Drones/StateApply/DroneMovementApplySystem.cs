using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

/// <summary>도착 결과만 기존 배정 상태에 연결한다. 아이템 인계/완료량/예약 정산은 기존 소유자에 남긴다.</summary>
[UpdateInGroup(typeof(DroneStateApplyGroup))]
[UpdateAfter(typeof(DroneTaskLifecycleApplySystem))]
[UpdateBefore(typeof(DroneTaskAssignmentPublishSystem))]
public partial struct DroneMovementApplySystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        var end = state.World.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>();
        var handle = new ApplyArrivalsJob
        {
            Assignments = state.GetComponentLookup<DroneTaskAssignment>(true),
            ECB = end.CreateCommandBuffer().AsParallelWriter()
        }.ScheduleParallel(state.Dependency);
        state.Dependency = handle;
        end.AddJobHandleForProducer(handle);
    }

    [BurstCompile]
    private partial struct ApplyArrivalsJob : IJobEntity
    {
        [ReadOnly] public ComponentLookup<DroneTaskAssignment> Assignments;
        public EntityCommandBuffer.ParallelWriter ECB;

        private void Execute([EntityIndexInQuery] int sortKey, in DroneWorker worker,
            in DroneWorkerAssignment link, in DroneMovementResult result)
        {
            if (!result.Arrived || result.Failure != DroneMovementFailureEnum.None)
            {
                return;
            }
            if (link.Assignment != result.Assignment)
            {
                return;
            }
            if (!Assignments.HasComponent(result.Assignment))
            {
                return;
            }
            var assignment = Assignments[result.Assignment];
            if (assignment.Revision != result.AssignmentRevision || assignment.NextAction != result.Action)
            {
                return;
            }
            if (assignment.State == DroneTaskAssignmentStateEnum.MovingToSource)
            {
                assignment.State = DroneTaskAssignmentStateEnum.AwaitingCollection;
            }
            else if (assignment.State == DroneTaskAssignmentStateEnum.MovingToDestination)
            {
                assignment.State = DroneTaskAssignmentStateEnum.AwaitingDelivery;
            }
            else
            {
                return;
            }
            ECB.SetComponent(sortKey, result.Assignment, assignment);
        }
    }
}
