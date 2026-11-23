using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: Decision에서 필요한 공급/회수 작업, 무효화, 경로 평가와 배정 후보를 판단한다.
/// 입력: Command 반영 뒤의 현장 요구량, 보관품/공간, 기존 작업/배정과 수행자 관측. 경로는 이번 틱 내부 판단을 우선 읽고 없으면 기존 공개 결과를 읽는다.
/// 출력·소유권: 공통 생성/무효화/경로/배정 후보와 철거 root의 진행 표시를 작성한다. 실제 수량 확보와 인계는 후속 소유자가 처리한다.
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
    private EntityQuery _buildingCosts;
    private EntityQuery _taskRequests;
    private EntityQuery _stations;
    private uint _evaluationRevision;

    public void OnCreate(ref SystemState state)
    {
        _candidates = DroneSchedulingUtility.GetOrCreateCandidates(state.EntityManager);
        _taskRequests = state.GetEntityQuery(typeof(DroneTaskRequest), typeof(DroneTaskRequestDecision));
        _tasks = state.GetEntityQuery(ComponentType.ReadOnly<DroneLogisticsTask>());
        _sites = state.GetEntityQuery(ComponentType.ReadOnly<ConstructionSite>(), ComponentType.ReadOnly<GridPosition>());
        _storages = state.GetEntityQuery(ComponentType.ReadOnly<BuildingIdentity>(), ComponentType.ReadOnly<Storage>(),
            ComponentType.ReadOnly<StorageSlotElement>(), ComponentType.ReadOnly<GridPosition>());
        _workers = state.GetEntityQuery(ComponentType.ReadOnly<DroneWorker>(), ComponentType.ReadOnly<DroneWorkerObservation>(),
            ComponentType.ReadOnly<DroneWorkerAssignment>(), ComponentType.ReadOnly<DroneCargoState>());
        _routes = state.GetEntityQuery(ComponentType.ReadOnly<DroneRouteEvaluationRequest>());
        _assignments = state.GetEntityQuery(ComponentType.ReadOnly<DroneTaskAssignment>());
        _transportReservations = state.GetEntityQuery(ComponentType.ReadOnly<DroneTransportReservation>());
        _registry = state.GetEntityQuery(ComponentType.ReadOnly<ItemRegistry>(), ComponentType.ReadOnly<ItemConfigElement>());
        _capacity = state.GetEntityQuery(ComponentType.ReadOnly<DroneCapacityState>());
        _buildingCosts = state.GetEntityQuery(ComponentType.ReadOnly<BuildingConstructionMaterialElement>());
        _stations = state.GetEntityQuery(ComponentType.ReadOnly<DroneStation>(), ComponentType.ReadOnly<DroneStationNetworkDecision>());
        _creationResearch = state.GetEntityQuery(ComponentType.ReadOnly<ResearchRecoveryElement>(),
            ComponentType.ReadOnly<StorageSlotElement>(), ComponentType.ReadOnly<ResearchCycleState>());
        _creationSites = state.GetEntityQuery(
            ComponentType.ReadOnly<ConstructionSite>(),
            ComponentType.ReadOnly<GridPosition>(),
            ComponentType.ReadOnly<ConstructionMaterialRequirementElement>());
        _creationItems = state.GetEntityQuery(ComponentType.ReadOnly<ItemIdentity>(),
            ComponentType.ReadOnly<GridPosition>());
        _creationPendingRecoveryItems = state.GetEntityQuery(ComponentType.ReadOnly<ItemIdentity>(),
            ComponentType.ReadOnly<GridPosition>(), ComponentType.ReadOnly<DroneRecoveryPending>());
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
        // ToComponentDataArray는 같은 틱 정거장 Decision Writer 완료 뒤 준비된 결과만 읽는다.
        using var networks = _stations.ToComponentDataArray<DroneStationNetworkDecision>(Allocator.Temp);
        var transportPlans = manager.GetBuffer<DroneTransportPlanDecisionElement>(_candidates);
        transportPlans.Clear();
        DroneTaskRequestDecisionUtility.Prepare(manager, _candidates, _taskRequests, tasks, sites, assignments);
        // Prepare가 취소 결정을 모두 기록한 뒤, 같은 Decision 호출 안에서만 읽는다.
        var cancellations = new DroneTaskRequestDecisionUtility.CancellationReadContext(manager, _candidates);
        BuildCreationDecisions(ref state, tasks, taskReads, ref cancellations);
        Entity config = _buildingCosts.IsEmptyIgnoreFilter ? Entity.Null : _buildingCosts.GetSingletonEntity();
        Entity registry = _registry.IsEmptyIgnoreFilter ? Entity.Null : _registry.GetSingletonEntity();
        using var networkBounds = new NativeParallelMultiHashMap<Entity, DroneStationActivityBounds>(math.max(1, networks.Length), Allocator.Temp);
        using var storagesByNetwork = new StorageCandidatesByNetwork(manager, storages);
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
                manager.HasComponent<BuildingDemolitionResult>(tasks[t]) &&
                !DroneDemolitionUtility.IsCommittedPickup(manager, tasks[t], task))
                continue;
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

        using var invalidCrews = new NativeHashSet<Entity>(4, Allocator.Temp);
        using var demolitionCrewCounts = new NativeHashMap<Entity, (int Assigned, int Arrived)>(4, Allocator.Temp);
        for (int a = 0; a < assignments.Length; a++)
        {
            var assignment = manager.GetComponentData<DroneTaskAssignment>(assignments[a]);
            bool terminal = DroneSchedulingUtility.IsTerminal(assignment.State);
            bool demolition = assignment.NextAction == DroneActionKindEnum.DemolishBuilding;
            // 취소·무효화 의도보다 먼저 공개된 배정을 세어 기존 진행 상태의 시점을 유지한다.
            if (demolition && !terminal) CountDemolitionCrewAssignment(demolitionCrewCounts, assignment);
            if (!terminal &&
                (cancellations.IsPlanned(assignment.Task) ||
                 ((assignment.NextAction == DroneActionKindEnum.CollectFromStorage ||
                   assignment.NextAction == DroneActionKindEnum.RecoverWorldItem) &&
                  DroneDemolitionUtility.IsDemolitionChildCancellationPlanned(manager, _candidates, assignment.Task))))
            {
                invalidationDecisions.Add(new DroneTaskInvalidationDecisionElement
                {
                    Kind = DroneTaskInvalidationDecisionKindEnum.CancelAssignment,
                    Target = assignments[a],
                    AssignmentRevision = assignment.Revision
                });
                continue;
            }
            if (DroneSchedulingUtility.AssignmentNeedsCleanup(manager, assignments[a], assignment, sites, registry))
            {
                if (demolition && !terminal) invalidCrews.Add(assignment.Task);
                invalidationDecisions.Add(new DroneTaskInvalidationDecisionElement
                {
                    Kind = DroneTaskInvalidationDecisionKindEnum.InvalidateAssignment,
                    Target = assignments[a],
                    AssignmentRevision = assignment.Revision
                });
                continue;
            }
            busyWorkers.Add(assignment.Worker);
            if (demolition)
            {
                uint observed = manager.GetComponentData<DroneWorkerObservation>(assignment.Worker).Revision;
                if (DroneDemolitionUtility.CanConfirm(manager, assignments[a], assignment.Revision, observed))
                {
                    if (assignment.ItemType != ItemTypeEnum.None &&
                        !DroneDemolitionUtility.CanReachRefundStorage(manager, assignment.Worker, assignment.Source,
                            assignment.Destination, arrived: true))
                        invalidCrews.Add(assignment.Task);
                    else
                        demolitionDecisions.Add(new DroneDemolitionDecisionElement
                        {
                            Assignment = assignments[a], AssignmentRevision = assignment.Revision,
                            WorkerObservationRevision = observed
                        });
                }
            }
            if (assignment.NextAction == DroneActionKindEnum.RecoverWorldItem) claimedRecoveryItems.Add(assignment.Source);
        }
        if (!invalidCrews.IsEmpty)
        {
            foreach (Entity entity in assignments)
            {
                var assignment = manager.GetComponentData<DroneTaskAssignment>(entity);
                if (assignment.NextAction == DroneActionKindEnum.DemolishBuilding &&
                    !DroneSchedulingUtility.IsTerminal(assignment.State) && invalidCrews.Contains(assignment.Task))
                    invalidationDecisions.Add(new DroneTaskInvalidationDecisionElement
                    {
                        Kind = DroneTaskInvalidationDecisionKindEnum.CancelAssignment,
                        Target = entity, AssignmentRevision = assignment.Revision
                    });
            }
        }
        UpdateDemolitionProgress(manager, tasks, demolitionCrewCounts.AsReadOnly(), networks, storagesByNetwork, networkBounds,
            config, registry, carryingCapacity);

        using var routesByKey = new NativeHashMap<RouteRequestKey, Entity>(math.max(1, routes.Length), Allocator.Temp);
        IndexCurrentRoutes(manager, routes, routesByKey, workers.Length, carryingCapacity);

        var candidates = manager.GetBuffer<DroneTaskCandidateDecisionElement>(_candidates);
        var pendingPublications = manager.GetBuffer<DroneTaskPendingPublicationElement>(_candidates);
        var pendingTransport = manager.GetBuffer<DroneTransportPendingReservationElement>(_candidates, true);
        // 재배정이 먼저 판단하며 망별 창고 목록은 같은 호출의 신규 후보와 공유한다.
        BuildRetargetCandidates(manager, networks, assignments, tasks, taskReads, sites, storagesByNetwork,
            networkBounds, registry, routesByKey, routeDecisions, usedRoutes, candidates, pendingPublications,
            carryingCapacity, ref cancellations);
        using var tasksByNetwork = new TaskCandidatesByNetwork(Allocator.Temp);
        using var candidateReads = new TaskCandidateReads(manager, registry);
        using var returningTransport = new NativeHashMap<Entity, long>(16, Allocator.Temp);
        bool collectedTransportReturns = false;
        using var pickupDestinations = new NativeList<PickupDestination>(Allocator.Temp);
        bool collectedNewTasks = false;
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
            if (!collectedNewTasks)
            {
                DroneTaskNetworkUtility.CollectNetworkBoundsIfNeeded(networks, networkBounds);
                tasksByNetwork.Collect(manager, tasks, taskReads, sites, ref cancellations);
                collectedNewTasks = true;
            }
            foreach (int t in tasksByNetwork.GetTasks(network, networkBounds))
            {
                var task = taskReads[t].Task;
                if (task.DemolitionRoot != Entity.Null && cancellations.IsPlanned(task.DemolitionRoot)) continue;
                bool transport = DroneSchedulingUtility.IsTransportKind(task.Kind);
                if (task.Kind == DroneLogisticsTaskKindEnum.BuildingDemolition)
                {
                    Entity root = tasks[t];
                    if (!manager.HasComponent<DroneDemolitionProgress>(root)) continue;
                    var progress = manager.GetComponentData<DroneDemolitionProgress>(root);
                    if (progress.AwaitingSpace || progress.RequiredWorkers <= 0 || progress.AssignedWorkers != 0) continue;
                    using var costs = DroneDemolitionUtility.ReadRefunds(manager, root, task, config);
                    if (costs.IsEmpty)
                    {
                        Entity direct = FindOrRequestRoute(manager, worker, observation, Entity.Null, task.Target,
                            routesByKey, routeDecisions, usedRoutes, demolition: true);
                        if (direct != Entity.Null &&
                            DroneSchedulingUtility.TryReadRoute(manager, direct, carryingCapacity, out var result) &&
                            result.Status == DroneRouteEvaluationStatusEnum.Reachable)
                            candidates.Add(new DroneTaskCandidateDecisionElement
                            {
                                Worker = worker, Task = root, Destination = task.Target, RouteRequest = direct,
                                WorkerObservationRevision = observation.Revision, Quantity = 1
                            });
                        continue;
                    }

                    var refundStorages = storagesByNetwork.GetStorages(network, networkBounds.AsReadOnly());
                    foreach (Entity store in refundStorages)
                    {
                        if (store == task.Target) continue;
                        foreach (var cost in costs)
                        {
                            int refundQuantity = DroneSchedulingUtility.CountStorageSpace(manager, store, cost.ItemType,
                                registry, math.min(carryingCapacity, cost.Quantity), generalOnly: true);
                            if (refundQuantity == 0) continue;
                            Entity route = FindOrRequestRoute(manager, worker, observation, task.Target, store,
                                routesByKey, routeDecisions, usedRoutes, demolitionRoot: root);
                            if (route == Entity.Null ||
                                !DroneSchedulingUtility.TryReadRoute(manager, route, carryingCapacity, out var result) ||
                                result.Status != DroneRouteEvaluationStatusEnum.Reachable) continue;
                            candidates.Add(new DroneTaskCandidateDecisionElement
                            {
                                Worker = worker, Task = root, Source = task.Target, Destination = store,
                                ItemType = cost.ItemType, Quantity = refundQuantity, RouteRequest = route,
                                WorkerObservationRevision = observation.Revision
                            });
                        }
                    }
                    continue;
                }
                bool supply = task.Kind == DroneLogisticsTaskKindEnum.ConstructionSupply;
                bool sourceIsStorage = supply || task.Kind == DroneLogisticsTaskKindEnum.StorageImport;
                if (transport && !sourceIsStorage && candidateReads.GetSourceQuantity(task, task.Target) <= 0)
                {
                    continue;
                }
                if (transport && sourceIsStorage && candidateReads.GetDestinationSpace(task, task.Target, 1) <= 0)
                {
                    continue;
                }
                int transportQuantity = 0;
                if (transport)
                {
                    if (!collectedTransportReturns)
                    {
                        using var transportReservations = _transportReservations.ToEntityArray(Allocator.Temp);
                        DroneTransportReservationUtility.CollectReturningQuantities(manager, transportReservations,
                            pendingTransport, invalidationDecisions, returningTransport);
                        collectedTransportReturns = true;
                    }
                    // 이번 Reservation에서 반환할 양까지 반영해 새 배정에 쓸 잔량이 있을 때만 경로를 준비한다.
                    transportQuantity = math.min(carryingCapacity,
                        candidateReads.GetProjectedRemaining(tasks[t], returningTransport.AsReadOnly()));
                    if (transportQuantity <= 0)
                    {
                        continue;
                    }
                }
                if (supply && DroneSchedulingUtility.ProjectedRemaining(manager, task.Target, task.ItemType, assignments, sites, pendingPublications) <= 0) continue;
                if (!supply && !transport && claimedRecoveryItems.Contains(task.Target)) continue;

                bool waitingForRoutes = false;
                pickupDestinations.Clear();
                var nearest = new PickupDestination();
                float bestDistance = float.MaxValue;
                // 공급은 재고가 있는 공급원을, 회수는 보관 가능한 목적지를 비교한다.
                // 빈 드론의 ViaSource 결과는 드론→공급원/월드 실물→목적지의 전체 실제 이동거리다.
                var networkStorages = storagesByNetwork.GetStorages(network, networkBounds.AsReadOnly());
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
                            ? candidateReads.GetSourceQuantity(task, storage)
                            : candidateReads.GetDestinationSpace(task, storage, 1);
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
                    else if (!candidateReads.HasRecoverySpace(storage, task.ItemType))
                    {
                        continue;
                    }

                    Entity source = sourceIsStorage ? storage : task.Target;
                    Entity destination = sourceIsStorage ? task.Target : storage;
                    Entity route = FindOrRequestRoute(manager, worker, observation, source, destination,
                        routesByKey, routeDecisions, usedRoutes, demolitionRoot: task.DemolitionRoot);
                    if (route == Entity.Null)
                    {
                        waitingForRoutes = true;
                        continue;
                    }

                    // 같은 호출의 경로 인덱스가 요청 자격과 결과 revision을 확인했다. 미평가 결과만 기다린다.
                    if (!manager.HasComponent<DroneRouteEvaluationResult>(route))
                    {
                        waitingForRoutes = true;
                        continue;
                    }
                    var routeResult = DroneSchedulingUtility.ReadCurrentRouteResult(manager, route);
                    if (routeResult.Status == DroneRouteEvaluationStatusEnum.None)
                    {
                        waitingForRoutes = true;
                        continue;
                    }
                    if (routeResult.Status != DroneRouteEvaluationStatusEnum.Reachable) continue;
                    float distance = routeResult.TotalDistance;
                    var option = new PickupDestination { Storage = storage, Route = route };
                    if (task.DemolitionRoot != Entity.Null)
                    {
                        // 철거 반출은 실제 슬롯을 확보하면 충전 경유지가 사라질 수 있다. Reservation이 대안을 함께 비교한다.
                        pickupDestinations.Add(option);
                    }
                    else if (nearest.Storage == Entity.Null || distance < bestDistance ||
                        (distance == bestDistance && DroneSchedulingUtility.ComparePlacement(manager, storage, nearest.Storage) < 0))
                    {
                        nearest = option;
                        bestDistance = distance;
                    }
                }

                if (waitingForRoutes) continue;
                if (nearest.Storage != Entity.Null) pickupDestinations.Add(nearest);
                foreach (var option in pickupDestinations)
                {
                    Entity source = sourceIsStorage ? option.Storage : task.Target;
                    Entity destination = sourceIsStorage ? task.Target : option.Storage;
                    int quantity = 1;
                    if (supply)
                    {
                        quantity = math.min(carryingCapacity,
                            math.min(DroneSchedulingUtility.ProjectedRemaining(manager, destination, task.ItemType, assignments, sites, pendingPublications),
                                DroneSchedulingUtility.CountInventory(manager, source, task.ItemType)));
                    }
                    else if (transport)
                    {
                        quantity = math.min(transportQuantity, candidateReads.GetSourceQuantity(task, source));
                        quantity = candidateReads.GetDestinationSpace(task, destination, quantity);
                    }
                    if (quantity <= 0) continue;
                    var candidate = new DroneTaskCandidateDecisionElement
                    {
                        Worker = worker, Task = tasks[t], Source = source, Destination = destination,
                        SourceArea = transport && task.Kind == DroneLogisticsTaskKindEnum.StorageExport ? task.Area : default,
                        DestinationArea = transport ? DroneTransportEndpointUtility.DestinationArea(task) : default,
                        RouteRequest = option.Route, ItemType = task.ItemType,
                        WorkerObservationRevision = observation.Revision, Quantity = quantity
                    };
                    if (transport)
                    {
                        transportPlans.Add(new DroneTransportPlanDecisionElement { Candidate = candidate });
                    }
                    else
                    {
                        candidates.Add(candidate);
                    }
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

    private struct PickupDestination
    {
        public Entity Storage;
        public Entity Route;
    }

    // 이번 Decision의 출고/입고 원본과 무효화 의도는 신규 후보 순회 중에 바뀌지 않는다.
    // 선택 이후 수량 경합과 실제 확보는 Reservation이 처리한다.
    private readonly struct TaskCandidateReads : IDisposable
    {
        private readonly EntityManager _manager;
        private readonly Entity _registry;
        private readonly NativeHashMap<TransportEndpointReadKey, int> _sourceQuantities;
        private readonly NativeHashMap<TransportEndpointReadKey, int> _destinationSpaces;
        private readonly NativeHashMap<Entity, int> _remainingQuantities;
        private readonly NativeHashMap<int3, bool> _recoverySpaces;

        public TaskCandidateReads(EntityManager manager, Entity registry)
        {
            _manager = manager;
            _registry = registry;
            _sourceQuantities = new NativeHashMap<TransportEndpointReadKey, int>(16, Allocator.Temp);
            _destinationSpaces = new NativeHashMap<TransportEndpointReadKey, int>(16, Allocator.Temp);
            _remainingQuantities = new NativeHashMap<Entity, int>(16, Allocator.Temp);
            _recoverySpaces = new NativeHashMap<int3, bool>(16, Allocator.Temp);
        }

        public bool HasRecoverySpace(Entity storage, ItemTypeEnum type)
        {
            var key = new int3(storage.Index, storage.Version, (int)type);
            if (!_recoverySpaces.TryGetValue(key, out bool hasSpace))
            {
                hasSpace = DroneSchedulingUtility.HasStorageSpace(_manager, storage, type, _registry);
                _recoverySpaces.Add(key, hasSpace);
            }
            return hasSpace;
        }

        public int GetSourceQuantity(in DroneLogisticsTask task, Entity source)
        {
            var area = DroneTransportEndpointUtility.SourceArea(task);
            var key = new TransportEndpointReadKey(source, task.ItemType, area, task.Purpose, 0);
            if (!_sourceQuantities.TryGetValue(key, out int quantity))
            {
                quantity = DroneTransportEndpointUtility.SourceQuantity(_manager, task, source);
                _sourceQuantities.Add(key, quantity);
            }
            return quantity;
        }

        public int GetDestinationSpace(in DroneLogisticsTask task, Entity destination, int wanted)
        {
            var area = DroneTransportEndpointUtility.DestinationArea(task);
            var key = new TransportEndpointReadKey(destination, task.ItemType, area, default, wanted);
            if (!_destinationSpaces.TryGetValue(key, out int space))
            {
                space = DroneTransportEndpointUtility.DestinationSpace(_manager, task, destination, _registry, wanted);
                _destinationSpaces.Add(key, space);
            }
            return space;
        }

        public int GetProjectedRemaining(Entity task, NativeHashMap<Entity, long>.ReadOnly returningQuantities)
        {
            if (!_remainingQuantities.TryGetValue(task, out int remaining))
            {
                returningQuantities.TryGetValue(task, out long returning);
                remaining = DroneTransportReservationUtility.ProjectedRemaining(
                    _manager.GetComponentData<DroneTransportRequirement>(task),
                    _manager.GetComponentData<DroneTransportProgress>(task), returning);
                _remainingQuantities.Add(task, remaining);
            }
            return remaining;
        }

        public void Dispose()
        {
            _recoverySpaces.Dispose();
            _remainingQuantities.Dispose();
            _destinationSpaces.Dispose();
            _sourceQuantities.Dispose();
        }
    }

    private readonly struct TransportEndpointReadKey : IEquatable<TransportEndpointReadKey>
    {
        private readonly Entity _owner;
        private readonly ItemTypeEnum _itemType;
        private readonly DroneTransportAreaEnum _area;
        private readonly DroneTransportPurposeEnum _purpose;
        private readonly int _wanted;

        public TransportEndpointReadKey(Entity owner, ItemTypeEnum itemType, DroneTransportAreaEnum area,
            DroneTransportPurposeEnum purpose, int wanted)
        {
            _owner = owner;
            _itemType = itemType;
            _area = area;
            _purpose = purpose;
            _wanted = wanted;
        }

        public bool Equals(TransportEndpointReadKey other) =>
            _owner == other._owner && _itemType == other._itemType && _area == other._area &&
            _purpose == other._purpose && _wanted == other._wanted;

        public override int GetHashCode() => HashCode.Combine(_owner, _itemType, _area, _purpose, _wanted);
    }

    private struct NewTaskNetworkCandidate
    {
        public int TaskIndex;
        public DroneStationActivityBounds Bounds;
        public bool AllowsCrossNetworkAccess;
    }

    private readonly struct TaskCandidatesByNetwork : IDisposable
    {
        private readonly NativeList<NewTaskNetworkCandidate> _eligibleTasks;
        private readonly NativeList<int> _taskIndexes;
        private readonly NativeHashMap<Entity, int2> _ranges;

        public TaskCandidatesByNetwork(Allocator allocator)
        {
            _eligibleTasks = new NativeList<NewTaskNetworkCandidate>(allocator);
            _taskIndexes = new NativeList<int>(allocator);
            _ranges = new NativeHashMap<Entity, int2>(1, allocator);
        }

        public void Collect(EntityManager manager, NativeArray<Entity> tasks, NativeArray<TaskReadSnapshot> taskReads,
            NativeArray<Entity> sites, ref DroneTaskRequestDecisionUtility.CancellationReadContext cancellations)
        {
            for (int t = 0; t < tasks.Length; t++)
            {
                if (cancellations.IsPlanned(tasks[t])) continue;
                var task = ReadTask(manager, tasks, taskReads, t);
                if (!DroneSchedulingUtility.IsTaskValid(manager, task, sites)) continue;

                // 재배치 정거장의 마무리 철거는 현재 활동망 외부에서도 접근할 수 있다.
                bool crossNetwork = DroneStationRelocationUtility.IsStationDemolition(manager, task);
                _eligibleTasks.Add(new NewTaskNetworkCandidate
                {
                    TaskIndex = t,
                    AllowsCrossNetworkAccess = crossNetwork,
                    Bounds = crossNetwork ? default : DroneTaskNetworkUtility.ReadTargetBounds(manager, task.Target)
                });
            }
        }

        // 반환한 범위는 다른 망의 후보를 추가하기 전에 순회한다.
        public NativeArray<int> GetTasks(Entity network,
            NativeParallelMultiHashMap<Entity, DroneStationActivityBounds> networkBounds)
        {
            if (!_ranges.TryGetValue(network, out int2 range))
            {
                int start = _taskIndexes.Length;
                // 망을 넘는 정거장 철거도 원래 작업 쿼리 순서에서 같은 위치를 유지한다.
                foreach (var task in _eligibleTasks)
                {
                    if (task.AllowsCrossNetworkAccess)
                    {
                        _taskIndexes.Add(task.TaskIndex);
                        continue;
                    }
                    foreach (var bounds in networkBounds.GetValuesForKey(network))
                    {
                        if (bounds.Overlaps(task.Bounds))
                        {
                            _taskIndexes.Add(task.TaskIndex);
                            break;
                        }
                    }
                }
                range = new int2(start, _taskIndexes.Length - start);
                _ranges.Add(network, range);
            }
            return _taskIndexes.AsArray().GetSubArray(range.x, range.y);
        }

        public void Dispose()
        {
            _ranges.Dispose();
            _taskIndexes.Dispose();
            _eligibleTasks.Dispose();
        }
    }

    private static void IndexCurrentRoutes(EntityManager manager, NativeArray<Entity> routes,
        NativeHashMap<RouteRequestKey, Entity> routesByKey, int workerCount, int carryingCapacity)
    {
        // 현재 요청과 존재하는 결과를 검증한다. 미평가 요청은 유지하고, 같은 키는 쿼리의 첫 유효 요청을 사용한다.
        // 인덱스 구성 동안 유휴 수행자의 자격/관측과 일반 경로 대상의 원본이 유지되므로 조회를 공유한다.
        int initialCapacity = math.max(1, math.min(workerCount, routes.Length));
        using var idleWorkerObservations = routes.Length > 1
            ? new NativeHashMap<Entity, DroneWorkerObservation>(initialCapacity, Allocator.Temp)
            : default;
        using var endpoints = routes.Length > 1
            ? new NativeHashMap<Entity, DroneSchedulingUtility.RouteEndpoint>(initialCapacity, Allocator.Temp)
            : default;
        for (int i = 0; i < routes.Length; i++)
        {
            Entity route = routes[i];
            var request = manager.GetComponentData<DroneRouteEvaluationRequest>(route);
            if (DroneSchedulingUtility.IsRouteInvalid(manager, route, request, carryingCapacity, idleWorkerObservations, endpoints))
            {
                continue;
            }
            var key = new RouteRequestKey(request.Worker, request.Source, request.Destination, request.Assignment,
                request.AssignmentRevision, request.IsDropPositionSearch, request.DemolitionRoot);
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
        private readonly Entity _demolitionRoot;

        public RouteRequestKey(Entity worker, Entity source, Entity destination, Entity assignment,
            uint assignmentRevision, bool dropPositionSearch, Entity demolitionRoot)
        {
            _worker = worker;
            _source = source;
            _destination = destination;
            _assignment = assignment;
            _assignmentRevision = assignmentRevision;
            _dropPositionSearch = dropPositionSearch;
            _demolitionRoot = demolitionRoot;
        }

        public bool Equals(RouteRequestKey other) =>
            _worker == other._worker && _source == other._source && _destination == other._destination &&
            _assignment == other._assignment && _assignmentRevision == other._assignmentRevision &&
            _dropPositionSearch == other._dropPositionSearch && _demolitionRoot == other._demolitionRoot;

        public override int GetHashCode() =>
            HashCode.Combine(_worker, _source, _destination, _assignment, _assignmentRevision, _dropPositionSearch, _demolitionRoot);
    }

    private Entity FindOrRequestRoute(EntityManager manager, Entity worker, DroneWorkerObservation observation,
        Entity source, Entity destination, NativeHashMap<RouteRequestKey, Entity> routesByKey,
        DynamicBuffer<DroneRouteDecisionElement> routeDecisions, NativeHashSet<Entity> usedRoutes,
        Entity assignment = default, uint assignmentRevision = 0, bool dropPositionSearch = false, bool demolition = false,
        Entity demolitionRoot = default)
    {
        // Null 값은 이번 판단에서 생성 의도만 작성한 요청이다. EndSimulation 공개 전에는 사용하지 않는다.
        var key = new RouteRequestKey(worker, source, destination, assignment, assignmentRevision, dropPositionSearch, demolitionRoot);
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
            DemolitionRoot = demolitionRoot,
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
        NativeArray<Entity> tasks, NativeArray<TaskReadSnapshot> taskReads, NativeArray<Entity> sites,
        in StorageCandidatesByNetwork storagesByNetwork,
        NativeParallelMultiHashMap<Entity, DroneStationActivityBounds> networkBounds, Entity registry,
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
            if (!DroneSchedulingUtility.IsRetargetingWorker(manager, entity, out var assignment, out int cargo, includeDocked: true)) continue;
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
                DroneTaskNetworkUtility.CollectNetworkBoundsIfNeeded(networks, networkBounds);
                var networkStorages = storagesByNetwork.GetStorages(network, networkBounds.AsReadOnly());
                selection = FindCargoDestination(manager, networks, network, entity, assignment, observation, networkStorages, false, assignments, sites,
                    registry, routesByKey, routeDecisions, usedRoutes, pendingPublications, capacity,
                    out destination, out route);
                if (selection == CargoDestinationSelectionEnum.Waiting) continue;
                if (selection == CargoDestinationSelectionEnum.Selected) action = DroneActionKindEnum.StoreCargo;
            }
            if (action == DroneActionKindEnum.DropCargo &&
                (origin == DroneCargoOriginEnum.DemolitionRecovery ||
                 !TrySelectDropPosition(manager, networks, network, entity, assignment, observation,
                     routesByKey, routeDecisions, usedRoutes, capacity, out route, out dropPosition))) continue;
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

    // 창고 후보는 호출자가 해당 망으로 좁힌 목록을 전달한다.
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
        bool bestNeedsDockAtOrigin = false;
        bool demolitionCargo = !supply && manager.GetComponentData<DroneCargoState>(assignment.Worker).Origin ==
            DroneCargoOriginEnum.DemolitionRecovery;
        bool waiting = false;
        bool waitingForEnergy = false;
        // 적재품은 공급원을 다시 거치지 않는 Direct 경로를 비교한다.
        // 경로 평가가 남으면 보관/방출로 우회하지 않고 기다리며, 완료 결과의 거리 동률은 PlacementStamp로 푼다.
        for (int i = 0; i < destinations.Length; i++)
        {
            Entity destination = destinations[i];
            if (supply)
            {
                if (!DroneTaskNetworkUtility.IsTargetInNetwork(manager, networks, network, destination))
                {
                    continue;
                }
                if (!DroneSchedulingUtility.IsSite(manager, destination)) continue;
                if (DroneTaskRequestDecisionUtility.IsBlocked(manager, _candidates,
                    DroneTaskGenerationKey.From(DroneLogisticsTaskKindEnum.ConstructionSupply, destination, assignment.ItemType)))
                {
                    continue;
                }
                if (DroneSchedulingUtility.ProjectedRemaining(manager, destination, assignment.ItemType,
                    assignments, sites, pendingPublications) <= 0) continue;
            }
            else if (demolitionCargo)
            {
                if (DroneDemolitionStorageUtility.AvailableTransportSpace(manager, destination, assignment.ItemType, registry, 1) == 0)
                    continue;
            }
            else if (!DroneSchedulingUtility.HasStorageSpace(manager, destination, assignment.ItemType, registry))
            {
                continue;
            }
            Entity route = FindOrRequestRoute(manager, assignment.Worker, observation, Entity.Null, destination,
                routesByKey, routeDecisions, usedRoutes, entity, assignment.Revision);
            if (route == Entity.Null)
            {
                waiting = true;
                continue;
            }
            if (!DroneSchedulingUtility.TryReadRoute(manager, route, capacity, out var routeResult, out bool needsDockAtOrigin))
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
            // 같은 종류 안에서 바로 이동할 경로를 먼저 고른다. 모두 정박이 필요하면 거리·설치 순서로 고른다.
            bool preferred = bestDestination == Entity.Null || (bestNeedsDockAtOrigin && !needsDockAtOrigin);
            if (!preferred && needsDockAtOrigin == bestNeedsDockAtOrigin)
            {
                preferred = distance < bestDistance ||
                    (distance == bestDistance && DroneSchedulingUtility.ComparePlacement(manager, destination, bestDestination) < 0);
            }
            if (preferred)
            {
                bestNeedsDockAtOrigin = needsDockAtOrigin;
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
    private EntityQuery _creationPendingRecoveryItems;
    private EntityQuery _creationFootprints;
    private EntityQuery _creationResearch;

    private void BuildCreationDecisions(ref SystemState state, NativeArray<Entity> taskEntities,
        NativeArray<TaskReadSnapshot> taskReads, ref DroneTaskRequestDecisionUtility.CancellationReadContext cancellations)
    {
        var manager = state.EntityManager;
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
        BuildRecoveryCreationDecisions(ref state, tasks.AsArray(), decisions);
        BuildResearchRecoveryCreationDecisions(manager, tasks.AsArray(), decisions);
        BuildDemolitionRecoveryCreationDecisions(manager, taskEntities, tasks.AsArray(), decisions, ref cancellations);
    }

    private static void CountDemolitionCrewAssignment(NativeHashMap<Entity, (int Assigned, int Arrived)> crewCounts,
        in DroneTaskAssignment assignment)
    {
        crewCounts.TryGetValue(assignment.Task, out var crew);
        crew.Assigned++;
        if (assignment.State == DroneTaskAssignmentStateEnum.AwaitingDelivery) crew.Arrived++;
        crewCounts[assignment.Task] = crew;
    }

    private void UpdateDemolitionProgress(EntityManager manager, NativeArray<Entity> tasks,
        NativeHashMap<Entity, (int Assigned, int Arrived)>.ReadOnly demolitionCrewCounts,
        NativeArray<DroneStationNetworkDecision> networks, in StorageCandidatesByNetwork storages,
        NativeParallelMultiHashMap<Entity, DroneStationActivityBounds> networkBounds, Entity config, Entity registry, int capacity)
    {
        foreach (Entity root in tasks)
        {
            if (!manager.HasComponent<DroneDemolitionProgress>(root) ||
                !DroneDemolitionUtility.TryGetDemolitionWork(manager, root, out var task)) continue;
            var progress = new DroneDemolitionProgress
            {
                AwaitingRelocation = !DroneDemolitionUtility.CanDrain(manager, task),
                RemainingStock = DroneDemolitionUtility.CountInventory(manager, task.Target)
            };
            if (manager.HasComponent<BeltComponent>(task.Target))
            {
                using var items = _creationItems.ToEntityArray(Allocator.Temp);
                foreach (Entity item in items)
                    if (DroneDemolitionUtility.IsStoppedBeltItem(manager, task.Target, item)) progress.RemainingStock++;
            }
            if (!progress.AwaitingRelocation && progress.RemainingStock == 0)
            {
                using var costs = DroneDemolitionUtility.ReadRefunds(manager, root, task, config);
                progress.RequiredWorkers = DroneDemolitionUtility.RequiredWorkers(costs, capacity);
                if (demolitionCrewCounts.TryGetValue(root, out var crew))
                {
                    progress.AssignedWorkers = crew.Assigned;
                    progress.ArrivedWorkers = crew.Arrived;
                }
                if (progress.AssignedWorkers == 0 && !costs.IsEmpty)
                {
                    using var destinations = DroneTaskNetworkUtility.ReadDemolitionDestinations(manager, task, networks, storages, networkBounds,
                        DroneStationRelocationUtility.IsStationDemolition(manager, task));
                    progress.AwaitingSpace = !DroneDemolitionStorageUtility.CanFitRefund(manager, registry,
                        destinations, costs, 0, costs[0].Quantity);
                }
            }
            else if (!progress.AwaitingRelocation)
            {
                using var destinations = DroneTaskNetworkUtility.ReadDemolitionDestinations(manager, task, networks, storages, networkBounds, false);
                foreach (Entity entity in tasks)
                {
                    var child = manager.GetComponentData<DroneLogisticsTask>(entity);
                    if (child.DemolitionRoot != root || child.State != DroneLogisticsTaskStateEnum.Open ||
                        (child.Kind == DroneLogisticsTaskKindEnum.StorageExport &&
                         DroneTransportEndpointUtility.SourceQuantity(manager, child, child.Target) <= 0)) continue;
                    bool hasSpace = false;
                    foreach (var storage in destinations)
                    {
                        if (DroneSchedulingUtility.CountStorageSpace(manager, storage.Key, child.ItemType, registry,
                            1, generalOnly: true) <= 0) continue;
                        hasSpace = true;
                        break;
                    }
                    progress.AwaitingSpace |= !hasSpace;
                }
            }
            manager.SetComponentData(root, progress);
        }
    }

    private void BuildDemolitionRecoveryCreationDecisions(EntityManager manager, NativeArray<Entity> taskEntities,
        NativeArray<DroneLogisticsTask> tasks, DynamicBuffer<DroneTaskCreationDecisionElement> decisions,
        ref DroneTaskRequestDecisionUtility.CancellationReadContext cancellations)
    {
        NativeArray<Entity> beltItems = default;
        try
        {
            foreach (Entity root in taskEntities)
            {
                if (!DroneDemolitionUtility.TryGetDemolitionWork(manager, root, out var task) ||
                    cancellations.IsPlanned(root) || !DroneDemolitionUtility.CanDrain(manager, task))
                    continue;

                Entity building = task.Target;
                if (manager.HasBuffer<StorageSlotElement>(building))
                {
                    var area = DroneDemolitionUtility.StorageArea(manager, building);
                    var slots = manager.GetBuffer<StorageSlotElement>(building, true);
                    AppendStorageDemolitionExports(manager, tasks, decisions, root, building, area, slots);
                    if (area == DroneTransportAreaEnum.GeneralStorage && manager.HasComponent<FuelStorageSlots>(building))
                        AppendStorageDemolitionExports(manager, tasks, decisions, root, building, DroneTransportAreaEnum.Input, slots);
                }

                if (manager.HasBuffer<ProductSlotElement>(building))
                {
                    using var quantities = new NativeHashMap<int, int>(8, Allocator.Temp);
                    foreach (var slot in manager.GetBuffer<ProductSlotElement>(building, true))
                        AddDemolitionQuantity(quantities, slot.ItemType, slot.Quantity);
                    AppendDemolitionExports(tasks, decisions, root, building, DroneTransportAreaEnum.Product, quantities);
                }

                if (!manager.HasComponent<BeltComponent>(building))
                    continue;
                if (!beltItems.IsCreated)
                    beltItems = _creationItems.ToEntityArray(Allocator.Temp);
                foreach (Entity item in beltItems)
                {
                    if (!DroneDemolitionUtility.IsStoppedBeltItem(manager, building, item))
                        continue;
                    bool exists = false;
                    foreach (var active in tasks)
                        exists |= active.State == DroneLogisticsTaskStateEnum.Open && active.Target == item &&
                            active.Kind == DroneLogisticsTaskKindEnum.WorldItemRecovery;
                    if (!exists)
                        decisions.Add(new DroneTaskCreationDecisionElement
                        {
                            Kind = DroneLogisticsTaskKindEnum.WorldItemRecovery, Target = item,
                            ItemType = manager.GetComponentData<ItemIdentity>(item).Type,
                            Purpose = DroneTransportPurposeEnum.DemolitionRecovery,
                            RecoveryReason = DroneRecoveryReasonEnum.BuildingDemolition,
                            DemolitionRoot = root, Quantity = 1
                        });
                }
            }
        }
        finally
        {
            if (beltItems.IsCreated) beltItems.Dispose();
        }
    }

    private static void AppendStorageDemolitionExports(EntityManager manager, NativeArray<DroneLogisticsTask> tasks,
        DynamicBuffer<DroneTaskCreationDecisionElement> decisions, Entity root, Entity building,
        DroneTransportAreaEnum area, in DynamicBuffer<StorageSlotElement> slots)
    {
        DroneTransportEndpointUtility.ReadInventoryRange(manager, building, area, slots.Length, out int start, out int end);
        using var quantities = new NativeHashMap<int, int>(8, Allocator.Temp);
        for (int i = start; i < end; i++)
            AddDemolitionQuantity(quantities, slots[i].ItemType, slots[i].Quantity);
        AppendDemolitionExports(tasks, decisions, root, building, area, quantities);
    }

    private static void AddDemolitionQuantity(NativeHashMap<int, int> quantities, ItemTypeEnum type, int amount)
    {
        if (type == ItemTypeEnum.None || amount <= 0)
            return;
        quantities.TryGetValue((int)type, out int previous);
        quantities[(int)type] = (int)math.min(int.MaxValue, (long)previous + amount);
    }

    private static void AppendDemolitionExports(NativeArray<DroneLogisticsTask> tasks,
        DynamicBuffer<DroneTaskCreationDecisionElement> decisions, Entity root, Entity building,
        DroneTransportAreaEnum area, NativeHashMap<int, int> quantities)
    {
        using var items = quantities.GetKeyArray(Allocator.Temp);
        foreach (var item in items)
        {
            bool exists = false;
            foreach (var task in tasks)
                exists |= task.State == DroneLogisticsTaskStateEnum.Open && task.DemolitionRoot == root &&
                    task.Area == area && (int)task.ItemType == item;
            if (!exists)
                decisions.Add(new DroneTaskCreationDecisionElement
                {
                    Kind = DroneLogisticsTaskKindEnum.StorageExport, Target = building, ItemType = (ItemTypeEnum)item,
                    Area = area, Purpose = DroneTransportPurposeEnum.DemolitionRecovery,
                    DemolitionRoot = root, Quantity = quantities[item]
                });
        }
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

    private void BuildRecoveryCreationDecisions(ref SystemState state,
        NativeArray<DroneLogisticsTask> tasks, DynamicBuffer<DroneTaskCreationDecisionElement> decisions)
    {
        var manager = state.EntityManager;
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

        if (footprints.IsEmpty && _creationPendingRecoveryItems.IsEmptyIgnoreFilter)
        {
            return;
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

        // 같은 쿼리의 청크와 청크 내 실물 순서를 따라 회수 작업 생성 순서를 유지한다.
        var activeFootprints = footprints.AsArray();
        var entitiesHandle = state.GetEntityTypeHandle();
        var positionsHandle = state.GetComponentTypeHandle<GridPosition>(true);
        var recoveryHandle = state.GetComponentTypeHandle<DroneRecoveryPending>(true);
        using var chunks = _creationItems.ToArchetypeChunkArray(Allocator.Temp);
        foreach (var chunk in chunks)
        {
            bool hasRecoveryMark = chunk.Has(ref recoveryHandle);
            if (!hasRecoveryMark && activeFootprints.Length == 0)
            {
                continue;
            }

            var entities = chunk.GetNativeArray(entitiesHandle);
            var marks = hasRecoveryMark ? chunk.GetNativeArray(ref recoveryHandle) : default;
            var positions = hasRecoveryMark ? default : chunk.GetNativeArray(ref positionsHandle);
            for (int i = 0; i < entities.Length; i++)
            {
                DroneRecoveryReasonEnum reason;
                if (hasRecoveryMark)
                {
                    reason = marks[i].Reason;
                    if (reason == DroneRecoveryReasonEnum.None)
                    {
                        reason = DroneRecoveryReasonEnum.DroneDrop;
                    }
                }
                else
                {
                    if (ConstructionSiteWorldItemUtility.IsWorldPositionAllowed(activeFootprints, positions[i].Value))
                    {
                        continue;
                    }
                    reason = DroneRecoveryReasonEnum.SiteClearance;
                }
                AppendRecoveryCreationDecision(manager, entities[i], reason, claimedItems, decisions);
            }
        }
    }

    private void AppendRecoveryCreationDecision(EntityManager manager, Entity item, DroneRecoveryReasonEnum reason,
        NativeHashSet<Entity> claimedItems, DynamicBuffer<DroneTaskCreationDecisionElement> decisions)
    {
        var itemType = manager.GetComponentData<ItemIdentity>(item).Type;
        if (!DroneSchedulingUtility.IsWorldItem(manager, item, itemType))
        {
            return;
        }
        if (claimedItems.Contains(item))
        {
            return;
        }
        if (DroneTaskRequestDecisionUtility.IsBlocked(manager, _candidates,
            DroneTaskGenerationKey.From(DroneLogisticsTaskKindEnum.WorldItemRecovery, item, itemType)))
        {
            return;
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
