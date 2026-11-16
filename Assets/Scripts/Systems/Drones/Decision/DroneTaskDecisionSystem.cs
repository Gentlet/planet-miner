using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: Decision에서 필요한 공급/회수 작업, 무효화, 경로 평가와 배정 후보를 판단한다.
/// 입력: Command 반영 뒤의 현장 요구량, 보관품/공간, 기존 작업/배정과 수행자 관측. 경로는 이번 틱 내부 판단을 우선 읽고 없으면 기존 공개 결과를 읽는다.
/// 출력·소유권: 공통 후보 엔티티의 생성/무효화/경로/후보 버퍼만 작성한다. 작업·실물·예약 원본은 쓰지 않는다.
/// 이용: Reservation이 후보와 현장 수량을 중재하고 Execution이 생성/경로 의도를 소비한다.
/// 정리·가시화: 후보는 매 틱 Clear한다. 별도 공개 대기 예약은 읽기만 하며 Reservation의 정산까지 보존한다.
/// 작업·경로 엔티티는 Execution의 EndSimulation 기록이 재생된 뒤 다음 Decision에서 조회한다.
/// </summary>
[UpdateInGroup(typeof(DroneDecisionGroup))]
public partial struct DroneTaskDecisionSystem : ISystem
{
    private Entity _candidates;
    private EntityQuery _tasks;
    private EntityQuery _sites;
    private EntityQuery _storages;
    private EntityQuery _workers;
    private EntityQuery _routes;
    private EntityQuery _assignments;
    private EntityQuery _transportReservations;
    private EntityQuery _registry;
    private EntityQuery _capacity;
    private EntityQuery _taskRequests;
    private EntityQuery _stations;
    private uint _evaluationRevision;

    public void OnCreate(ref SystemState state)
    {
        _candidates = DroneSchedulingUtility.GetOrCreateCandidates(state.EntityManager);
        _taskRequests = state.GetEntityQuery(typeof(DroneTaskRequest), typeof(DroneTaskRequestDecision));
        _tasks = state.GetEntityQuery(ComponentType.ReadOnly<DroneLogisticsTask>());
        _sites = state.GetEntityQuery(ComponentType.ReadOnly<ConstructionSite>(), ComponentType.ReadOnly<GridPosition>());
        _storages = state.GetEntityQuery(ComponentType.ReadOnly<BuildingType>(), ComponentType.ReadOnly<Storage>(),
            ComponentType.ReadOnly<StoredItemElement>(), ComponentType.ReadOnly<GridPosition>());
        _workers = state.GetEntityQuery(ComponentType.ReadOnly<DroneWorker>(), ComponentType.ReadOnly<DroneWorkerObservation>(),
            ComponentType.ReadOnly<DroneWorkerAssignment>(), ComponentType.ReadOnly<DroneCargoState>(), ComponentType.ReadOnly<StoredItemElement>());
        _routes = state.GetEntityQuery(ComponentType.ReadOnly<DroneRouteEvaluationRequest>());
        _assignments = state.GetEntityQuery(ComponentType.ReadOnly<DroneTaskAssignment>());
        _transportReservations = state.GetEntityQuery(ComponentType.ReadOnly<DroneTransportReservation>());
        _registry = state.GetEntityQuery(ComponentType.ReadOnly<ItemRegistry>(), ComponentType.ReadOnly<ItemConfigElement>());
        _capacity = state.GetEntityQuery(ComponentType.ReadOnly<DroneCapacityState>());
        _stations = state.GetEntityQuery(ComponentType.ReadOnly<DroneStation>(), ComponentType.ReadOnly<DroneStationNetworkDecision>());
        _creationResearch = state.GetEntityQuery(ComponentType.ReadOnly<ResearchRecoveryElement>(),
            ComponentType.ReadOnly<StoredItemElement>(), ComponentType.ReadOnly<ResearchCycleState>());
        _creationSites = state.GetEntityQuery(
            ComponentType.ReadOnly<ConstructionSite>(),
            ComponentType.ReadOnly<GridPosition>(),
            ComponentType.ReadOnly<ConstructionMaterialRequirementElement>());
        _creationItems = state.GetEntityQuery(ComponentType.ReadOnly<ItemIdentity>(),
            ComponentType.ReadOnly<ItemOwnership>(), ComponentType.ReadOnly<GridPosition>());
        _creationFootprints = state.GetEntityQuery(ComponentType.ReadOnly<ConstructionSite>(),
            ComponentType.ReadOnly<GridPosition>(), ComponentType.ReadOnly<BuildingFootprint>(),
            ComponentType.ReadOnly<Direction>());
    }

    public void OnUpdate(ref SystemState state)
    {
        state.CompleteDependency();
        var manager = state.EntityManager;
        int carryingCapacity = DroneSchedulingUtility.ReadCarryingCapacity(_capacity);
        manager.GetBuffer<DroneTaskCandidateDecisionElement>(_candidates).Clear();
        var routeDecisions = manager.GetBuffer<DroneRouteDecisionElement>(_candidates);
        routeDecisions.Clear();
        var invalidationDecisions = manager.GetBuffer<DroneTaskInvalidationDecisionElement>(_candidates);
        invalidationDecisions.Clear();
        var demolitionDecisions = manager.GetBuffer<DroneDemolitionDecisionElement>(_candidates);
        demolitionDecisions.Clear();
        using var tasks = _tasks.ToEntityArray(Allocator.Temp);
        using var taskReads = new NativeArray<TaskReadSnapshot>(tasks.Length, Allocator.Temp);
        using var sites = _sites.ToEntityArray(Allocator.Temp);
        using var storages = _storages.ToEntityArray(Allocator.Temp);
        using var workers = _workers.ToEntityArray(Allocator.Temp);
        using var routes = _routes.ToEntityArray(Allocator.Temp);
        using var assignments = _assignments.ToEntityArray(Allocator.Temp);
        using var transportReservations = _transportReservations.ToEntityArray(Allocator.Temp);
        // ToComponentDataArray는 같은 틱 정거장 Decision Writer 완료 뒤 준비된 결과만 읽는다.
        using var networks = _stations.ToComponentDataArray<DroneStationNetworkDecision>(Allocator.Temp);
        var transportPlans = manager.GetBuffer<DroneTransportPlanDecisionElement>(_candidates);
        transportPlans.Clear();
        DroneTaskRequestDecisionUtility.Prepare(manager, _candidates, _taskRequests, tasks, sites, assignments);
        // Prepare가 취소 결정을 모두 기록한 뒤, 같은 Decision 호출 안에서만 읽는다.
        var cancellations = new DroneTaskRequestDecisionUtility.CancellationReadContext(manager, _candidates);
        BuildCreationDecisions(manager, tasks, taskReads, ref cancellations);
        using var busyWorkers = new NativeHashSet<Entity>(16, Allocator.Temp);
        using var claimedRecoveryItems = new NativeHashSet<Entity>(16, Allocator.Temp);
        using var usedRoutes = new NativeHashSet<Entity>(16, Allocator.Temp);

        // 무효 작업/배정을 신규 수집 후보에서 제외하되 여기서 원본을 닫거나 연결을 끊지 않는다.
        // 예약 해제는 Reservation, 종료 상태와 삭제 보류는 Lifecycle Apply가 각 소유 경계에서 처리한다.
        for (int t = 0; t < tasks.Length; t++)
        {
            if (cancellations.IsPlanned(tasks[t])) continue;
            var task = ReadTask(manager, tasks, taskReads, t);
            if (task.Kind == DroneLogisticsTaskKindEnum.BuildingDemolition &&
                manager.HasComponent<BuildingDemolitionResult>(tasks[t]))
            {
                invalidationDecisions.Add(new DroneTaskInvalidationDecisionElement
                {
                    Kind = DroneTaskInvalidationDecisionKindEnum.FinishDemolitionTask, Target = tasks[t]
                });
                continue;
            }
            if (task.State == DroneLogisticsTaskStateEnum.Open && !DroneSchedulingUtility.IsTaskValid(manager, task, sites))
            {
                invalidationDecisions.Add(new DroneTaskInvalidationDecisionElement
                {
                    Kind = DroneTaskInvalidationDecisionKindEnum.CloseTask,
                    Target = tasks[t]
                });
            }
            else if (task.State == DroneLogisticsTaskStateEnum.Open && DroneSchedulingUtility.IsTransportKind(task.Kind) &&
                DroneTransportReservationUtility.IsCompleted(manager.GetComponentData<DroneTransportRequirement>(tasks[t]),
                    manager.GetComponentData<DroneTransportProgress>(tasks[t])))
            {
                invalidationDecisions.Add(new DroneTaskInvalidationDecisionElement
                {
                    Kind = DroneTaskInvalidationDecisionKindEnum.CompleteTransportTask,
                    Target = tasks[t]
                });
            }
        }

        for (int a = 0; a < assignments.Length; a++)
        {
            var assignment = manager.GetComponentData<DroneTaskAssignment>(assignments[a]);
            if (!DroneSchedulingUtility.IsTerminal(assignment.State) &&
                cancellations.IsPlanned(assignment.Task))
            {
                invalidationDecisions.Add(new DroneTaskInvalidationDecisionElement
                {
                    Kind = DroneTaskInvalidationDecisionKindEnum.CancelAssignment,
                    Target = assignments[a],
                    AssignmentRevision = assignment.Revision
                });
                continue;
            }
            if (DroneSchedulingUtility.AssignmentNeedsCleanup(manager, assignments[a], assignment, sites))
            {
                invalidationDecisions.Add(new DroneTaskInvalidationDecisionElement
                {
                    Kind = DroneTaskInvalidationDecisionKindEnum.InvalidateAssignment,
                    Target = assignments[a],
                    AssignmentRevision = assignment.Revision
                });
                continue;
            }
            busyWorkers.Add(assignment.Worker);
            if (assignment.NextAction == DroneActionKindEnum.DemolishBuilding)
            {
                uint observed = manager.GetComponentData<DroneWorkerObservation>(assignment.Worker).Revision;
                if (DroneDemolitionUtility.CanConfirm(manager, assignments[a], assignment.Revision, observed))
                    demolitionDecisions.Add(new DroneDemolitionDecisionElement
                    {
                        Assignment = assignments[a], AssignmentRevision = assignment.Revision,
                        WorkerObservationRevision = observed
                    });
            }
            if (assignment.NextAction == DroneActionKindEnum.RecoverWorldItem) claimedRecoveryItems.Add(assignment.Source);
        }
        using var routesByKey = new NativeHashMap<RouteRequestKey, Entity>(math.max(1, routes.Length), Allocator.Temp);
        IndexCurrentRoutes(manager, routes, routesByKey, carryingCapacity);

        Entity registry = _registry.IsEmptyIgnoreFilter ? Entity.Null : _registry.GetSingletonEntity();
        var candidates = manager.GetBuffer<DroneTaskCandidateDecisionElement>(_candidates);
        var pendingPublications = manager.GetBuffer<DroneTaskPendingPublicationElement>(_candidates);
        var pendingTransport = manager.GetBuffer<DroneTransportPendingReservationElement>(_candidates, true);
        // 이미 적재한 자재의 목적지를 먼저 판단한다. 최종 선택·수량 확보 순서는 Reservation이 정한다.
        BuildRetargetCandidates(manager, networks, assignments, tasks, taskReads, sites, storages, registry, routesByKey,
            routeDecisions, usedRoutes, candidates, pendingPublications, carryingCapacity, ref cancellations);
        using var storagesByNetwork = new StorageCandidatesByNetwork(manager, storages, networks);
        for (int w = 0; w < workers.Length; w++)
        {
            Entity worker = workers[w];
            if (busyWorkers.Contains(worker) || !DroneSchedulingUtility.IsIdleWorker(manager, worker, carryingCapacity)) continue;
            var observation = manager.GetComponentData<DroneWorkerObservation>(worker);
            Entity network = DroneTaskNetworkUtility.FindWorkerNetwork(networks, observation.Position);
            if (network == Entity.Null)
            {
                continue;
            }
            for (int t = 0; t < tasks.Length; t++)
            {
                var task = ReadTask(manager, tasks, taskReads, t);
                if (cancellations.IsPlanned(tasks[t]))
                {
                    continue;
                }
                if (!DroneSchedulingUtility.IsTaskValid(manager, task, sites)) continue;
                // 정거장 재배치는 다른 망으로 이관할 수 있으므로 그 정거장의 마무리 철거만 망 간 접근을 허용한다.
                if (!DroneStationRelocationUtility.IsStationDemolition(manager, task) &&
                    !DroneTaskNetworkUtility.IsTargetInNetwork(manager, networks, network, task.Target))
                {
                    continue;
                }
                bool transport = DroneSchedulingUtility.IsTransportKind(task.Kind);
                if (task.Kind == DroneLogisticsTaskKindEnum.BuildingDemolition)
                {
                    if (task.State != DroneLogisticsTaskStateEnum.Open) continue;
                    bool assigned = false;
                    foreach (Entity owner in assignments)
                    {
                        var current = manager.GetComponentData<DroneTaskAssignment>(owner);
                        assigned |= current.Task == tasks[t] && !DroneSchedulingUtility.IsTerminal(current.State);
                    }
                    if (assigned) continue;
                    Entity direct = FindOrRequestRoute(manager, worker, observation, Entity.Null, task.Target,
                        routesByKey, routeDecisions, usedRoutes, demolition: true);
                    if (direct == Entity.Null)
                    {
                        continue;
                    }
                    if (!DroneSchedulingUtility.TryReadRoute(manager, direct, carryingCapacity, out var directResult))
                    {
                        continue;
                    }
                    if (directResult.Status != DroneRouteEvaluationStatusEnum.Reachable)
                    {
                        continue;
                    }
                    candidates.Add(new DroneTaskCandidateDecisionElement
                    {
                        Worker = worker, Task = tasks[t], Destination = task.Target,
                        RouteRequest = direct, ItemType = ItemTypeEnum.None,
                        WorkerObservationRevision = observation.Revision, Quantity = 1
                    });
                    continue;
                }
                bool supply = task.Kind == DroneLogisticsTaskKindEnum.ConstructionSupply;
                bool sourceIsStorage = supply || task.Kind == DroneLogisticsTaskKindEnum.StorageImport;
                if (transport && !sourceIsStorage && DroneTransportEndpointUtility.SourceQuantity(manager, task, task.Target) <= 0)
                {
                    continue;
                }
                if (transport && sourceIsStorage && DroneTransportEndpointUtility.DestinationSpace(manager, task, task.Target, registry, 1) <= 0)
                {
                    continue;
                }
                if (supply && DroneSchedulingUtility.ProjectedRemaining(manager, task.Target, task.ItemType, assignments, sites, pendingPublications) <= 0) continue;
                if (!supply && !transport && claimedRecoveryItems.Contains(task.Target)) continue;

                bool waitingForRoutes = false;
                Entity bestStorage = Entity.Null;
                Entity bestRoute = Entity.Null;
                float bestDistance = float.MaxValue;
                // 공급은 재고가 있는 공급원을, 회수는 보관 가능한 목적지를 비교한다.
                // 빈 드론의 ViaSource 결과는 드론→공급원/월드 실물→목적지의 전체 실제 이동거리다.
                var networkStorages = storagesByNetwork.GetStorages(network);
                for (int s = 0; s < networkStorages.Length; s++)
                {
                    Entity storage = networkStorages[s];
                    if (storage == task.Target)
                    {
                        continue;
                    }
                    if (transport)
                    {
                        int available = sourceIsStorage
                            ? DroneTransportEndpointUtility.SourceQuantity(manager, task, storage)
                            : DroneTransportEndpointUtility.DestinationSpace(manager, task, storage, registry, 1);
                        if (available <= 0)
                        {
                            continue;
                        }
                    }
                    else if (sourceIsStorage)
                    {
                        if (DroneSchedulingUtility.CountInventory(manager, storage, task.ItemType) <= 0)
                        {
                            continue;
                        }
                    }
                    else if (!DroneSchedulingUtility.HasStorageSpace(manager, storage, task.ItemType, registry))
                    {
                        continue;
                    }

                    Entity source = sourceIsStorage ? storage : task.Target;
                    Entity destination = sourceIsStorage ? task.Target : storage;
                    Entity route = FindOrRequestRoute(manager, worker, observation, source, destination,
                        routesByKey, routeDecisions, usedRoutes);
                    if (route == Entity.Null)
                    {
                        waitingForRoutes = true;
                        continue;
                    }

                    if (!DroneSchedulingUtility.TryReadRoute(manager, route, carryingCapacity, out var routeResult))
                    {
                        waitingForRoutes = true;
                        continue;
                    }

                    if (routeResult.Status != DroneRouteEvaluationStatusEnum.Reachable) continue;
                    float distance = routeResult.TotalDistance;
                    if (bestStorage == Entity.Null || distance < bestDistance ||
                        (distance == bestDistance && DroneSchedulingUtility.ComparePlacement(manager, storage, bestStorage) < 0))
                    {
                        bestStorage = storage;
                        bestRoute = route;
                        bestDistance = distance;
                    }
                }

                // 미평가 대상을 도달 불가로 간주하면 더 가까운 공급원/보관처를 놓치므로 결과를 모두 기다린다.
                if (waitingForRoutes || bestStorage == Entity.Null) continue;
                int quantity = supply
                    ? math.min(carryingCapacity,
                        math.min(DroneSchedulingUtility.ProjectedRemaining(manager, task.Target, task.ItemType, assignments, sites, pendingPublications),
                            DroneSchedulingUtility.CountInventory(manager, bestStorage, task.ItemType)))
                    : 1;
                Entity selectedSource = sourceIsStorage ? bestStorage : task.Target;
                Entity selectedDestination = sourceIsStorage ? task.Target : bestStorage;
                if (transport)
                {
                    quantity = math.min(carryingCapacity, DroneTransportReservationUtility.ProjectedRemaining(
                        tasks[t], manager.GetComponentData<DroneTransportRequirement>(tasks[t]),
                        manager.GetComponentData<DroneTransportProgress>(tasks[t]), pendingTransport,
                        DroneTransportReservationUtility.ReadReleasingQuantity(manager, tasks[t], transportReservations, invalidationDecisions)));
                    quantity = math.min(quantity, DroneTransportEndpointUtility.SourceQuantity(manager, task, selectedSource));
                    quantity = DroneTransportEndpointUtility.DestinationSpace(manager, task, selectedDestination, registry, quantity);
                }
                if (quantity <= 0) continue;
                var candidate = new DroneTaskCandidateDecisionElement
                {
                    Worker = worker,
                    Task = tasks[t],
                    Source = selectedSource,
                    Destination = selectedDestination,
                    SourceArea = transport && task.Kind == DroneLogisticsTaskKindEnum.StorageExport ? task.Area : default,
                    DestinationArea = transport ? DroneTransportEndpointUtility.DestinationArea(task) : default,
                    RouteRequest = bestRoute,
                    ItemType = task.ItemType,
                    WorkerObservationRevision = observation.Revision,
                    Quantity = quantity
                };
                if (transport)
                {
                    // 일반 운반의 작업 내 수량 중재로 전달한다. 기존 공급/회수 배정 Publish와는 분리한다.
                    transportPlans.Add(new DroneTransportPlanDecisionElement { Candidate = candidate });
                }
                else
                {
                    candidates.Add(candidate);
                }
            }
        }

        // 기존 경로의 유지/제거도 명시적인 의도로 전달한다. Execution이 사용 여부를 재탐색하지 않는다.
        for (int r = 0; r < routes.Length; r++)
        {
            routeDecisions.Add(new DroneRouteDecisionElement
            {
                Kind = usedRoutes.Contains(routes[r]) ? DroneRouteDecisionKindEnum.Retain : DroneRouteDecisionKindEnum.Remove,
                ExistingRequest = routes[r]
            });
        }
    }

    // 이번 호출에서 읽은 망과 창고의 회전 범위만 사용한다. 창고는 겹치는 각 망의 목록에 독립적으로 들어간다.
    private readonly struct StorageCandidatesByNetwork : IDisposable
    {
        private readonly EntityManager _manager;
        private readonly NativeArray<Entity> _storages;
        private readonly NativeArray<DroneStationNetworkDecision> _stations;
        private readonly NativeList<DroneStationActivityBounds> _storageBounds;
        private readonly NativeList<DroneStationActivityBounds> _networkBounds;
        private readonly NativeList<Entity> _candidates;
        private readonly NativeHashMap<Entity, int2> _ranges;

        public StorageCandidatesByNetwork(EntityManager manager, NativeArray<Entity> storages,
            NativeArray<DroneStationNetworkDecision> stations)
        {
            _manager = manager;
            _storages = storages;
            _stations = stations;
            _storageBounds = new NativeList<DroneStationActivityBounds>(Allocator.Temp);
            _networkBounds = new NativeList<DroneStationActivityBounds>(Allocator.Temp);
            _candidates = new NativeList<Entity>(Allocator.Temp);
            _ranges = new NativeHashMap<Entity, int2>(1, Allocator.Temp);
        }

        // 반환한 목록은 다른 망의 후보를 추가하기 전에 순회한다.
        public NativeArray<Entity> GetStorages(Entity network)
        {
            if (!_ranges.TryGetValue(network, out int2 range))
            {
                int start = _candidates.Length;
                AppendCandidates(network);
                range = new int2(start, _candidates.Length - start);
                _ranges.Add(network, range);
            }
            return _candidates.AsArray().GetSubArray(range.x, range.y);
        }

        private void AppendCandidates(Entity network)
        {
            if (_storageBounds.IsEmpty)
            {
                foreach (Entity storage in _storages)
                {
                    _storageBounds.Add(DroneTaskNetworkUtility.ReadTargetBounds(_manager, storage));
                }
            }

            _networkBounds.Clear();
            foreach (var station in _stations)
            {
                if (station.RepresentativeStation == network)
                {
                    _networkBounds.Add(station.ActivityBounds);
                }
            }

            // 창고 쿼리 순서를 보존하고, 같은 망에 속한 정거장이 여러 개 겹쳐도 한 번만 추가한다.
            for (int i = 0; i < _storages.Length; i++)
            {
                var bounds = _storageBounds[i];
                foreach (var stationBounds in _networkBounds)
                {
                    if (stationBounds.Overlaps(bounds))
                    {
                        _candidates.Add(_storages[i]);
                        break;
                    }
                }
            }
        }

        public void Dispose()
        {
            _ranges.Dispose();
            _candidates.Dispose();
            _networkBounds.Dispose();
            _storageBounds.Dispose();
        }
    }

    private static void IndexCurrentRoutes(EntityManager manager, NativeArray<Entity> routes,
        NativeHashMap<RouteRequestKey, Entity> routesByKey, int carryingCapacity)
    {
        // 현재 관측과 경로 결과가 일치하는 요청만 넣고, 키가 같으면 쿼리에서 처음 만난 요청을 유지한다.
        // 후보 판단 동안 이 원본은 바뀌지 않는다. 실제 삭제와 생성은 Execution 의도를 거친다.
        for (int i = 0; i < routes.Length; i++)
        {
            Entity route = routes[i];
            var request = manager.GetComponentData<DroneRouteEvaluationRequest>(route);
            if (DroneSchedulingUtility.IsRouteInvalid(manager, route, request, carryingCapacity))
            {
                continue;
            }
            var key = new RouteRequestKey(request.Worker, request.Source, request.Destination, request.Assignment,
                request.AssignmentRevision, request.IsDropPositionSearch);
            routesByKey.TryAdd(key, route);
        }
    }

    private readonly struct RouteRequestKey : IEquatable<RouteRequestKey>
    {
        private readonly Entity _worker;
        private readonly Entity _source;
        private readonly Entity _destination;
        private readonly Entity _assignment;
        private readonly uint _assignmentRevision;
        private readonly bool _dropPositionSearch;

        public RouteRequestKey(Entity worker, Entity source, Entity destination, Entity assignment,
            uint assignmentRevision, bool dropPositionSearch)
        {
            _worker = worker;
            _source = source;
            _destination = destination;
            _assignment = assignment;
            _assignmentRevision = assignmentRevision;
            _dropPositionSearch = dropPositionSearch;
        }

        public bool Equals(RouteRequestKey other) =>
            _worker == other._worker && _source == other._source && _destination == other._destination &&
            _assignment == other._assignment && _assignmentRevision == other._assignmentRevision &&
            _dropPositionSearch == other._dropPositionSearch;

        public override int GetHashCode() =>
            HashCode.Combine(_worker, _source, _destination, _assignment, _assignmentRevision, _dropPositionSearch);
    }

    private Entity FindOrRequestRoute(EntityManager manager, Entity worker, DroneWorkerObservation observation,
        Entity source, Entity destination, NativeHashMap<RouteRequestKey, Entity> routesByKey,
        DynamicBuffer<DroneRouteDecisionElement> routeDecisions, NativeHashSet<Entity> usedRoutes,
        Entity assignment = default, uint assignmentRevision = 0, bool dropPositionSearch = false, bool demolition = false)
    {
        // Null 값은 이번 판단에서 생성 의도만 작성한 요청이다. EndSimulation 공개 전에는 사용하지 않는다.
        var key = new RouteRequestKey(worker, source, destination, assignment, assignmentRevision, dropPositionSearch);
        if (routesByKey.TryGetValue(key, out Entity route))
        {
            if (route != Entity.Null)
            {
                usedRoutes.Add(route);
            }
            return route;
        }

        _evaluationRevision++;
        if (_evaluationRevision == 0) _evaluationRevision++;
        var next = new DroneRouteEvaluationRequest
        {
            Worker = worker,
            Assignment = assignment,
            AssignmentRevision = assignmentRevision,
            EvaluationRevision = _evaluationRevision,
            WorkerObservationRevision = observation.Revision,
            Kind = demolition ? DroneRouteKindEnum.DirectToBuilding :
                assignment == Entity.Null ? DroneRouteKindEnum.ViaSource : DroneRouteKindEnum.Direct,
            IsDropPositionSearch = dropPositionSearch,
            OriginPosition = observation.Position,
            Source = source,
            SourcePosition = source == Entity.Null ? int2.zero : manager.GetComponentData<GridPosition>(source).Value,
            Destination = destination,
            DestinationPosition = destination == Entity.Null ? int2.zero : manager.GetComponentData<GridPosition>(destination).Value
        };
        routesByKey.Add(key, Entity.Null);
        routeDecisions.Add(new DroneRouteDecisionElement { Kind = DroneRouteDecisionKindEnum.Create, Request = next });
        return Entity.Null;
    }

    private enum CargoDestinationSelectionEnum : byte
    {
        NoDestination,
        Waiting,
        Selected
    }

    private void BuildRetargetCandidates(EntityManager manager, NativeArray<DroneStationNetworkDecision> networks, NativeArray<Entity> assignments,
        NativeArray<Entity> tasks, NativeArray<TaskReadSnapshot> taskReads, NativeArray<Entity> sites, NativeArray<Entity> storages, Entity registry,
        NativeHashMap<RouteRequestKey, Entity> routesByKey, DynamicBuffer<DroneRouteDecisionElement> routeDecisions,
        NativeHashSet<Entity> usedRoutes, DynamicBuffer<DroneTaskCandidateDecisionElement> candidates,
        DynamicBuffer<DroneTaskPendingPublicationElement> pendingPublications, int capacity,
        ref DroneTaskRequestDecisionUtility.CancellationReadContext cancellations)
    {
        // 공급원에서 가져온 적재품은 필요한 현장→보관처 순서로 찾는다.
        // 월드 회수품은 보관처만 찾으며, 보관되기 전에는 건설 현장에 직접 공급하지 않는다.
        for (int i = 0; i < assignments.Length; i++)
        {
            Entity entity = assignments[i];
            if (!DroneSchedulingUtility.IsRetargetingWorker(manager, entity, out var assignment, out int cargo)) continue;
            if (cancellations.IsPlanned(assignment.Task)) continue;
            var observation = manager.GetComponentData<DroneWorkerObservation>(assignment.Worker);
            Entity network = DroneTaskNetworkUtility.FindWorkerNetwork(networks, observation.Position);
            if (network == Entity.Null)
            {
                continue;
            }
            var origin = manager.GetComponentData<DroneCargoState>(assignment.Worker).Origin;
            var selection = CargoDestinationSelectionEnum.NoDestination;
            Entity destination = Entity.Null;
            Entity route = Entity.Null;
            Entity task = assignment.Task;
            var action = DroneActionKindEnum.DropCargo;
            int2 dropPosition = int2.zero;
            // 이미 운반 중인 실물은 공통 적재량 변경으로 줄이지 않는다. 목적지 필요량만 상한으로 삼는다.
            int quantity = cargo;
            if (origin == DroneCargoOriginEnum.Supply)
            {
                selection = FindCargoDestination(manager, networks, network, entity, assignment, observation, sites, true, assignments, sites,
                    registry, routesByKey, routeDecisions, usedRoutes, pendingPublications, capacity,
                    out destination, out route);
                if (selection == CargoDestinationSelectionEnum.Waiting) continue;
                if (selection == CargoDestinationSelectionEnum.Selected)
                {
                    task = FindSupplyTask(manager, tasks, taskReads, destination, assignment.ItemType, ref cancellations);
                    // 새 현장의 작업은 EndSimulation에 게시될 수 있다. 게시 전에는 보관으로 우회하지 않는다.
                    if (task == Entity.Null) continue;
                    action = DroneActionKindEnum.SupplyConstructionSite;
                    quantity = math.min(cargo, DroneSchedulingUtility.ProjectedRemaining(manager, destination,
                        assignment.ItemType, assignments, sites, pendingPublications));
                }
            }
            if (selection == CargoDestinationSelectionEnum.NoDestination)
            {
                selection = FindCargoDestination(manager, networks, network, entity, assignment, observation, storages, false, assignments, sites,
                    registry, routesByKey, routeDecisions, usedRoutes, pendingPublications, capacity,
                    out destination, out route);
                if (selection == CargoDestinationSelectionEnum.Waiting) continue;
                if (selection == CargoDestinationSelectionEnum.Selected) action = DroneActionKindEnum.StoreCargo;
            }
            if (action == DroneActionKindEnum.DropCargo && !TrySelectDropPosition(manager, networks, network, entity, assignment, observation,
                routesByKey, routeDecisions, usedRoutes, capacity, out route, out dropPosition)) continue;
            if (quantity <= 0) continue;
            candidates.Add(new DroneTaskCandidateDecisionElement
            {
                Worker = assignment.Worker,
                Assignment = entity,
                AssignmentRevision = assignment.Revision,
                NextAction = action,
                DropPosition = dropPosition,
                Task = task,
                Destination = destination,
                RouteRequest = route,
                ItemType = assignment.ItemType,
                WorkerObservationRevision = observation.Revision,
                Quantity = quantity
            });
        }
    }

    private bool TrySelectDropPosition(EntityManager manager, NativeArray<DroneStationNetworkDecision> networks,
        Entity network, Entity entity, in DroneTaskAssignment assignment, in DroneWorkerObservation observation,
        NativeHashMap<RouteRequestKey, Entity> routesByKey, DynamicBuffer<DroneRouteDecisionElement> routeDecisions,
        NativeHashSet<Entity> usedRoutes, int capacity, out Entity route, out int2 position)
    {
        route = Entity.Null;
        position = (int2)math.floor(observation.Position.xy);
        // 현재 셀이 현장 밖이면 그대로 방출 목표로 삼는다. 안쪽이면 외부 평가자에게 도달 가능한 외부 셀을 요청한다.
        // 선택 목표는 이동 결과가 아니며 실제 방출은 Lifecycle이 관측/요청 위치와 목표 일치를 다시 검사한다.
        if (ConstructionSiteWorldItemUtility.IsWorldPositionAllowed(manager, position)) return true;

        route = FindOrRequestRoute(manager, assignment.Worker, observation, Entity.Null, Entity.Null,
            routesByKey, routeDecisions, usedRoutes, entity, assignment.Revision, true);
        if (route == Entity.Null) return false;
        if (!DroneSchedulingUtility.TryReadRoute(manager, route, capacity, out var result)) return false;
        if (result.Status != DroneRouteEvaluationStatusEnum.Reachable) return false;
        if (!result.HasDropPosition) return false;
        // 현재 셀은 호출자의 네트워크 자격을 통과했다. 외부 평가 좌표도 같은 망 안이어야 한다.
        if (!DroneTaskNetworkUtility.IsCellInNetwork(networks, network, result.DropPosition))
        {
            return false;
        }
        if (!ConstructionSiteWorldItemUtility.IsWorldPositionAllowed(manager, result.DropPosition)) return false;
        position = result.DropPosition;
        return true;
    }

    private CargoDestinationSelectionEnum FindCargoDestination(EntityManager manager, NativeArray<DroneStationNetworkDecision> networks, Entity network, Entity entity,
        in DroneTaskAssignment assignment, in DroneWorkerObservation observation, NativeArray<Entity> destinations, bool supply,
        NativeArray<Entity> assignments, NativeArray<Entity> sites, Entity registry,
        NativeHashMap<RouteRequestKey, Entity> routesByKey, DynamicBuffer<DroneRouteDecisionElement> routeDecisions,
        NativeHashSet<Entity> usedRoutes,
        DynamicBuffer<DroneTaskPendingPublicationElement> pendingPublications, int capacity,
        out Entity bestDestination, out Entity bestRoute)
    {
        bestDestination = Entity.Null;
        bestRoute = Entity.Null;
        float bestDistance = float.MaxValue;
        bool waiting = false;
        bool waitingForEnergy = false;
        // 적재품은 공급원을 다시 거치지 않는 Direct 경로를 비교한다.
        // 경로 평가가 남으면 보관/방출로 우회하지 않고 기다리며, 완료 결과의 거리 동률은 PlacementStamp로 푼다.
        for (int i = 0; i < destinations.Length; i++)
        {
            Entity destination = destinations[i];
            if (!DroneTaskNetworkUtility.IsTargetInNetwork(manager, networks, network, destination))
            {
                continue;
            }
            if (supply)
            {
                if (!DroneSchedulingUtility.IsSite(manager, destination)) continue;
                if (DroneTaskRequestDecisionUtility.IsBlocked(manager, _candidates,
                    DroneTaskGenerationKey.From(DroneLogisticsTaskKindEnum.ConstructionSupply, destination, assignment.ItemType)))
                {
                    continue;
                }
                if (DroneSchedulingUtility.ProjectedRemaining(manager, destination, assignment.ItemType,
                    assignments, sites, pendingPublications) <= 0) continue;
            }
            else if (!DroneSchedulingUtility.HasStorageSpace(manager, destination, assignment.ItemType, registry)) continue;
            Entity route = FindOrRequestRoute(manager, assignment.Worker, observation, Entity.Null, destination,
                routesByKey, routeDecisions, usedRoutes, entity, assignment.Revision);
            if (route == Entity.Null)
            {
                waiting = true;
                continue;
            }
            if (!DroneSchedulingUtility.TryReadRoute(manager, route, capacity, out var routeResult))
            {
                waiting = true;
                continue;
            }
            if (routeResult.Status != DroneRouteEvaluationStatusEnum.Reachable)
            {
                waitingForEnergy |= routeResult.Status == DroneRouteEvaluationStatusEnum.WaitingForEnergy;
                continue;
            }
            float distance = routeResult.TotalDistance;
            if (bestDestination == Entity.Null || distance < bestDistance ||
                (distance == bestDistance && DroneSchedulingUtility.ComparePlacement(manager, destination, bestDestination) < 0))
            {
                bestDestination = destination;
                bestRoute = route;
                bestDistance = distance;
            }
        }
        if (waiting) return CargoDestinationSelectionEnum.Waiting;
        if (bestDestination == Entity.Null && waitingForEnergy)
        {
            return CargoDestinationSelectionEnum.Waiting;
        }
        return bestDestination == Entity.Null ? CargoDestinationSelectionEnum.NoDestination : CargoDestinationSelectionEnum.Selected;
    }

    // 같은 Decision 호출의 작업 원본만 재사용하고 취소 후보는 각 이용 시점에 확인한다.
    private struct TaskReadSnapshot
    {
        public bool HasRead;
        public DroneLogisticsTask Task;
    }

    private static DroneLogisticsTask ReadTask(EntityManager manager, NativeArray<Entity> tasks,
        NativeArray<TaskReadSnapshot> reads, int index)
    {
        var snapshot = reads[index];
        if (!snapshot.HasRead)
        {
            snapshot.Task = manager.GetComponentData<DroneLogisticsTask>(tasks[index]);
            snapshot.HasRead = true;
            reads[index] = snapshot;
        }
        return snapshot.Task;
    }

    private Entity FindSupplyTask(EntityManager manager, NativeArray<Entity> tasks, NativeArray<TaskReadSnapshot> taskReads,
        Entity site, ItemTypeEnum type, ref DroneTaskRequestDecisionUtility.CancellationReadContext cancellations)
    {
        for (int i = 0; i < tasks.Length; i++)
        {
            if (cancellations.IsPlanned(tasks[i]))
            {
                continue;
            }
            var task = ReadTask(manager, tasks, taskReads, i);
            if (task.Kind == DroneLogisticsTaskKindEnum.ConstructionSupply && task.State == DroneLogisticsTaskStateEnum.Open &&
                task.Target == site && task.ItemType == type) return tasks[i];
        }
        return Entity.Null;
    }

    private EntityQuery _creationSites;
    private EntityQuery _creationItems;
    private EntityQuery _creationFootprints;
    private EntityQuery _creationResearch;

    private void BuildCreationDecisions(EntityManager manager, NativeArray<Entity> taskEntities,
        NativeArray<TaskReadSnapshot> taskReads, ref DroneTaskRequestDecisionUtility.CancellationReadContext cancellations)
    {
        var decisions = manager.GetBuffer<DroneTaskCreationDecisionElement>(_candidates);
        decisions.Clear();
        using var tasks = new NativeList<DroneLogisticsTask>(Allocator.Temp);
        for (int i = 0; i < taskEntities.Length; i++)
        {
            if (!cancellations.IsPlanned(taskEntities[i]))
            {
                tasks.Add(ReadTask(manager, taskEntities, taskReads, i));
            }
        }
        BuildSupplyCreationDecisions(manager, tasks.AsArray(), decisions);
        BuildRecoveryCreationDecisions(manager, tasks.AsArray(), decisions);
        BuildResearchRecoveryCreationDecisions(manager, tasks.AsArray(), decisions);
    }

    private void BuildResearchRecoveryCreationDecisions(EntityManager manager,
        NativeArray<DroneLogisticsTask> tasks, DynamicBuffer<DroneTaskCreationDecisionElement> decisions)
    {
        // Closed 작업도 배정 정리가 끝날 때까지 같은 목적의 새 root를 막는다.
        using var existingKeys = new NativeList<DroneTaskGenerationKey>(Allocator.Temp);
        foreach (var task in tasks)
        {
            if (task.Kind == DroneLogisticsTaskKindEnum.StorageExport &&
                task.Area == DroneTransportAreaEnum.Input && task.Purpose == DroneTransportPurposeEnum.ResearchRecovery)
            {
                existingKeys.Add(DroneTaskRequestDecisionUtility.Key(task));
            }
        }
        using var buildings = _creationResearch.ToEntityArray(Allocator.Temp);
        foreach (Entity building in buildings)
        {
            if (!ResearchRecoveryUtility.IsSource(manager, building))
            {
                continue;
            }
            using var types = new NativeHashSet<int>(8, Allocator.Temp);
            var sourceRead = new ResearchRecoveryUtility.ValidatedSourceReadContext(manager, building);
            foreach (var item in sourceRead.StoredItems)
            {
                if (!types.Add((int)item.ItemType))
                {
                    continue;
                }
                int quantity = sourceRead.Count(item.ItemType);
                var key = DroneTaskGenerationKey.From(DroneLogisticsTaskKindEnum.StorageExport, building, item.ItemType,
                    DroneTransportAreaEnum.Input, DroneTransportPurposeEnum.ResearchRecovery);
                if (quantity == 0 || DroneTaskRequestDecisionUtility.IsBlocked(manager, _candidates, key))
                {
                    continue;
                }
                if (DroneTaskRequestDecisionUtility.ContainsGenerationKey(existingKeys, key))
                {
                    continue;
                }
                decisions.Add(new DroneTaskCreationDecisionElement
                {
                    Kind = DroneLogisticsTaskKindEnum.StorageExport, Target = building,
                    ItemType = item.ItemType, Quantity = quantity,
                    Area = DroneTransportAreaEnum.Input, Purpose = DroneTransportPurposeEnum.ResearchRecovery
                });
                existingKeys.Add(key);
            }
        }
    }

    private void BuildSupplyCreationDecisions(EntityManager manager,
        NativeArray<DroneLogisticsTask> tasks, DynamicBuffer<DroneTaskCreationDecisionElement> decisions)
    {
        // Closed 작업도 연결 배정이 정리될 때까지 살아 있으므로 같은 품목의 새 root를 만들지 않는다.
        using var existingKeys = new NativeList<DroneTaskGenerationKey>(Allocator.Temp);
        for (int i = 0; i < tasks.Length; i++)
        {
            var task = tasks[i];
            if (task.Kind == DroneLogisticsTaskKindEnum.ConstructionSupply)
            {
                existingKeys.Add(DroneTaskRequestDecisionUtility.Key(task));
            }
        }

        using var sites = _creationSites.ToEntityArray(Allocator.Temp);
        // 생성 의도부터 현장 PlacementStamp 순서로 기록한다. 품목마다 root 작업 하나를 두고 드론별 하위 배정을 만든다.
        SortCreationSitesByPlacement(manager, sites);
        for (int i = 0; i < sites.Length; i++)
        {
            Entity site = sites[i];
            if (!DroneSchedulingUtility.IsSite(manager, site))
            {
                continue;
            }

            var requirements = manager.GetBuffer<ConstructionMaterialRequirementElement>(site, true);
            for (int row = 0; row < requirements.Length; row++)
            {
                var requirement = requirements[row];
                if (requirement.ItemType == ItemTypeEnum.None || requirement.IsSatisfied)
                {
                    continue;
                }

                var key = DroneTaskGenerationKey.From(DroneLogisticsTaskKindEnum.ConstructionSupply, site, requirement.ItemType);
                if (DroneTaskRequestDecisionUtility.ContainsGenerationKey(existingKeys, key))
                {
                    continue;
                }
                if (DroneTaskRequestDecisionUtility.IsBlocked(manager, _candidates, key))
                {
                    continue;
                }

                decisions.Add(new DroneTaskCreationDecisionElement
                {
                    Kind = DroneLogisticsTaskKindEnum.ConstructionSupply,
                    Target = site,
                    ItemType = requirement.ItemType,
                    RecoveryReason = DroneRecoveryReasonEnum.None
                });
                existingKeys.Add(key);
            }
        }
    }

    private void BuildRecoveryCreationDecisions(EntityManager manager,
        NativeArray<DroneLogisticsTask> tasks, DynamicBuffer<DroneTaskCreationDecisionElement> decisions)
    {
        // 이전에 방출된 실물과 현재 현장 footprint 안의 월드 실물만 회수 작업으로 만든다.
        // 취소 현장은 제외하고 방향을 한 번 적용하여 회수 필요 영역과 완공 차단 영역을 맞춘다.
        using var footprints = new NativeList<ConstructionSiteWorldItemUtility.Footprint>(Allocator.Temp);
        using var footprintSites = _creationFootprints.ToEntityArray(Allocator.Temp);
        for (int i = 0; i < footprintSites.Length; i++)
        {
            Entity site = footprintSites[i];
            if ((manager.GetComponentData<ConstructionSite>(site).Flags & ConstructionSiteFlags.Cancelled) != 0)
            {
                continue;
            }

            footprints.Add(new ConstructionSiteWorldItemUtility.Footprint
            {
                Origin = manager.GetComponentData<GridPosition>(site).Value,
                Size = BuildingFootprintUtility.GetEffectiveSize(
                    manager.GetComponentData<BuildingFootprint>(site).Size,
                    manager.GetComponentData<Direction>(site).dir)
            });
        }

        using var claimedItems = new NativeHashSet<Entity>(16, Allocator.Temp);
        for (int i = 0; i < tasks.Length; i++)
        {
            var task = tasks[i];
            if (task.Kind == DroneLogisticsTaskKindEnum.WorldItemRecovery &&
                task.State == DroneLogisticsTaskStateEnum.Open)
            {
                claimedItems.Add(task.Target);
            }
        }

        using var items = _creationItems.ToEntityArray(Allocator.Temp);
        for (int i = 0; i < items.Length; i++)
        {
            Entity item = items[i];
            var itemType = manager.GetComponentData<ItemIdentity>(item).Type;
            if (!DroneSchedulingUtility.IsWorldItem(manager, item, itemType))
            {
                continue;
            }

            if (claimedItems.Contains(item))
            {
                continue;
            }

            bool hasRecovery = manager.HasComponent<DroneRecoveryPending>(item);
            var reason = hasRecovery
                ? manager.GetComponentData<DroneRecoveryPending>(item).Reason
                : DroneRecoveryReasonEnum.None;
            if (reason == DroneRecoveryReasonEnum.None && hasRecovery)
                reason = DroneRecoveryReasonEnum.DroneDrop;
            if (reason == DroneRecoveryReasonEnum.None &&
                !ConstructionSiteWorldItemUtility.IsWorldPositionAllowed(footprints.AsArray(), manager.GetComponentData<GridPosition>(item).Value))
            {
                reason = DroneRecoveryReasonEnum.SiteClearance;
            }

            if (reason == DroneRecoveryReasonEnum.None)
            {
                continue;
            }
            if (DroneTaskRequestDecisionUtility.IsBlocked(manager, _candidates,
                DroneTaskGenerationKey.From(DroneLogisticsTaskKindEnum.WorldItemRecovery, item, itemType)))
            {
                continue;
            }

            decisions.Add(new DroneTaskCreationDecisionElement
            {
                Kind = DroneLogisticsTaskKindEnum.WorldItemRecovery,
                Target = item,
                ItemType = itemType,
                RecoveryReason = reason
            });
            claimedItems.Add(item);
        }
    }

    private static void SortCreationSitesByPlacement(EntityManager manager, NativeArray<Entity> sites)
    {
        for (int i = 1; i < sites.Length; i++)
        {
            Entity site = sites[i];
            int destination = i;
            while (destination > 0 && DroneSchedulingUtility.ComparePlacement(manager, site, sites[destination - 1]) < 0)
            {
                sites[destination] = sites[destination - 1];
                destination--;
            }

            sites[destination] = site;
        }
    }
}
