using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

/// <summary>정상 귀환과 이번 틱 작업 선택/미평가 경로/정거장 요청의 경합을 중재한다. 이동 원본은 Execution만 작성한다.</summary>
[UpdateInGroup(typeof(DroneReservationGroup))]
[UpdateAfter(typeof(DroneTaskReservationSystem))]
[UpdateBefore(typeof(DroneStationLifecycleReservationSystem))]
public partial struct DroneReturnReservationSystem : ISystem
{
    private EntityQuery _pending;
    private EntityQuery _stations;
    private EntityQuery _actions;
    private EntityQuery _workers;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        _pending = SystemAPI.QueryBuilder().WithAll<DroneTaskPendingPublicationElement, DroneTransportPendingReservationElement, DroneRouteDecisionElement>()
            .WithAll<DroneReturnReservationElement>().Build();
        _stations = SystemAPI.QueryBuilder().WithAll<DroneStationRequest>().Build();
        _actions = SystemAPI.QueryBuilder().WithAll<DroneActionReadyRequest>().Build();
        _workers = SystemAPI.QueryBuilder().WithAll<DroneWorker, DroneMovementDecision, DroneBatteryState, DroneWorkerObservation>().WithNone<DroneDocked>().Build();
        state.RequireForUpdate(_pending);
    }

    public void OnUpdate(ref SystemState state)
    {
        var stationRequests = _stations.ToComponentDataArrayAsync<DroneStationRequest>(Allocator.TempJob, out var stationsReady);
        var actionRequests = _actions.ToComponentDataArrayAsync<DroneActionReadyRequest>(Allocator.TempJob, out var actionsReady);
        var workers = _workers.ToEntityArrayAsync(Allocator.TempJob, out var workersReady);
        var job = new ArbitrateReturnsJob
        {
            Candidates = _pending.GetSingletonEntity(), StationRequests = stationRequests, ActionRequests = actionRequests,
            Workers = workers, Decisions = SystemAPI.GetComponentLookup<DroneMovementDecision>(true),
            Batteries = SystemAPI.GetComponentLookup<DroneBatteryState>(true),
            Observations = SystemAPI.GetComponentLookup<DroneWorkerObservation>(true),
            Results = SystemAPI.GetBufferLookup<DroneReturnReservationElement>(),
            Pending = SystemAPI.GetBufferLookup<DroneTaskPendingPublicationElement>(true),
            Transport = SystemAPI.GetBufferLookup<DroneTransportPendingReservationElement>(true),
            Routes = SystemAPI.GetBufferLookup<DroneRouteDecisionElement>(true),
            Requests = SystemAPI.GetComponentLookup<DroneRouteEvaluationRequest>(true),
            Evaluations = SystemAPI.GetComponentLookup<DroneRouteEvaluationDecision>(true)
        }.Schedule(JobHandle.CombineDependencies(state.Dependency, workersReady, JobHandle.CombineDependencies(stationsReady, actionsReady)));
        state.Dependency = JobHandle.CombineDependencies(stationRequests.Dispose(job), actionRequests.Dispose(job), workers.Dispose(job));
    }

    [BurstCompile]
    private struct ArbitrateReturnsJob : IJob
    {
        public Entity Candidates;
        [ReadOnly] public NativeArray<Entity> Workers;
        [ReadOnly] public ComponentLookup<DroneMovementDecision> Decisions;
        [ReadOnly] public ComponentLookup<DroneBatteryState> Batteries;
        [ReadOnly] public ComponentLookup<DroneWorkerObservation> Observations;
        public BufferLookup<DroneReturnReservationElement> Results;
        [ReadOnly] public NativeArray<DroneStationRequest> StationRequests;
        [ReadOnly] public NativeArray<DroneActionReadyRequest> ActionRequests;
        [ReadOnly] public BufferLookup<DroneTaskPendingPublicationElement> Pending;
        [ReadOnly] public BufferLookup<DroneTransportPendingReservationElement> Transport;
        [ReadOnly] public BufferLookup<DroneRouteDecisionElement> Routes;
        [ReadOnly] public ComponentLookup<DroneRouteEvaluationRequest> Requests;
        [ReadOnly] public ComponentLookup<DroneRouteEvaluationDecision> Evaluations;

        public void Execute()
        {
            var results = Results[Candidates];
            results.Clear();
            for (int i = 0; i < Workers.Length; i++)
            {
                Entity entity = Workers[i];
                var decision = Decisions[entity];
                if (decision.Purpose != DroneFlightPurposeEnum.NormalReturn &&
                    !(decision.Purpose == DroneFlightPurposeEnum.WaitingForStation && Batteries[entity].CurrentCharge > 0f))
                {
                    continue;
                }
                if (ShouldWait(entity, Observations[entity], out bool preserveArrival))
                {
                    results.Add(new DroneReturnReservationElement { Worker = entity, PreserveArrival = preserveArrival });
                }
            }
        }

        private bool ShouldWait(Entity entity, in DroneWorkerObservation observation, out bool preserveArrival)
        {
            preserveArrival = false;
            // 접수된 정거장/행동 요청의 최종 Apply까지 현재 이동 상태와 위치를 유지한다.
            for (int i = 0; i < StationRequests.Length; i++)
            {
                if (StationRequests[i].Drone == entity)
                {
                    preserveArrival = true;
                    return true;
                }
            }
            for (int i = 0; i < ActionRequests.Length; i++)
            {
                if (ActionRequests[i].Action.Worker == entity)
                {
                    preserveArrival = true;
                    return true;
                }
            }
            var pending = Pending[Candidates];
            for (int i = 0; i < pending.Length; i++)
            {
                if (pending[i].Candidate.Worker == entity && pending[i].Quantity > 0)
                {
                    return true;
                }
            }
            var transport = Transport[Candidates];
            for (int i = 0; i < transport.Length; i++)
            {
                if (transport[i].Candidate.Worker == entity &&
                    (transport[i].Reservation.RemainingQuantity > 0 || transport[i].PublicationQueued))
                {
                    return true;
                }
            }
            var routes = Routes[Candidates];
            for (int i = 0; i < routes.Length; i++)
            {
                var route = routes[i];
                if (route.Kind == DroneRouteDecisionKindEnum.Create)
                {
                    if (route.Request.Worker == entity)
                    {
                        return true;
                    }
                }
                else if (route.Kind == DroneRouteDecisionKindEnum.Retain)
                {
                    if (!Requests.HasComponent(route.ExistingRequest))
                    {
                        continue;
                    }
                    var request = Requests[route.ExistingRequest];
                    if (request.Worker != entity || request.WorkerObservationRevision != observation.Revision)
                    {
                        continue;
                    }
                    if (!Evaluations.HasComponent(route.ExistingRequest) || !Evaluations[route.ExistingRequest].Ready ||
                        Evaluations[route.ExistingRequest].Result.Status == DroneRouteEvaluationStatusEnum.None)
                    {
                        // 평가를 기다리는 동안 위치를 바꾸면 관측 revision이 계속 바뀌어 새 작업이 굶는다.
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
