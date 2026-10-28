using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

/// <summary>
/// 수행자별 적재 재배정/신규 후보를 선택하고 현장 공급량과 일반 운반 목표량을 중재한다.
/// 공급 선두는 PlacementStamp→생성 순번, 회수/반입/반출 선두는 생성 순번이며 두 선두를 생성 순번으로 비교한다.
/// 메인 스레드는 현장 예약/공개 대기를, 뒤따르는 단일 Burst Job은 일반 운반 확보량 반환/승인을 소유한다.
/// 일반 운반의 로컬 잔량은 선택 예산이다. 선택분만 Job에 전달하므로 미선택 대안은 목표량을 점유하지 않는다.
/// 공급/회수/재배정은 기존 Publish로, 일반 운반은 별도 미공개 확보 버퍼로 전달한다. 재고/공간은 예약하지 않는다.
/// </summary>
[UpdateInGroup(typeof(DroneReservationGroup))]
public partial struct DroneTaskReservationSystem : ISystem
{
    private Entity _candidates;
    private EntityQuery _assignments;
    private EntityQuery _registry;
    private EntityQuery _sites;
    private EntityQuery _capacity;
    private EntityQuery _transportOwners;
    private ComponentLookup<DroneTransportRequirement> _requirements;
    private ComponentLookup<DroneTransportProgress> _progress;
    private BufferLookup<DroneTransportPendingReservationElement> _pendingTransport;
    private ComponentLookup<DroneTransportReservation> _transportReservations;
    private ComponentLookup<DroneTaskAssignment> _assignmentLookup;
    private BufferLookup<DroneTaskInvalidationDecisionElement> _decisions;

    public void OnCreate(ref SystemState state)
    {
        _candidates = DroneSchedulingUtility.GetOrCreateCandidates(state.EntityManager);
        _assignments = state.GetEntityQuery(ComponentType.ReadOnly<DroneTaskAssignment>());
        _registry = state.GetEntityQuery(ComponentType.ReadOnly<ItemRegistry>(), ComponentType.ReadOnly<ItemConfigElement>());
        _sites = state.GetEntityQuery(ComponentType.ReadOnly<ConstructionSite>(), ComponentType.ReadOnly<GridPosition>());
        _capacity = state.GetEntityQuery(ComponentType.ReadOnly<DroneCapacityState>());
        _transportOwners = state.GetEntityQuery(ComponentType.ReadWrite<DroneTransportReservation>());
        _requirements = state.GetComponentLookup<DroneTransportRequirement>(true);
        _progress = state.GetComponentLookup<DroneTransportProgress>(false);
        _pendingTransport = state.GetBufferLookup<DroneTransportPendingReservationElement>(false);
        _transportReservations = state.GetComponentLookup<DroneTransportReservation>(false);
        _assignmentLookup = state.GetComponentLookup<DroneTaskAssignment>(true);
        _decisions = state.GetBufferLookup<DroneTaskInvalidationDecisionElement>(true);
    }

    public void OnUpdate(ref SystemState state)
    {
        state.CompleteDependency();
        var manager = state.EntityManager;
        int carryingCapacity = DroneSchedulingUtility.ReadCarryingCapacity(_capacity);
        var candidates = manager.GetBuffer<DroneTaskCandidateDecisionElement>(_candidates);
        var pendingPublications = manager.GetBuffer<DroneTaskPendingPublicationElement>(_candidates);
        ReleasePendingReservations(manager, pendingPublications);
        using var usedWorkers = new NativeHashSet<Entity>(16, Allocator.Temp);
        using var claimedItems = new NativeHashSet<Entity>(16, Allocator.Temp);
        using var assignments = _assignments.ToEntityArray(Allocator.Temp);
        using var sites = _sites.ToEntityArray(Allocator.Temp);
        // 배정 상태를 닫기 전에 수량 예약만 해제한다. 연결/종료 상태 변경은 Lifecycle Apply의 책임이다.
        // 정상 배정의 드론과 회수 실물은 새 작업에 중복 선택하지 않는다.
        for (int i = 0; i < assignments.Length; i++)
        {
            Entity entity = assignments[i];
            var assignment = manager.GetComponentData<DroneTaskAssignment>(entity);
            if (DroneSchedulingUtility.AssignmentNeedsCleanup(manager, entity, assignment, sites))
            {
                if (manager.HasComponent<ConstructionSupplyReservation>(entity))
                {
                    var reservation = manager.GetComponentData<ConstructionSupplyReservation>(entity);
                    ConstructionSupplyReservationUtility.Release(manager, reservation.Site, reservation.ItemType, reservation.RemainingQuantity);
                    reservation.RemainingQuantity = 0;
                    manager.SetComponentData(entity, reservation);
                }
                continue;
            }
            usedWorkers.Add(assignment.Worker);
            if (assignment.NextAction == DroneActionKindEnum.RecoverWorldItem) claimedItems.Add(assignment.Source);
        }

        Entity registry = _registry.IsEmptyIgnoreFilter ? Entity.Null : _registry.GetSingletonEntity();
        // 적재품 재배정을 신규 수집보다 먼저 선택한다. 기존 실물에는 변경된 공통 적재량을 소급 적용하지 않는다.
        SelectRetargetCandidates(manager, candidates, pendingPublications, registry, carryingCapacity, usedWorkers);
        var owners = _transportOwners.ToEntityArray(Allocator.TempJob);
        var selectedTransport = new NativeList<DroneTaskCandidateDecisionElement>(Allocator.TempJob);
        SelectNewCandidates(manager, registry, carryingCapacity, usedWorkers, claimedItems, owners, selectedTransport);
        ScheduleTransportReservations(ref state, owners, selectedTransport);
    }

    private void SelectNewCandidates(EntityManager manager, Entity registry, int carryingCapacity,
        NativeHashSet<Entity> usedWorkers, NativeHashSet<Entity> claimedItems, NativeArray<Entity> owners,
        NativeList<DroneTaskCandidateDecisionElement> selectedTransport)
    {
        using var candidates = new NativeList<DroneTaskCandidateDecisionElement>(Allocator.Temp);
        var legacyCandidates = manager.GetBuffer<DroneTaskCandidateDecisionElement>(_candidates, true);
        for (int i = 0; i < legacyCandidates.Length; i++)
        {
            if (legacyCandidates[i].Assignment == Entity.Null)
            {
                candidates.Add(legacyCandidates[i]);
            }
        }
        var transportPlans = manager.GetBuffer<DroneTransportPlanDecisionElement>(_candidates, true);
        for (int i = 0; i < transportPlans.Length; i++)
        {
            candidates.Add(transportPlans[i].Candidate);
        }

        var pendingPublications = manager.GetBuffer<DroneTaskPendingPublicationElement>(_candidates);
        var oldTransport = manager.GetBuffer<DroneTransportPendingReservationElement>(_candidates, true);
        var decisions = manager.GetBuffer<DroneTaskInvalidationDecisionElement>(_candidates, true);
        var remainingTransport = new NativeHashMap<Entity, int>(16, Allocator.Temp);
        try
        {
            using var visitedWorkers = new NativeHashSet<Entity>(16, Allocator.Temp);
            for (int w = 0; w < candidates.Length; w++)
            {
                Entity worker = candidates[w].Worker;
                if (usedWorkers.Contains(worker))
                {
                    continue;
                }
                if (!visitedWorkers.Add(worker))
                {
                    continue;
                }
                int supplyHead = -1;
                int otherHead = -1;
                int supplyQuantity = 0;
                int otherQuantity = 0;
                for (int c = 0; c < candidates.Length; c++)
                {
                    var candidate = candidates[c];
                    if (candidate.Worker != worker)
                    {
                        continue;
                    }
                    var task = manager.GetComponentData<DroneLogisticsTask>(candidate.Task);
                    if (task.Kind == DroneLogisticsTaskKindEnum.WorldItemRecovery && claimedItems.Contains(candidate.Source))
                    {
                        continue;
                    }
                    int quantity;
                    if (DroneSchedulingUtility.IsTransportKind(task.Kind))
                    {
                        if (!remainingTransport.TryGetValue(candidate.Task, out int remaining))
                        {
                            remaining = DroneTransportReservationUtility.ProjectedRemaining(candidate.Task,
                                manager.GetComponentData<DroneTransportRequirement>(candidate.Task),
                                manager.GetComponentData<DroneTransportProgress>(candidate.Task), oldTransport,
                                DroneTransportReservationUtility.ReadReleasingQuantity(manager, candidate.Task, owners, decisions));
                            remainingTransport.Add(candidate.Task, remaining);
                        }
                        // Decision 이후 재고/공간/경로는 변하지 않는다. 선택한 목표량 예산만 차감한다.
                        quantity = math.min(candidate.Quantity, remaining);
                    }
                    else
                    {
                        quantity = DroneSchedulingUtility.CandidateQuantity(manager, candidate, registry, carryingCapacity);
                    }
                    if (quantity <= 0)
                    {
                        continue;
                    }
                    if (task.Kind == DroneLogisticsTaskKindEnum.ConstructionSupply)
                    {
                        if (supplyHead < 0 || DroneSchedulingUtility.EarlierTask(manager, candidate.Task, candidates[supplyHead].Task))
                        {
                            supplyHead = c;
                            supplyQuantity = quantity;
                        }
                    }
                    else if (otherHead < 0 || DroneSchedulingUtility.EarlierTask(manager, candidate.Task, candidates[otherHead].Task))
                    {
                        otherHead = c;
                        otherQuantity = quantity;
                    }
                }

                // 공급 전체를 생성 순번으로 정렬하지 않는다. Stamp로 고른 공급 선두와 다른 유형 선두만 비교한다.
                bool selectOther = supplyHead < 0;
                if (supplyHead >= 0 && otherHead >= 0)
                {
                    selectOther = manager.GetComponentData<DroneLogisticsTask>(candidates[otherHead].Task).CreationSequence <
                        manager.GetComponentData<DroneLogisticsTask>(candidates[supplyHead].Task).CreationSequence;
                }
                int selected = selectOther ? otherHead : supplyHead;
                if (selected < 0)
                {
                    continue;
                }
                var chosen = candidates[selected];
                var chosenTask = manager.GetComponentData<DroneLogisticsTask>(chosen.Task);
                int quantitySelected = selectOther ? otherQuantity : supplyQuantity;
                if (DroneSchedulingUtility.IsTransportKind(chosenTask.Kind))
                {
                    chosen.Quantity = quantitySelected;
                    selectedTransport.Add(chosen);
                    remainingTransport[chosen.Task] -= quantitySelected;
                }
                else
                {
                    int committedQuantity = 0;
                    if (chosenTask.Kind == DroneLogisticsTaskKindEnum.ConstructionSupply)
                    {
                        committedQuantity = ConstructionSupplyReservationUtility.Reserve(manager,
                            chosen.Destination, chosen.ItemType, quantitySelected);
                        quantitySelected = committedQuantity;
                    }
                    else
                    {
                        claimedItems.Add(chosen.Source);
                    }
                    if (quantitySelected <= 0)
                    {
                        continue;
                    }
                    pendingPublications.Add(new DroneTaskPendingPublicationElement
                    {
                        Candidate = chosen,
                        Quantity = quantitySelected,
                        CommittedQuantity = committedQuantity
                    });
                }
                usedWorkers.Add(worker);
            }
        }
        finally
        {
            remainingTransport.Dispose();
        }
    }

    private void ScheduleTransportReservations(ref SystemState state, NativeArray<Entity> owners,
        NativeList<DroneTaskCandidateDecisionElement> selectedTransport)
    {
        _requirements.Update(ref state);
        _progress.Update(ref state);
        _pendingTransport.Update(ref state);
        _transportReservations.Update(ref state);
        _assignmentLookup.Update(ref state);
        _decisions.Update(ref state);
        var handle = new ReserveTransportQuantityJob
        {
            Candidates = _candidates,
            Requirements = _requirements,
            Progress = _progress,
            Selected = selectedTransport.AsArray(),
            Pending = _pendingTransport,
            Owners = owners,
            Reservations = _transportReservations,
            Assignments = _assignmentLookup,
            Decisions = _decisions
        }.Schedule(state.Dependency);
        state.Dependency = JobHandle.CombineDependencies(owners.Dispose(handle), selectedTransport.Dispose(handle));
    }

    private static void ReleasePendingReservations(EntityManager manager,
        DynamicBuffer<DroneTaskPendingPublicationElement> pendingPublications)
    {
        // 이전 틱 미공개 확보량도 현장 예약 합계에 포함된다.
        // Decision이 보존한 별도 기록으로 합계를 되돌린 뒤 이번 틱 선택을 시작한다.
        for (int i = 0; i < pendingPublications.Length; i++)
        {
            var pending = pendingPublications[i];
            if (pending.PublicationQueued) continue;
            if (pending.CommittedQuantity <= 0) continue;
            ConstructionSupplyReservationUtility.Release(manager, pending.Candidate.Destination,
                pending.Candidate.ItemType, pending.CommittedQuantity);
        }
        pendingPublications.Clear();
    }

    private static void SelectRetargetCandidates(EntityManager manager,
        DynamicBuffer<DroneTaskCandidateDecisionElement> candidates,
        DynamicBuffer<DroneTaskPendingPublicationElement> pendingPublications, Entity registry, int capacity,
        NativeHashSet<Entity> usedWorkers)
    {
        var ordered = new NativeList<int>(Allocator.Temp);
        try
        {
            // 목적 작업이 바뀌어도 최초 작업 생성 순번을 유지하여 오래 운반 중인 자재부터 목적지를 확보한다.
            for (int i = 0; i < candidates.Length; i++)
            {
                if (candidates[i].Assignment == Entity.Null) continue;
                int insert = ordered.Length;
                ordered.Add(i);
                while (insert > 0 && EarlierRetarget(manager, candidates[i], candidates[ordered[insert - 1]]))
                {
                    ordered[insert] = ordered[insert - 1];
                    insert--;
                }
                ordered[insert] = i;
            }
            for (int i = 0; i < ordered.Length; i++)
            {
                int index = ordered[i];
                var candidate = candidates[index];
                if (usedWorkers.Contains(candidate.Worker)) continue;
                int quantity = DroneSchedulingUtility.CandidateQuantity(manager, candidate, registry, capacity);
                if (quantity <= 0) continue;
                int committedQuantity = 0;
                if (candidate.NextAction == DroneActionKindEnum.SupplyConstructionSite)
                {
                    committedQuantity = ConstructionSupplyReservationUtility.Reserve(manager,
                        candidate.Destination, candidate.ItemType, quantity);
                    quantity = committedQuantity;
                }
                if (quantity <= 0) continue;
                pendingPublications.Add(new DroneTaskPendingPublicationElement
                {
                    Candidate = candidate,
                    Quantity = quantity,
                    CommittedQuantity = committedQuantity
                });
                usedWorkers.Add(candidate.Worker);
            }
        }
        finally
        {
            ordered.Dispose();
        }
    }

    private static bool EarlierRetarget(EntityManager manager, in DroneTaskCandidateDecisionElement left,
        in DroneTaskCandidateDecisionElement right)
    {
        ulong leftOrder = RetargetOrder(manager, left.Assignment);
        ulong rightOrder = RetargetOrder(manager, right.Assignment);
        if (leftOrder != rightOrder) return leftOrder < rightOrder;
        return left.Assignment.Index < right.Assignment.Index;
    }

    private static ulong RetargetOrder(EntityManager manager, Entity entity)
    {
        if (!manager.HasComponent<DroneTaskAssignment>(entity)) return ulong.MaxValue;
        return DroneSchedulingUtility.AssignmentCreationSequence(manager,
            manager.GetComponentData<DroneTaskAssignment>(entity));
    }

    [BurstCompile]
    private struct ReserveTransportQuantityJob : IJob
    {
        public Entity Candidates;
        [ReadOnly] public ComponentLookup<DroneTransportRequirement> Requirements;
        public ComponentLookup<DroneTransportProgress> Progress;
        [ReadOnly] public NativeArray<DroneTaskCandidateDecisionElement> Selected;
        public BufferLookup<DroneTransportPendingReservationElement> Pending;
        [ReadOnly] public NativeArray<Entity> Owners;
        public ComponentLookup<DroneTransportReservation> Reservations;
        [ReadOnly] public ComponentLookup<DroneTaskAssignment> Assignments;
        [ReadOnly] public BufferLookup<DroneTaskInvalidationDecisionElement> Decisions;

        public void Execute()
        {
            var decisions = Decisions[Candidates];
            for (int i = 0; i < Owners.Length; i++)
            {
                var reservation = Reservations[Owners[i]];
                if (reservation.RemainingQuantity <= 0)
                {
                    continue;
                }
                // 같은 틱 Decision의 무효화 의도를 사용한다. 원본 배정/경로/실물을 여기서 다시 판단하지 않는다.
                if (!DroneTransportReservationUtility.ShouldRelease(Owners[i], Assignments.HasComponent(Owners[i]), decisions))
                {
                    continue;
                }
                if (Progress.HasComponent(reservation.Task))
                {
                    var progress = Progress[reservation.Task];
                    DroneTransportReservationUtility.Release(ref progress, ref reservation);
                    Progress[reservation.Task] = progress;
                }
                else
                {
                    reservation.RemainingQuantity = 0;
                }
                Reservations[Owners[i]] = reservation;
            }

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

            for (int i = 0; i < Selected.Length; i++)
            {
                var candidate = Selected[i];
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
