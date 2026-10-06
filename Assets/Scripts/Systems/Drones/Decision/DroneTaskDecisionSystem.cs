using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: Decision에서 필요한 공급/회수 작업, 무효화, 경로 평가와 배정 후보를 판단한다.
/// 입력: Command 반영 뒤의 현장 요구량, 보관품/공간, 기존 작업·배정, 수행자 관측과 외부 경로 평가 결과.
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
    private EntityQuery _registry;
    private EntityQuery _capacity;
    private uint _evaluationRevision;

    public void OnCreate(ref SystemState state)
    {
        _candidates = DroneSchedulingUtility.GetOrCreateCandidates(state.EntityManager);
        _tasks = state.GetEntityQuery(ComponentType.ReadOnly<DroneLogisticsTask>());
        _sites = state.GetEntityQuery(ComponentType.ReadOnly<ConstructionSite>(), ComponentType.ReadOnly<GridPosition>());
        _storages = state.GetEntityQuery(ComponentType.ReadOnly<BuildingType>(), ComponentType.ReadOnly<Storage>(),
            ComponentType.ReadOnly<StoredItemElement>(), ComponentType.ReadOnly<GridPosition>());
        _workers = state.GetEntityQuery(ComponentType.ReadOnly<DroneWorker>(), ComponentType.ReadOnly<DroneWorkerObservation>(),
            ComponentType.ReadOnly<DroneWorkerAssignment>(), ComponentType.ReadOnly<DroneCargoState>(), ComponentType.ReadOnly<StoredItemElement>());
        _routes = state.GetEntityQuery(ComponentType.ReadOnly<DroneRouteEvaluationRequest>());
        _assignments = state.GetEntityQuery(ComponentType.ReadOnly<DroneTaskAssignment>());
        _registry = state.GetEntityQuery(ComponentType.ReadOnly<ItemRegistry>(), ComponentType.ReadOnly<ItemConfigElement>());
        _capacity = state.GetEntityQuery(ComponentType.ReadOnly<DroneCapacityState>());
        InitializeCreationQueries(ref state);
    }

    public void OnUpdate(ref SystemState state)
    {
        state.CompleteDependency();
        var manager = state.EntityManager;
        int carryingCapacity = DroneSchedulingUtility.ReadCarryingCapacity(_capacity);
        manager.GetBuffer<DroneTaskCandidateDecisionElement>(_candidates).Clear();
        var routeDecisions = manager.GetBuffer<DroneRouteDecisionElement>(_candidates);
        routeDecisions.Clear();
        BuildCreationDecisions(ref state);
        var invalidationDecisions = manager.GetBuffer<DroneTaskInvalidationDecisionElement>(_candidates);
        invalidationDecisions.Clear();
        using var tasks = _tasks.ToEntityArray(Allocator.Temp);
        using var sites = _sites.ToEntityArray(Allocator.Temp);
        using var storages = _storages.ToEntityArray(Allocator.Temp);
        using var workers = _workers.ToEntityArray(Allocator.Temp);
        using var routes = _routes.ToEntityArray(Allocator.Temp);
        using var assignments = _assignments.ToEntityArray(Allocator.Temp);
        using var busyWorkers = new NativeHashSet<Entity>(16, Allocator.Temp);
        using var claimedRecoveryItems = new NativeHashSet<Entity>(16, Allocator.Temp);
        using var removedRoutes = new NativeHashSet<Entity>(16, Allocator.Temp);
        using var usedRoutes = new NativeHashSet<Entity>(16, Allocator.Temp);

        // 무효 작업/배정을 신규 수집 후보에서 제외하되 여기서 원본을 닫거나 연결을 끊지 않는다.
        // 예약 해제는 Reservation, 종료 상태와 삭제 보류는 Lifecycle Apply가 각 소유 경계에서 처리한다.
        for (int t = 0; t < tasks.Length; t++)
        {
            var task = manager.GetComponentData<DroneLogisticsTask>(tasks[t]);
            if (task.State == DroneLogisticsTaskStateEnum.Open && !DroneSchedulingUtility.IsTaskValid(manager, task, sites))
                invalidationDecisions.Add(new DroneTaskInvalidationDecisionElement
                {
                    Kind = DroneTaskInvalidationDecisionKindEnum.CloseTask,
                    Target = tasks[t]
                });
        }

        for (int a = 0; a < assignments.Length; a++)
        {
            var assignment = manager.GetComponentData<DroneTaskAssignment>(assignments[a]);
            if (DroneSchedulingUtility.AssignmentNeedsCleanup(manager, assignment, sites))
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
            if (assignment.NextAction == DroneActionKindEnum.RecoverWorldItem) claimedRecoveryItems.Add(assignment.Source);
        }
        FindInvalidRoutes(manager, routes, removedRoutes, carryingCapacity);

        Entity registry = _registry.IsEmptyIgnoreFilter ? Entity.Null : _registry.GetSingletonEntity();
        using var pendingRoutes = new NativeList<DroneRouteEvaluationRequest>(Allocator.Temp);
        var candidates = manager.GetBuffer<DroneTaskCandidateDecisionElement>(_candidates);
        var pendingPublications = manager.GetBuffer<DroneTaskPendingPublicationElement>(_candidates);
        // 이미 적재한 자재의 목적지를 먼저 판단한다. 최종 선택·수량 확보 순서는 Reservation이 정한다.
        BuildRetargetCandidates(manager, assignments, tasks, sites, storages, registry, routes, pendingRoutes,
            routeDecisions, removedRoutes, usedRoutes, candidates, pendingPublications, carryingCapacity);
        for (int w = 0; w < workers.Length; w++)
        {
            Entity worker = workers[w];
            if (busyWorkers.Contains(worker) || !DroneSchedulingUtility.IsIdleWorker(manager, worker, carryingCapacity)) continue;
            var observation = manager.GetComponentData<DroneWorkerObservation>(worker);
            for (int t = 0; t < tasks.Length; t++)
            {
                var task = manager.GetComponentData<DroneLogisticsTask>(tasks[t]);
                if (!DroneSchedulingUtility.IsTaskValid(manager, task, sites)) continue;
                bool supply = task.Kind == DroneLogisticsTaskKindEnum.ConstructionSupply;
                if (supply && DroneSchedulingUtility.ProjectedRemaining(manager, task.Target, task.ItemType, assignments, sites, pendingPublications) <= 0) continue;
                if (!supply && claimedRecoveryItems.Contains(task.Target)) continue;

                bool waitingForRoutes = false;
                Entity bestStorage = Entity.Null;
                Entity bestRoute = Entity.Null;
                float bestDistance = float.MaxValue;
                // 공급은 재고가 있는 공급원을, 회수는 보관 가능한 목적지를 비교한다.
                // 빈 드론의 ViaSource 결과는 드론→공급원/월드 실물→목적지의 전체 실제 이동거리다.
                for (int s = 0; s < storages.Length; s++)
                {
                    Entity storage = storages[s];
                    if (supply)
                    {
                        if (DroneSchedulingUtility.CountInventory(manager, storage, task.ItemType) <= 0) continue;
                    }
                    else if (!DroneSchedulingUtility.HasStorageSpace(manager, storage, task.ItemType, registry))
                    {
                        continue;
                    }

                    Entity source = supply ? storage : task.Target;
                    Entity destination = supply ? task.Target : storage;
                    Entity route = FindOrRequestRoute(manager, worker, observation, source, destination,
                        routes, pendingRoutes, routeDecisions, removedRoutes, usedRoutes, carryingCapacity);
                    if (route == Entity.Null)
                    {
                        waitingForRoutes = true;
                        continue;
                    }

                    if (!DroneSchedulingUtility.TryReadRoute(manager, route, carryingCapacity, out bool reachable, out float distance))
                    {
                        waitingForRoutes = true;
                        continue;
                    }

                    if (!reachable) continue;
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
                if (quantity <= 0) continue;
                candidates.Add(new DroneTaskCandidateDecisionElement
                {
                    Worker = worker,
                    Task = tasks[t],
                    Source = supply ? bestStorage : task.Target,
                    Destination = supply ? task.Target : bestStorage,
                    RouteRequest = bestRoute,
                    ItemType = task.ItemType,
                    WorkerObservationRevision = observation.Revision,
                    Quantity = quantity
                });
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

    private static void FindInvalidRoutes(EntityManager manager, NativeArray<Entity> routes,
        NativeHashSet<Entity> removedRoutes, int carryingCapacity)
    {
        // 관측/배정 revision과 대상 위치가 달라진 결과를 재사용하지 않는다.
        // 제외 목록은 이번 판단에만 쓰며 실제 요청 삭제는 아래 경로 의도와 Execution을 거친다.
        for (int i = 0; i < routes.Length; i++)
        {
            var request = manager.GetComponentData<DroneRouteEvaluationRequest>(routes[i]);
            bool invalid = !DroneSchedulingUtility.RouteIsCurrent(manager, request, carryingCapacity);
            if (!invalid && manager.HasComponent<DroneRouteEvaluationResult>(routes[i]))
            {
                var result = manager.GetComponentData<DroneRouteEvaluationResult>(routes[i]);
                invalid = result.EvaluationRevision != request.EvaluationRevision ||
                          (result.Status != DroneRouteEvaluationStatusEnum.None &&
                           !DroneSchedulingUtility.TryReadRoute(manager, routes[i], carryingCapacity, out _, out _));
            }
            if (invalid)
            {
                removedRoutes.Add(routes[i]);
            }
        }
    }

    private Entity FindOrRequestRoute(EntityManager manager, Entity worker, DroneWorkerObservation observation,
        Entity source, Entity destination, NativeArray<Entity> routes,
        NativeList<DroneRouteEvaluationRequest> pending, DynamicBuffer<DroneRouteDecisionElement> routeDecisions,
        NativeHashSet<Entity> removedRoutes, NativeHashSet<Entity> usedRoutes, int carryingCapacity,
        Entity assignment = default, uint assignmentRevision = 0, bool dropPositionSearch = false)
    {
        // 같은 관측·배정에 대한 기존 요청은 유지하고, 이번 틱 미게시 요청은 중복 생성하지 않는다.
        // 실제 거리 계산은 외부 평가자가 담당하므로 새 요청 의도를 만든 틱에는 후보를 확정하지 않는다.
        for (int i = 0; i < routes.Length; i++)
        {
            if (removedRoutes.Contains(routes[i])) continue;
            var request = manager.GetComponentData<DroneRouteEvaluationRequest>(routes[i]);
            if (request.Worker == worker && request.Source == source && request.Destination == destination &&
                request.Assignment == assignment && request.AssignmentRevision == assignmentRevision &&
                request.IsDropPositionSearch == dropPositionSearch &&
                DroneSchedulingUtility.RouteIsCurrent(manager, request, carryingCapacity))
            {
                usedRoutes.Add(routes[i]);
                return routes[i];
            }
        }

        for (int i = 0; i < pending.Length; i++)
        {
            if (pending[i].Worker == worker && pending[i].Source == source && pending[i].Destination == destination &&
                pending[i].Assignment == assignment && pending[i].AssignmentRevision == assignmentRevision &&
                pending[i].IsDropPositionSearch == dropPositionSearch)
                return Entity.Null;
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
            Kind = assignment == Entity.Null ? DroneRouteKindEnum.ViaSource : DroneRouteKindEnum.Direct,
            IsDropPositionSearch = dropPositionSearch,
            OriginPosition = observation.Position,
            Source = source,
            SourcePosition = source == Entity.Null ? int2.zero : manager.GetComponentData<GridPosition>(source).Value,
            Destination = destination,
            DestinationPosition = destination == Entity.Null ? int2.zero : manager.GetComponentData<GridPosition>(destination).Value
        };
        pending.Add(next);
        routeDecisions.Add(new DroneRouteDecisionElement { Kind = DroneRouteDecisionKindEnum.Create, Request = next });
        return Entity.Null;
    }

    private enum CargoDestinationSelectionEnum : byte
    {
        NoDestination,
        Waiting,
        Selected
    }

    private void BuildRetargetCandidates(EntityManager manager, NativeArray<Entity> assignments,
        NativeArray<Entity> tasks, NativeArray<Entity> sites, NativeArray<Entity> storages, Entity registry,
        NativeArray<Entity> routes, NativeList<DroneRouteEvaluationRequest> pending,
        DynamicBuffer<DroneRouteDecisionElement> routeDecisions, NativeHashSet<Entity> removedRoutes,
        NativeHashSet<Entity> usedRoutes, DynamicBuffer<DroneTaskCandidateDecisionElement> candidates,
        DynamicBuffer<DroneTaskPendingPublicationElement> pendingPublications, int capacity)
    {
        // 공급원에서 가져온 적재품은 필요한 현장→보관처 순서로 찾는다.
        // 월드 회수품은 보관처만 찾으며, 보관되기 전에는 건설 현장에 직접 공급하지 않는다.
        for (int i = 0; i < assignments.Length; i++)
        {
            Entity entity = assignments[i];
            if (!DroneSchedulingUtility.IsRetargetingWorker(manager, entity, out var assignment, out int cargo)) continue;
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
                selection = FindCargoDestination(manager, entity, assignment, sites, true, assignments, sites,
                    registry, routes, pending, routeDecisions, removedRoutes, usedRoutes, pendingPublications, capacity,
                    out destination, out route);
                if (selection == CargoDestinationSelectionEnum.Waiting) continue;
                if (selection == CargoDestinationSelectionEnum.Selected)
                {
                    task = FindSupplyTask(manager, tasks, destination, assignment.ItemType);
                    // 새 현장의 작업은 EndSimulation에 게시될 수 있다. 게시 전에는 보관으로 우회하지 않는다.
                    if (task == Entity.Null) continue;
                    action = DroneActionKindEnum.SupplyConstructionSite;
                    quantity = math.min(cargo, DroneSchedulingUtility.ProjectedRemaining(manager, destination,
                        assignment.ItemType, assignments, sites, pendingPublications));
                }
            }
            if (selection == CargoDestinationSelectionEnum.NoDestination)
            {
                selection = FindCargoDestination(manager, entity, assignment, storages, false, assignments, sites,
                    registry, routes, pending, routeDecisions, removedRoutes, usedRoutes, pendingPublications, capacity,
                    out destination, out route);
                if (selection == CargoDestinationSelectionEnum.Waiting) continue;
                if (selection == CargoDestinationSelectionEnum.Selected) action = DroneActionKindEnum.StoreCargo;
            }
            if (action == DroneActionKindEnum.DropCargo && !TrySelectDropPosition(manager, entity, assignment,
                routes, pending, routeDecisions, removedRoutes, usedRoutes, capacity, out route, out dropPosition)) continue;
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
                WorkerObservationRevision = manager.GetComponentData<DroneWorkerObservation>(assignment.Worker).Revision,
                Quantity = quantity
            });
        }
    }

    private bool TrySelectDropPosition(EntityManager manager, Entity entity, in DroneTaskAssignment assignment,
        NativeArray<Entity> routes, NativeList<DroneRouteEvaluationRequest> pending,
        DynamicBuffer<DroneRouteDecisionElement> routeDecisions, NativeHashSet<Entity> removedRoutes,
        NativeHashSet<Entity> usedRoutes, int capacity, out Entity route, out int2 position)
    {
        route = Entity.Null;
        var observation = manager.GetComponentData<DroneWorkerObservation>(assignment.Worker);
        position = (int2)math.floor(observation.Position.xy);
        // 현재 셀이 현장 밖이면 그대로 방출 목표로 삼는다. 안쪽이면 외부 평가자에게 도달 가능한 외부 셀을 요청한다.
        // 선택 목표는 이동 결과가 아니며 실제 방출은 Lifecycle이 관측/요청 위치와 목표 일치를 다시 검사한다.
        if (ConstructionSiteWorldItemUtility.IsWorldPositionAllowed(manager, position)) return true;

        route = FindOrRequestRoute(manager, assignment.Worker, observation, Entity.Null, Entity.Null,
            routes, pending, routeDecisions, removedRoutes, usedRoutes, capacity, entity, assignment.Revision, true);
        if (route == Entity.Null) return false;
        if (!DroneSchedulingUtility.TryReadRoute(manager, route, capacity, out bool reachable, out _)) return false;
        if (!reachable) return false;
        var result = manager.GetComponentData<DroneRouteEvaluationResult>(route);
        if (!result.HasDropPosition) return false;
        if (!ConstructionSiteWorldItemUtility.IsWorldPositionAllowed(manager, result.DropPosition)) return false;
        position = result.DropPosition;
        return true;
    }

    private CargoDestinationSelectionEnum FindCargoDestination(EntityManager manager, Entity entity,
        in DroneTaskAssignment assignment, NativeArray<Entity> destinations, bool supply,
        NativeArray<Entity> assignments, NativeArray<Entity> sites, Entity registry, NativeArray<Entity> routes,
        NativeList<DroneRouteEvaluationRequest> pending, DynamicBuffer<DroneRouteDecisionElement> routeDecisions,
        NativeHashSet<Entity> removedRoutes, NativeHashSet<Entity> usedRoutes,
        DynamicBuffer<DroneTaskPendingPublicationElement> pendingPublications, int capacity,
        out Entity bestDestination, out Entity bestRoute)
    {
        bestDestination = Entity.Null;
        bestRoute = Entity.Null;
        float bestDistance = float.MaxValue;
        bool waiting = false;
        var observation = manager.GetComponentData<DroneWorkerObservation>(assignment.Worker);
        // 적재품은 공급원을 다시 거치지 않는 Direct 경로를 비교한다.
        // 경로 평가가 남으면 보관/방출로 우회하지 않고 기다리며, 완료 결과의 거리 동률은 PlacementStamp로 푼다.
        for (int i = 0; i < destinations.Length; i++)
        {
            Entity destination = destinations[i];
            if (supply)
            {
                if (!DroneSchedulingUtility.IsSite(manager, destination)) continue;
                if (DroneSchedulingUtility.ProjectedRemaining(manager, destination, assignment.ItemType,
                    assignments, sites, pendingPublications) <= 0) continue;
            }
            else if (!DroneSchedulingUtility.HasStorageSpace(manager, destination, assignment.ItemType, registry)) continue;
            Entity route = FindOrRequestRoute(manager, assignment.Worker, observation, Entity.Null, destination,
                routes, pending, routeDecisions, removedRoutes, usedRoutes, capacity, entity, assignment.Revision);
            if (route == Entity.Null || !DroneSchedulingUtility.TryReadRoute(manager, route, capacity,
                out bool reachable, out float distance))
            {
                waiting = true;
                continue;
            }
            if (!reachable) continue;
            if (bestDestination == Entity.Null || distance < bestDistance ||
                (distance == bestDistance && DroneSchedulingUtility.ComparePlacement(manager, destination, bestDestination) < 0))
            {
                bestDestination = destination;
                bestRoute = route;
                bestDistance = distance;
            }
        }
        if (waiting) return CargoDestinationSelectionEnum.Waiting;
        return bestDestination == Entity.Null ? CargoDestinationSelectionEnum.NoDestination : CargoDestinationSelectionEnum.Selected;
    }

    private static Entity FindSupplyTask(EntityManager manager, NativeArray<Entity> tasks, Entity site, ItemTypeEnum type)
    {
        for (int i = 0; i < tasks.Length; i++)
        {
            var task = manager.GetComponentData<DroneLogisticsTask>(tasks[i]);
            if (task.Kind == DroneLogisticsTaskKindEnum.ConstructionSupply && task.State == DroneLogisticsTaskStateEnum.Open &&
                task.Target == site && task.ItemType == type) return tasks[i];
        }
        return Entity.Null;
    }

    private EntityQuery _creationSites;
    private EntityQuery _creationTasks;
    private EntityQuery _creationItems;
    private EntityQuery _creationFootprints;

    private void InitializeCreationQueries(ref SystemState state)
    {
        _creationSites = state.GetEntityQuery(
            ComponentType.ReadOnly<ConstructionSite>(),
            ComponentType.ReadOnly<GridPosition>(),
            ComponentType.ReadOnly<ConstructionMaterialRequirementElement>());
        _creationTasks = state.GetEntityQuery(ComponentType.ReadOnly<DroneLogisticsTask>());
        _creationItems = state.GetEntityQuery(ComponentType.ReadOnly<ItemIdentity>(),
            ComponentType.ReadOnly<ItemOwnership>(), ComponentType.ReadOnly<GridPosition>());
        _creationFootprints = state.GetEntityQuery(ComponentType.ReadOnly<ConstructionSite>(),
            ComponentType.ReadOnly<GridPosition>(), ComponentType.ReadOnly<BuildingFootprint>(),
            ComponentType.ReadOnly<Direction>());
    }

    private void BuildCreationDecisions(ref SystemState state)
    {
        var manager = state.EntityManager;
        var decisions = manager.GetBuffer<DroneTaskCreationDecisionElement>(_candidates);
        decisions.Clear();
        using var tasks = _creationTasks.ToComponentDataArray<DroneLogisticsTask>(Allocator.Temp);
        BuildSupplyCreationDecisions(manager, tasks, decisions);
        BuildRecoveryCreationDecisions(manager, tasks, decisions);
    }

    private void BuildSupplyCreationDecisions(EntityManager manager,
        NativeArray<DroneLogisticsTask> tasks, DynamicBuffer<DroneTaskCreationDecisionElement> decisions)
    {
        // Closed 작업도 연결 배정이 정리될 때까지 살아 있으므로 같은 품목의 새 root를 만들지 않는다.
        using var existingTasks = new NativeList<CreationSupplyTaskKey>(Allocator.Temp);
        for (int i = 0; i < tasks.Length; i++)
        {
            var task = tasks[i];
            if (task.Kind == DroneLogisticsTaskKindEnum.ConstructionSupply)
            {
                existingTasks.Add(new CreationSupplyTaskKey { Site = task.Target, ItemType = task.ItemType });
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

                if (HasCreationSupplyTask(existingTasks, site, requirement.ItemType))
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
                existingTasks.Add(new CreationSupplyTaskKey { Site = site, ItemType = requirement.ItemType });
            }
        }
    }

    private void BuildRecoveryCreationDecisions(EntityManager manager,
        NativeArray<DroneLogisticsTask> tasks, DynamicBuffer<DroneTaskCreationDecisionElement> decisions)
    {
        // 이전에 방출된 실물과 현재 현장 footprint 안의 월드 실물만 회수 작업으로 만든다.
        // 취소 현장은 제외하고 방향을 한 번 적용하여 회수 필요 영역과 완공 차단 영역을 맞춘다.
        using var footprints = new NativeList<CreationSiteFootprint>(Allocator.Temp);
        using var footprintSites = _creationFootprints.ToEntityArray(Allocator.Temp);
        for (int i = 0; i < footprintSites.Length; i++)
        {
            Entity site = footprintSites[i];
            if ((manager.GetComponentData<ConstructionSite>(site).Flags & ConstructionSiteFlags.Cancelled) != 0)
            {
                continue;
            }

            footprints.Add(new CreationSiteFootprint
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

            var reason = manager.HasComponent<DroneRecoveryPending>(item)
                ? DroneRecoveryReasonEnum.DroneDrop
                : DroneRecoveryReasonEnum.None;
            if (reason == DroneRecoveryReasonEnum.None &&
                IsInsideCreationSite(manager.GetComponentData<GridPosition>(item).Value, footprints))
            {
                reason = DroneRecoveryReasonEnum.SiteClearance;
            }

            if (reason == DroneRecoveryReasonEnum.None)
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

    private static bool HasCreationSupplyTask(NativeList<CreationSupplyTaskKey> tasks, Entity site, ItemTypeEnum itemType)
    {
        for (int i = 0; i < tasks.Length; i++)
        {
            if (tasks[i].Site == site && tasks[i].ItemType == itemType)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsInsideCreationSite(int2 position, NativeList<CreationSiteFootprint> footprints)
    {
        for (int i = 0; i < footprints.Length; i++)
        {
            var footprint = footprints[i];
            if (math.all(position >= footprint.Origin) && math.all(position < footprint.Origin + footprint.Size))
            {
                return true;
            }
        }

        return false;
    }

    private struct CreationSiteFootprint
    {
        public int2 Origin;
        public int2 Size;
    }

    private struct CreationSupplyTaskKey
    {
        public Entity Site;
        public ItemTypeEnum ItemType;
    }
}
