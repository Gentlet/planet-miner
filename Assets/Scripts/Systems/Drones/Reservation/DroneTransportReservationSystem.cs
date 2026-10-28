using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

/// <summary>일반 운반의 작업 내 목표량을 단일 Job에서 중재한다. 작업 간 선택과 배정/실물 변경은 수행하지 않는다.</summary>
[UpdateInGroup(typeof(DroneReservationGroup))]
public partial struct DroneTransportReservationSystem : ISystem
{
    private Entity _candidates;
    private ComponentLookup<DroneTransportRequirement> _requirements;
    private ComponentLookup<DroneTransportProgress> _progress;
    private BufferLookup<DroneTransportPlanDecisionElement> _plans;
    private BufferLookup<DroneTransportPendingReservationElement> _pending;

    public void OnCreate(ref SystemState state)
    {
        _candidates = DroneSchedulingUtility.GetOrCreateCandidates(state.EntityManager);
        _requirements = state.GetComponentLookup<DroneTransportRequirement>(true);
        _progress = state.GetComponentLookup<DroneTransportProgress>(false);
        _plans = state.GetBufferLookup<DroneTransportPlanDecisionElement>(true);
        _pending = state.GetBufferLookup<DroneTransportPendingReservationElement>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        _requirements.Update(ref state);
        _progress.Update(ref state);
        _plans.Update(ref state);
        _pending.Update(ref state);
        state.Dependency = new ReserveTransportQuantityJob
        {
            Candidates = _candidates,
            Requirements = _requirements,
            Progress = _progress,
            Plans = _plans,
            Pending = _pending
        }.Schedule(state.Dependency);
    }

    [BurstCompile]
    private struct ReserveTransportQuantityJob : IJob
    {
        public Entity Candidates;
        [ReadOnly] public ComponentLookup<DroneTransportRequirement> Requirements;
        public ComponentLookup<DroneTransportProgress> Progress;
        [ReadOnly] public BufferLookup<DroneTransportPlanDecisionElement> Plans;
        public BufferLookup<DroneTransportPendingReservationElement> Pending;

        public void Execute()
        {
            var pending = Pending[Candidates];
            for (int i = 0; i < pending.Length; i++)
            {
                var reservation = pending[i].Reservation;
                if (reservation.RemainingQuantity <= 0)
                {
                    continue;
                }
                // 원래 작업은 이전 EndSimulation에서 삭제됐을 수 있다. 반환 대상이 없으면 기록만 소비한다.
                if (!Progress.HasComponent(reservation.Task))
                {
                    continue;
                }
                var progress = Progress[reservation.Task];
                DroneTransportReservationUtility.Release(ref progress, ref reservation);
                Progress[reservation.Task] = progress;
            }
            pending.Clear();

            var plans = Plans[Candidates];
            for (int i = 0; i < plans.Length; i++)
            {
                var candidate = plans[i].Candidate;
                var progress = Progress[candidate.Task];
                if (!DroneTransportReservationUtility.TryReserve(candidate.Task, Requirements[candidate.Task],
                    ref progress, candidate.Quantity, out var reservation))
                {
                    continue;
                }
                Progress[candidate.Task] = progress;
                pending.Add(new DroneTransportPendingReservationElement { Candidate = candidate, Reservation = reservation });
            }
        }
    }
}
