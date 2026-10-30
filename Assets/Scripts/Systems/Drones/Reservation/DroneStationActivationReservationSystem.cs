using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

/// <summary>실제 작업 선택 뒤 남은 수요만 자동 출고한다. 이 예산은 한 번의 출고 중재에만 쓰며 작업을 예약하지 않는다.</summary>
[UpdateInGroup(typeof(DroneReservationGroup))]
[UpdateAfter(typeof(DroneTaskReservationSystem))]
[UpdateBefore(typeof(DroneStationLifecycleReservationSystem))]
public partial struct DroneStationActivationReservationSystem : ISystem
{
    private EntityQuery _requests, _assignments;
    private Entity _candidates;
    public void OnCreate(ref SystemState state)
    {
        _candidates = DroneSchedulingUtility.GetOrCreateCandidates(state.EntityManager);
        _requests = SystemAPI.QueryBuilder().WithAll<DroneStationRequest, DroneAutomaticStationRequest>().WithAllRW<DroneStationDecision>().Build();
        _assignments = SystemAPI.QueryBuilder().WithAll<DroneTaskAssignment>().Build();
        state.RequireForUpdate(_requests);
    }
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var requests = _requests.ToEntityListAsync(Allocator.TempJob, state.Dependency, out var requestReady);
        var assignments = _assignments.ToEntityListAsync(Allocator.TempJob, state.Dependency, out var assignmentReady);
        var handle = new LimitActivationsJob {
            Requests = requests.AsDeferredJobArray(), AssignmentEntities = assignments.AsDeferredJobArray(), Candidates = _candidates,
            RequestLookup = SystemAPI.GetComponentLookup<DroneStationRequest>(true),
            Automatic = SystemAPI.GetComponentLookup<DroneAutomaticStationRequest>(true),
            Decisions = SystemAPI.GetComponentLookup<DroneStationDecision>(false),
            Tasks = SystemAPI.GetComponentLookup<DroneLogisticsTask>(true),
            Assignments = SystemAPI.GetComponentLookup<DroneTaskAssignment>(true),
            Requirements = SystemAPI.GetComponentLookup<DroneTransportRequirement>(true),
            Progress = SystemAPI.GetComponentLookup<DroneTransportProgress>(true),
            Materials = SystemAPI.GetBufferLookup<ConstructionMaterialRequirementElement>(true),
            Pending = SystemAPI.GetBufferLookup<DroneTaskPendingPublicationElement>(true)
        }.Schedule(JobHandle.CombineDependencies(state.Dependency, JobHandle.CombineDependencies(requestReady, assignmentReady)));
        state.Dependency = JobHandle.CombineDependencies(requests.Dispose(handle), assignments.Dispose(handle));
    }

    [BurstCompile]
    private struct LimitActivationsJob : IJob
    {
        [ReadOnly] public NativeArray<Entity> Requests, AssignmentEntities;
        public Entity Candidates;
        [ReadOnly] public ComponentLookup<DroneStationRequest> RequestLookup;
        [ReadOnly] public ComponentLookup<DroneAutomaticStationRequest> Automatic;
        public ComponentLookup<DroneStationDecision> Decisions;
        [ReadOnly] public ComponentLookup<DroneLogisticsTask> Tasks;
        [ReadOnly] public ComponentLookup<DroneTaskAssignment> Assignments;
        [ReadOnly] public ComponentLookup<DroneTransportRequirement> Requirements;
        [ReadOnly] public ComponentLookup<DroneTransportProgress> Progress;
        [ReadOnly] public BufferLookup<ConstructionMaterialRequirementElement> Materials;
        [ReadOnly] public BufferLookup<DroneTaskPendingPublicationElement> Pending;
        public void Execute()
        {
            using var records = new NativeList<DroneStationRequestRecord>(Requests.Length, Allocator.Temp);
            DroneStationLifecycleUtility.ReadSortedRequests(Requests, RequestLookup, Decisions, records);
            using var remaining = new NativeHashMap<Entity, int>(math.max(1, Requests.Length), Allocator.Temp);
            foreach (var record in records)
            {
                var automatic = Automatic[record.Entity];
                if (record.Request.Operation != DroneStationOperation.Activate)
                {
                    continue;
                }
                if (automatic.DemandTask == Entity.Null)
                {
                    continue;
                }
                if (!record.Decision.Eligible)
                {
                    continue;
                }
                if (!remaining.TryGetValue(automatic.DemandTask, out int available))
                {
                    available = ReadRemaining(automatic.DemandTask);
                    remaining.Add(automatic.DemandTask, available);
                }
                int taken = math.min(available, automatic.Quantity);
                if (taken <= 0)
                {
                    var decision = record.Decision;
                    decision.Eligible = false;
                    decision.Status = DroneStationRequestStatus.Busy;
                    Decisions[record.Entity] = decision;
                }
                else
                {
                    remaining.Remove(automatic.DemandTask);
                    remaining.Add(automatic.DemandTask, available - taken);
                }
            }
        }
        private int ReadRemaining(Entity taskEntity)
        {
            if (!Tasks.HasComponent(taskEntity)) return 0;
            var task = Tasks[taskEntity];
            if (task.State != DroneLogisticsTaskStateEnum.Open) return 0;
            if (DroneSchedulingUtility.IsTransportKind(task.Kind))
            {
                var requirement = Requirements[taskEntity];
                var progress = Progress[taskEntity];
                return (int)math.max(0L, (long)requirement.Quantity - progress.DeliveredQuantity - progress.ReservedQuantity);
            }
            if (task.Kind == DroneLogisticsTaskKindEnum.ConstructionSupply)
            {
                int amount = 0;
                foreach (var row in Materials[task.Target])
                    if (row.ItemType == task.ItemType) amount += row.RemainingToReserve;
                return amount;
            }
            foreach (Entity owner in AssignmentEntities)
            {
                var assignment = Assignments[owner];
                if (assignment.Source == task.Target && !DroneSchedulingUtility.IsTerminal(assignment.State)) return 0;
            }
            foreach (var pending in Pending[Candidates])
                if (pending.Candidate.Source == task.Target && (pending.Quantity > 0 || pending.PublicationQueued)) return 0;
            return 1;
        }
    }
}
