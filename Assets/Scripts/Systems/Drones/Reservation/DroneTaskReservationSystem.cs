using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

/// <summary>
/// 적재 화물 재배정을 우선하고 신규 공급, 회수, 운반, 철거를 같은 수행자 예산으로 중재한다.
/// 일반 운반은 작업 목표량을 확보한다. 철거 화물의 재배정은 목적지 슬롯도 먼저 확보하고, 신규 환급 집결은 전원분을 함께 확보한다.
/// 환급 배정 전체가 Publish에서 유효하면 배정을 공개한다. 납품 성공 또는 중단에서 슬롯 확보량을 정산한다.
/// </summary>
[UpdateInGroup(typeof(DroneReservationGroup))]
public partial struct DroneTaskReservationSystem : ISystem
{
    private Entity _candidates;
    private EntityQuery _assignments;
    private EntityQuery _registry;
    private EntityQuery _capacity;
    private EntityQuery _refundConfig;
    private EntityQuery _destinationClaims;
    private EntityQuery _demolitions, _automaticRequests, _storages, _networks;
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
        _capacity = state.GetEntityQuery(ComponentType.ReadOnly<DroneCapacityState>());
        _refundConfig = state.GetEntityQuery(ComponentType.ReadOnly<BuildingConstructionMaterialElement>());
        _destinationClaims = state.GetEntityQuery(ComponentType.ReadWrite<DroneStationReservedSlotElement>());
        _demolitions = state.GetEntityQuery(ComponentType.ReadOnly<DroneDemolitionProgress>());
        _automaticRequests = state.GetEntityQuery(ComponentType.ReadOnly<DroneStationRequest>(), ComponentType.ReadOnly<DroneAutomaticStationRequest>());
        _storages = state.GetEntityQuery(ComponentType.ReadOnly<BuildingIdentity>(), ComponentType.ReadOnly<Storage>(),
            ComponentType.ReadOnly<StorageSlotElement>(), ComponentType.ReadOnly<GridPosition>());
        _networks = state.GetEntityQuery(ComponentType.ReadOnly<DroneStation>(), ComponentType.ReadOnly<DroneStationNetworkDecision>());
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
        PrepareDemolitionStorage(manager);
        var candidates = manager.GetBuffer<DroneTaskCandidateDecisionElement>(_candidates);
        var pendingPublications = manager.GetBuffer<DroneTaskPendingPublicationElement>(_candidates);
        ReleasePendingReservations(manager, pendingPublications);
        using var usedWorkers = new NativeHashSet<Entity>(16, Allocator.Temp);
        using var claimedTargets = new NativeHashSet<Entity>(16, Allocator.Temp);
        using var assignments = _assignments.ToEntityArray(Allocator.Temp);
        var invalidations = manager.GetBuffer<DroneTaskInvalidationDecisionElement>(_candidates, true);
        using var endingAssignments = new NativeHashMap<Entity, uint>(invalidations.Length, Allocator.Temp);
        foreach (var intent in invalidations)
        {
            if (intent.Kind == DroneTaskInvalidationDecisionKindEnum.CancelAssignment ||
                intent.Kind == DroneTaskInvalidationDecisionKindEnum.InvalidateAssignment)
            {
                endingAssignments.TryAdd(intent.Target, intent.AssignmentRevision);
            }
        }
        // 배정 상태를 닫기 전에 수량 예약만 해제한다. 연결/종료 상태 변경은 Lifecycle Apply의 책임이다.
        // 정상 배정의 드론/회수 실물/철거 건물은 새 작업에 중복 선택하지 않는다.
        for (int i = 0; i < assignments.Length; i++)
        {
            Entity entity = assignments[i];
            var assignment = manager.GetComponentData<DroneTaskAssignment>(entity);
            if (endingAssignments.TryGetValue(entity, out uint revision) && revision == assignment.Revision)
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
            if (assignment.NextAction == DroneActionKindEnum.RecoverWorldItem) claimedTargets.Add(assignment.Source);
            if (assignment.NextAction == DroneActionKindEnum.DemolishBuilding &&
                manager.HasComponent<DroneLogisticsTask>(assignment.Task))
                claimedTargets.Add(manager.GetComponentData<DroneLogisticsTask>(assignment.Task).Target);
        }

        Entity registry = _registry.IsEmptyIgnoreFilter ? Entity.Null : _registry.GetSingletonEntity();
        // 적재품 재배정을 신규 수집보다 먼저 선택한다. 기존 실물에는 변경된 공통 적재량을 소급 적용하지 않는다.
        SelectRetargetCandidates(manager, candidates, pendingPublications, registry, carryingCapacity, usedWorkers);
        var owners = _transportOwners.ToEntityArray(Allocator.TempJob);
        var selectedTransport = new NativeList<DroneTaskCandidateDecisionElement>(Allocator.TempJob);
        SelectNewCandidates(manager, registry, carryingCapacity, usedWorkers, claimedTargets, owners, selectedTransport);
        BlockActivationsWithoutRefundSpace(manager, registry, pendingPublications);
        ScheduleTransportReservations(ref state, owners, selectedTransport);
    }

    private void BlockActivationsWithoutRefundSpace(EntityManager manager, Entity registry,
        DynamicBuffer<DroneTaskPendingPublicationElement> selected)
    {
        var blocks = manager.GetBuffer<DroneDemolitionActivationBlockElement>(_candidates);
        blocks.Clear();
        if (_demolitions.IsEmptyIgnoreFilter || _automaticRequests.IsEmptyIgnoreFilter) return;

        using var requests = _automaticRequests.ToComponentDataArray<DroneAutomaticStationRequest>(Allocator.Temp);
        using var roots = new NativeHashSet<Entity>(4, Allocator.Temp);
        foreach (var request in requests)
        {
            Entity root = request.DemandTask;
            if (!manager.HasComponent<DroneDemolitionProgress>(root)) continue;
            var progress = manager.GetComponentData<DroneDemolitionProgress>(root);
            if (!progress.AwaitingSpace && progress.AssignedWorkers == 0 && progress.RequiredWorkers > 0) roots.Add(root);
        }
        // 이 틱에 이미 배정된 작업은 기존 Activation Reservation이 배정 기록으로 직접 제외한다.
        foreach (var publication in selected)
        {
            if (publication.Quantity > 0) roots.Remove(publication.Candidate.Task);
        }
        if (roots.IsEmpty) return;

        using var stations = _networks.ToComponentDataArray<DroneStationNetworkDecision>(Allocator.Temp);
        using var stores = _storages.ToEntityArray(Allocator.Temp);
        using var storagesByNetwork = new StorageCandidatesByNetwork(manager, stores);
        using var bounds = new NativeParallelMultiHashMap<Entity, DroneStationActivityBounds>(math.max(1, stations.Length), Allocator.Temp);
        Entity config = _refundConfig.IsEmptyIgnoreFilter ? Entity.Null : _refundConfig.GetSingletonEntity();
        foreach (Entity root in roots)
        {
            if (!DroneDemolitionUtility.TryGetDemolitionWork(manager, root, out var task)) continue;
            using var costs = DroneDemolitionUtility.ReadRefunds(manager, root, task, config);
            if (costs.IsEmpty) continue;
            using var destinations = DroneTaskNetworkUtility.ReadDemolitionDestinations(manager, task, stations,
                storagesByNetwork, bounds, DroneStationRelocationUtility.IsStationDemolition(manager, task));
            if (!DroneDemolitionStorageUtility.CanFitRefund(manager, registry, destinations, costs, 0, costs[0].Quantity))
            {
                blocks.Add(new DroneDemolitionActivationBlockElement { Task = root });
            }
        }
    }

    private void SelectNewCandidates(EntityManager manager, Entity registry, int carryingCapacity,
        NativeHashSet<Entity> usedWorkers, NativeHashSet<Entity> claimedTargets, NativeArray<Entity> owners,
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
        using var returningTransport = new NativeHashMap<Entity, long>(16, Allocator.Temp);
        bool collectedTransportReturns = false;
        var remainingTransport = new NativeHashMap<Entity, int>(16, Allocator.Temp);
        var flightReads = new DroneDemolitionUtility.RefundFlightReadContext();
        try
        {
            using var visitedWorkers = new NativeHashSet<Entity>(16, Allocator.Temp);
            using var rejectedCrews = new NativeHashSet<Entity>(4, Allocator.Temp);
            Entity config = _refundConfig.IsEmptyIgnoreFilter ? Entity.Null : _refundConfig.GetSingletonEntity();
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
                    if (task.Kind == DroneLogisticsTaskKindEnum.WorldItemRecovery && claimedTargets.Contains(candidate.Source))
                    {
                        continue;
                    }
                    if (task.Kind == DroneLogisticsTaskKindEnum.BuildingDemolition &&
                        (claimedTargets.Contains(task.Target) || rejectedCrews.Contains(candidate.Task))) continue;
                    int quantity;
                    if (DroneSchedulingUtility.IsTransportKind(task.Kind))
                    {
                        if (!remainingTransport.TryGetValue(candidate.Task, out int remaining))
                        {
                            if (!collectedTransportReturns)
                            {
                                DroneTransportReservationUtility.CollectReturningQuantities(manager, owners, oldTransport,
                                    decisions, returningTransport);
                                collectedTransportReturns = true;
                            }
                            returningTransport.TryGetValue(candidate.Task, out long returning);
                            remaining = DroneTransportReservationUtility.ProjectedRemaining(
                                manager.GetComponentData<DroneTransportRequirement>(candidate.Task),
                                manager.GetComponentData<DroneTransportProgress>(candidate.Task), returning);
                            remainingTransport.Add(candidate.Task, remaining);
                        }
                        quantity = math.min(candidate.Quantity, remaining);
                        // 앞선 이관 Reservation이 일반 슬롯까지 확보할 수 있다. 차순위를 버리기 전에 현재 공간을 확인한다.
                        if (quantity > 0 && manager.HasBuffer<DroneStationReservedSlotElement>(candidate.Destination) &&
                            !manager.GetBuffer<DroneStationReservedSlotElement>(candidate.Destination, true).IsEmpty)
                        {
                            quantity = DroneTransportEndpointUtility.DestinationSpace(manager, task, candidate.Destination, registry, quantity);
                        }
                    }
                    else
                    {
                        quantity = task.Kind == DroneLogisticsTaskKindEnum.BuildingDemolition
                            ? DroneDemolitionUtility.CandidateQuantity(manager, candidate, registry, carryingCapacity)
                            : DroneSchedulingUtility.CandidateQuantity(manager, candidate, registry, carryingCapacity);
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
                    else if (otherHead < 0 || PreferNonSupplyCandidate(manager, task, candidate, candidates[otherHead]))
                    {
                        if (!CanReservePickupRoute(manager, registry, task, candidate, quantity, ref flightReads)) continue;
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
                if (chosenTask.Kind == DroneLogisticsTaskKindEnum.BuildingDemolition)
                {
                    using var costs = DroneDemolitionUtility.ReadRefunds(manager, chosen.Task, chosenTask, config);
                    using var crew = new NativeList<DroneTaskCandidateDecisionElement>(Allocator.Temp);
                    bool selectedCrew;
                    if (costs.IsEmpty)
                    {
                        crew.Add(chosen);
                        usedWorkers.Add(worker);
                        selectedCrew = true;
                    }
                    else
                    {
                        using var available = new NativeList<DroneTaskCandidateDecisionElement>(Allocator.Temp);
                        using var canJoin = new NativeHashMap<Entity, bool>(8, Allocator.Temp);
                        foreach (var member in candidates)
                        {
                            if (member.Task != chosen.Task || usedWorkers.Contains(member.Worker)) continue;
                            if (!canJoin.TryGetValue(member.Worker, out bool eligible))
                            {
                                eligible = !PrefersEarlierWork(manager, candidates, member, registry, carryingCapacity,
                                    claimedTargets, rejectedCrews, remainingTransport, ref flightReads);
                                canJoin.Add(member.Worker, eligible);
                            }
                            if (eligible) available.Add(member);
                        }
                        using var bounds = new RefundSearchBounds(Allocator.Temp);
                        selectedCrew = TryReserveRefundCrew(manager, registry, carryingCapacity, chosen.Task, available,
                            costs, 0, costs[0].Quantity, 0, usedWorkers, crew, bounds, ref flightReads);
                    }
                    if (!selectedCrew)
                    {
                        // 전량 배정이 어려운 철거는 남은 후보의 일반 배차를 막지 않는다.
                        rejectedCrews.Add(chosen.Task);
                        visitedWorkers.Remove(worker);
                        w--;
                        continue;
                    }

                    claimedTargets.Add(chosenTask.Target);
                    foreach (var member in crew)
                        pendingPublications.Add(new DroneTaskPendingPublicationElement
                        {
                            Candidate = member, Quantity = member.Quantity
                        });
                    continue;
                }
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
                        claimedTargets.Add(chosen.Source);
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
            flightReads.Dispose();
            remainingTransport.Dispose();
        }
    }

    private void PrepareDemolitionStorage(EntityManager manager)
    {
        using var existing = _destinationClaims.ToEntityArray(Allocator.Temp);
        foreach (Entity storage in existing)
        {
            var slots = manager.GetBuffer<DroneStationReservedSlotElement>(storage);
            for (int i = slots.Length - 1; i >= 0; i--)
            {
                var slot = slots[i];
                if (slot.TransportWorker == Entity.Null) continue;
                Entity assignment = manager.HasComponent<DroneWorkerAssignment>(slot.TransportWorker)
                    ? manager.GetComponentData<DroneWorkerAssignment>(slot.TransportWorker).Assignment : Entity.Null;
                if (manager.HasComponent<DroneTaskAssignment>(assignment))
                {
                    var current = manager.GetComponentData<DroneTaskAssignment>(assignment);
                    if (current.Task == slot.Task && current.Destination == storage &&
                        current.State != DroneTaskAssignmentStateEnum.Retargeting && !DroneSchedulingUtility.IsTerminal(current.State)) continue;
                }
                slots.RemoveAt(i);
            }
        }

        using var destinations = new NativeHashSet<Entity>(4, Allocator.Temp);
        foreach (var row in manager.GetBuffer<DroneTransportPlanDecisionElement>(_candidates, true))
            if (manager.GetComponentData<DroneLogisticsTask>(row.Candidate.Task).DemolitionRoot != Entity.Null)
                destinations.Add(row.Candidate.Destination);
        foreach (var candidate in manager.GetBuffer<DroneTaskCandidateDecisionElement>(_candidates, true))
        {
            if (candidate.Assignment != Entity.Null && candidate.NextAction == DroneActionKindEnum.StoreCargo &&
                manager.HasComponent<DroneCargoState>(candidate.Worker) &&
                manager.GetComponentData<DroneCargoState>(candidate.Worker).Origin == DroneCargoOriginEnum.DemolitionRecovery)
                destinations.Add(candidate.Destination);
            if (!manager.HasComponent<DroneLogisticsTask>(candidate.Task)) continue;
            var task = manager.GetComponentData<DroneLogisticsTask>(candidate.Task);
            if (task.DemolitionRoot != Entity.Null ||
                (task.Kind == DroneLogisticsTaskKindEnum.BuildingDemolition && candidate.ItemType != ItemTypeEnum.None))
                destinations.Add(candidate.Destination);
        }
        foreach (Entity storage in destinations)
            if (DroneSchedulingUtility.IsStorage(manager, storage) &&
                !manager.HasBuffer<DroneStationReservedSlotElement>(storage))
                manager.AddBuffer<DroneStationReservedSlotElement>(storage);
    }

    private static bool PreferNonSupplyCandidate(EntityManager manager, in DroneLogisticsTask task,
        in DroneTaskCandidateDecisionElement candidate, in DroneTaskCandidateDecisionElement previous)
    {
        if (candidate.Task != previous.Task)
            return DroneSchedulingUtility.EarlierTask(manager, candidate.Task, previous.Task);
        if (task.DemolitionRoot == Entity.Null) return false;
        float distance = DroneSchedulingUtility.ReadCurrentRouteResult(manager, candidate.RouteRequest).TotalDistance;
        float previousDistance = DroneSchedulingUtility.ReadCurrentRouteResult(manager, previous.RouteRequest).TotalDistance;
        return distance < previousDistance || (distance == previousDistance &&
            DroneSchedulingUtility.ComparePlacement(manager, candidate.Destination, previous.Destination) < 0);
    }

    private static bool CanReservePickupRoute(EntityManager manager, Entity registry, in DroneLogisticsTask task,
        in DroneTaskCandidateDecisionElement candidate, int quantity, ref DroneDemolitionUtility.RefundFlightReadContext flightReads) =>
        task.DemolitionRoot == Entity.Null || !manager.HasComponent<DroneStation>(candidate.Destination) ||
        DroneDemolitionStorageUtility.TestPickupRoute(manager, registry, candidate, quantity, ref flightReads);

    private static bool PrefersEarlierWork(EntityManager manager, NativeList<DroneTaskCandidateDecisionElement> candidates,
        in DroneTaskCandidateDecisionElement member, Entity registry, int capacity, NativeHashSet<Entity> claimed,
        NativeHashSet<Entity> rejectedCrews, NativeHashMap<Entity, int> remainingTransport,
        ref DroneDemolitionUtility.RefundFlightReadContext flightReads)
    {
        Entity supplyHead = Entity.Null;
        foreach (var candidate in candidates)
        {
            if (candidate.Worker != member.Worker || candidate.Task == member.Task) continue;
            var task = manager.GetComponentData<DroneLogisticsTask>(candidate.Task);
            if (task.Kind == DroneLogisticsTaskKindEnum.WorldItemRecovery && claimed.Contains(candidate.Source)) continue;
            if (task.Kind == DroneLogisticsTaskKindEnum.BuildingDemolition &&
                (claimed.Contains(task.Target) || rejectedCrews.Contains(candidate.Task))) continue;
            if (task.Kind != DroneLogisticsTaskKindEnum.ConstructionSupply &&
                !DroneSchedulingUtility.EarlierTask(manager, candidate.Task, member.Task)) continue;
            bool limited = remainingTransport.TryGetValue(candidate.Task, out int remaining);
            if (limited && remaining <= 0) continue;
            int quantity = task.Kind == DroneLogisticsTaskKindEnum.BuildingDemolition
                ? DroneDemolitionUtility.CandidateQuantity(manager, candidate, registry, capacity)
                : DroneSchedulingUtility.CandidateQuantity(manager, candidate, registry, capacity);
            if (limited) quantity = math.min(quantity, remaining);
            if (quantity <= 0 || !CanReservePickupRoute(manager, registry, task, candidate, quantity, ref flightReads)) continue;
            if (task.Kind == DroneLogisticsTaskKindEnum.ConstructionSupply)
            {
                if (DroneSchedulingUtility.EarlierTask(manager, candidate.Task, supplyHead)) supplyHead = candidate.Task;
            }
            else
            {
                return true;
            }
        }
        return supplyHead != Entity.Null && DroneSchedulingUtility.EarlierTask(manager, supplyHead, member.Task);
    }

    // 분기마다 현재 남은 후보를 다시 모으되, 같은 집결 탐색 안에서는 작업 메모리를 재사용한다.
    private struct RefundSearchBounds : System.IDisposable
    {
        private NativeHashMap<Entity, FixedBitSet> _destinations;
        private NativeHashMap<Entity, FixedBitSet> _workerOptions;
        private NativeHashMap<Entity, int> _workerLimits;
        private NativeList<int> _minimumByType;

        public RefundSearchBounds(Allocator allocator)
        {
            _destinations = new NativeHashMap<Entity, FixedBitSet>(8, allocator);
            _workerOptions = new NativeHashMap<Entity, FixedBitSet>(8, allocator);
            _workerLimits = new NativeHashMap<Entity, int>(8, allocator);
            _minimumByType = new NativeList<int>(allocator);
        }

        public bool CanFill(EntityManager manager, Entity registry,
            NativeList<DroneTaskCandidateDecisionElement> candidates, NativeList<DroneDemolitionRefundElement> costs,
            int firstItem, int firstQuantity, int firstCandidate, NativeHashSet<Entity> used)
        {
            _destinations.Clear();
            _workerOptions.Clear();
            _minimumByType.Clear();
            bool multipleTypes = firstItem + 1 < costs.Length;
            long requiredWorkers = 0;
            for (int item = firstItem; item < costs.Length; item++)
            {
                var type = costs[item].ItemType;
                int required = item == firstItem ? firstQuantity : costs[item].Quantity;
                _workerLimits.Clear();
                int largest = 0;
                long available = 0;
                for (int c = item == firstItem ? firstCandidate : 0; c < candidates.Length; c++)
                {
                    var candidate = candidates[c];
                    if (candidate.ItemType != type || used.Contains(candidate.Worker)) continue;
                    if (multipleTypes)
                    {
                        _workerOptions.TryGetValue(candidate.Worker, out var workerTypes);
                        workerTypes.Set((byte)type, true);
                        _workerOptions[candidate.Worker] = workerTypes;
                    }
                    _workerLimits.TryGetValue(candidate.Worker, out int previous);
                    if (candidate.Quantity > previous)
                    {
                        available += candidate.Quantity - previous;
                        largest = math.max(largest, candidate.Quantity);
                        _workerLimits[candidate.Worker] = candidate.Quantity;
                    }
                    _destinations.TryGetValue(candidate.Destination, out var allowed);
                    allowed.Set((byte)type, true);
                    _destinations[candidate.Destination] = allowed;
                }
                if (available < required) return false;
                if (multipleTypes)
                {
                    int minimum = MinimumWorkers(required, available, largest);
                    requiredWorkers += minimum;
                    _minimumByType.Add(minimum);
                }
            }
            if (multipleTypes && (_workerOptions.Count < requiredWorkers || !CanMatchWorkers(costs, firstItem))) return false;
            return DroneDemolitionStorageUtility.CanFitRefund(manager, registry, _destinations, costs, firstItem, firstQuantity);
        }

        private int MinimumWorkers(int required, long available, int largest)
        {
            if (available == (long)_workerLimits.Count * largest) return 1 + (required - 1) / largest;
            using var limits = _workerLimits.GetValueArray(Allocator.Temp);
            limits.Sort();
            int count = 0;
            for (int i = limits.Length - 1; required > 0; i--)
            {
                required -= limits[i];
                count++;
            }
            return count;
        }

        private bool CanMatchWorkers(NativeList<DroneDemolitionRefundElement> costs, int firstItem)
        {
            using var options = _workerOptions.GetValueArray(Allocator.Temp);
            using var owners = new NativeArray<ItemTypeEnum>(options.Length, Allocator.Temp);
            for (int i = 0; i < _minimumByType.Length; i++)
            {
                var type = costs[firstItem + i].ItemType;
                for (int j = 0; j < _minimumByType[i]; j++)
                {
                    FixedBitSet visited = default;
                    if (!DroneDemolitionUtility.TryMatchRefundUnit(type, options, owners, ref visited)) return false;
                }
            }
            return true;
        }

        public void Dispose()
        {
            _minimumByType.Dispose();
            _workerLimits.Dispose();
            _workerOptions.Dispose();
            _destinations.Dispose();
        }
    }

    private static bool TryReserveRefundCrew(EntityManager manager, Entity registry, int capacity,
        Entity root, NativeList<DroneTaskCandidateDecisionElement> candidates, NativeList<DroneDemolitionRefundElement> costs,
        int itemIndex, int remaining, int firstCandidate, NativeHashSet<Entity> used,
        NativeList<DroneTaskCandidateDecisionElement> crew, RefundSearchBounds bounds,
        ref DroneDemolitionUtility.RefundFlightReadContext flightReads)
    {
        if (itemIndex == costs.Length)
        {
            // 예약이 마지막 빈 정박 공간을 차지하면 기존 경로 결과가 도달 가능해도 사용할 수 없다.
            foreach (var member in crew)
                if (!DroneDemolitionUtility.CanReachRefundStorage(manager, member.Worker, member.Source, member.Destination, ref flightReads))
                    return false;
            return true;
        }
        if (!bounds.CanFill(manager, registry, candidates, costs, itemIndex, remaining, firstCandidate, used))
            return false;

        for (int i = firstCandidate; i < candidates.Length; i++)
        {
            var candidate = candidates[i];
            if (candidate.ItemType != costs[itemIndex].ItemType || used.Contains(candidate.Worker)) continue;
            int quantity = math.min(remaining, DroneDemolitionUtility.CandidateQuantity(manager, candidate, registry, capacity));
            while (quantity > 0 && DroneDemolitionStorageUtility.TryReserveRefund(manager, candidate.Destination, registry,
                root, candidate.Worker, candidate.ItemType, quantity, out int withoutLastEmptySlot))
            {
                // 재귀가 읽는 정거장 목록은 현재 분기에서 확보한 슬롯을 반영한다.
                flightReads.RefreshDockAvailability(manager, candidate.Destination);
                var member = candidate;
                member.Quantity = quantity;
                crew.Add(member);
                used.Add(member.Worker);
                int next = quantity == remaining ? itemIndex + 1 : itemIndex;
                int nextQuantity = remaining - quantity;
                if (next > itemIndex && next < costs.Length) nextQuantity = costs[next].Quantity;
                if (TryReserveRefundCrew(manager, registry, capacity, root, candidates, costs, next, nextQuantity,
                    next == itemIndex ? i + 1 : 0, used, crew, bounds, ref flightReads)) return true;

                DroneDemolitionStorageUtility.ResizeClaim(manager, member.Destination, member.Worker, 0);
                flightReads.RefreshDockAvailability(manager, member.Destination);
                used.Remove(member.Worker);
                crew.RemoveAt(crew.Length - 1);
                // 마지막 빈 슬롯을 다른 품목이나 충전에 남기는 감량만 별도 배치가 된다.
                quantity = withoutLastEmptySlot;
            }
        }
        return false;
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
            if (pending.HasRetargetStorageClaim)
                DroneDemolitionStorageUtility.ResizeClaim(manager, pending.Candidate.Destination, pending.Candidate.Worker, 0);
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
        using var ordered = new NativeList<RetargetCandidateOrder>(Allocator.Temp);
        // 목적 작업이 바뀌어도 최초 작업 생성 순번을 유지하여 오래 운반 중인 자재부터 목적지를 확보한다.
        for (int i = 0; i < candidates.Length; i++)
        {
            Entity assignment = candidates[i].Assignment;
            if (assignment == Entity.Null)
            {
                continue;
            }
            ulong creationSequence = ulong.MaxValue;
            if (manager.HasComponent<DroneTaskAssignment>(assignment))
            {
                creationSequence = DroneSchedulingUtility.AssignmentCreationSequence(manager,
                    manager.GetComponentData<DroneTaskAssignment>(assignment));
            }
            ordered.Add(new RetargetCandidateOrder
            {
                CreationSequence = creationSequence,
                AssignmentIndex = assignment.Index,
                CandidateIndex = i
            });
        }
        ordered.AsArray().Sort();
        for (int i = 0; i < ordered.Length; i++)
        {
            var candidate = candidates[ordered[i].CandidateIndex];
            if (usedWorkers.Contains(candidate.Worker))
            {
                continue;
            }
            int quantity = DroneSchedulingUtility.CandidateQuantity(manager, candidate, registry, capacity);
            if (quantity <= 0)
            {
                continue;
            }
            int committedQuantity = 0;
            if (candidate.NextAction == DroneActionKindEnum.SupplyConstructionSite)
            {
                committedQuantity = ConstructionSupplyReservationUtility.Reserve(manager,
                    candidate.Destination, candidate.ItemType, quantity);
                quantity = committedQuantity;
            }
            if (quantity <= 0)
            {
                continue;
            }
            bool demolitionCargo = candidate.NextAction == DroneActionKindEnum.StoreCargo &&
                manager.GetComponentData<DroneCargoState>(candidate.Worker).Origin == DroneCargoOriginEnum.DemolitionRecovery;
            if (demolitionCargo && !DroneDemolitionStorageUtility.TryReserveTransport(manager, registry, candidate, quantity))
                continue;
            pendingPublications.Add(new DroneTaskPendingPublicationElement
            {
                Candidate = candidate,
                Quantity = quantity,
                CommittedQuantity = committedQuantity,
                HasRetargetStorageClaim = demolitionCargo
            });
            usedWorkers.Add(candidate.Worker);
        }
    }

    private struct RetargetCandidateOrder : System.IComparable<RetargetCandidateOrder>
    {
        public ulong CreationSequence;
        public int AssignmentIndex;
        public int CandidateIndex;

        public int CompareTo(RetargetCandidateOrder other)
        {
            int comparison = CreationSequence.CompareTo(other.CreationSequence);
            if (comparison != 0)
            {
                return comparison;
            }
            comparison = AssignmentIndex.CompareTo(other.AssignmentIndex);
            if (comparison != 0)
            {
                return comparison;
            }
            // 같은 배정의 대안은 Decision이 전달한 순서대로 시도한다.
            return CandidateIndex.CompareTo(other.CandidateIndex);
        }
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
                if (Progress.TryGetComponent(reservation.Task, out var progress))
                {
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
                if (!Progress.TryGetComponent(reservation.Task, out var progress))
                {
                    continue;
                }
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
