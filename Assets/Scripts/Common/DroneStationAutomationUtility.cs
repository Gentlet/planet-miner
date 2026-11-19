using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

internal struct DroneStationAutomationIntent
{
    public Entity Drone, Station, DemandTask;
    public DroneStationOperation Operation;
    public int Quantity;
    public int StockSlot;
    public int StockOrdinal;
}

internal struct DroneStationActivationPlan
{
    public Entity Drone, Station, Task;
    public float2 Origin, Source, Destination;
    public float Charge;
    public int Quantity;
    public int StockSlot;
    public int StockOrdinal;
    public bool ViaSource;
}

/// <summary>자동 출고의 현재 수요/공간/경로 입력을 준비한다. 배정이나 실물 수량을 선점하지 않는다.</summary>
internal static class DroneStationAutomationUtility
{
    // 정박/보관 드론의 출고 수요 탐색이 한 Decision 호출 안에서 같은 작업 순서를 읽는다.
    internal readonly struct DemandTasksByNetwork : System.IDisposable
    {
        private readonly EntityManager _manager;
        private readonly Entity _candidates;
        private readonly NativeArray<Entity> _tasks;
        private readonly NativeArray<Entity> _sites;
        private readonly NativeArray<DroneStationNetworkDecision> _networks;
        private readonly NativeParallelMultiHashMap<Entity, DroneStationActivityBounds> _bounds;
        private readonly NativeList<NetworkTask> _eligibleTasks;
        private readonly NativeList<DemandTask> _networkTasks;
        private readonly NativeHashMap<Entity, int2> _ranges;

        public DemandTasksByNetwork(EntityManager manager, Entity candidates, NativeArray<Entity> tasks,
            NativeArray<Entity> sites, NativeArray<DroneStationNetworkDecision> networks)
        {
            _manager = manager;
            _candidates = candidates;
            _tasks = tasks;
            _sites = sites;
            _networks = networks;
            _bounds = new NativeParallelMultiHashMap<Entity, DroneStationActivityBounds>(math.max(1, networks.Length), Allocator.Temp);
            _eligibleTasks = new NativeList<NetworkTask>(Allocator.Temp);
            _networkTasks = new NativeList<DemandTask>(Allocator.Temp);
            _ranges = new NativeHashMap<Entity, int2>(1, Allocator.Temp);
        }

        // 다른 망의 후보가 추가되면 배열이 재할당될 수 있으므로 반환 범위는 즉시 순회한다.
        public NativeArray<DemandTask> GetTasks(Entity network)
        {
            if (!_ranges.TryGetValue(network, out int2 range))
            {
                if (_ranges.IsEmpty)
                {
                    CollectInputs();
                }
                int start = _networkTasks.Length;
                foreach (var candidate in _eligibleTasks)
                {
                    if (candidate.AllowsCrossNetworkAccess)
                    {
                        _networkTasks.Add(candidate.Demand);
                        continue;
                    }
                    foreach (var bounds in _bounds.GetValuesForKey(network))
                    {
                        if (bounds.Overlaps(candidate.Bounds))
                        {
                            _networkTasks.Add(candidate.Demand);
                            break;
                        }
                    }
                }
                range = new int2(start, _networkTasks.Length - start);
                _ranges.Add(network, range);
            }
            return _networkTasks.AsArray().GetSubArray(range.x, range.y);
        }

        private void CollectInputs()
        {
            foreach (var station in _networks)
            {
                _bounds.Add(station.RepresentativeStation, station.ActivityBounds);
            }
            foreach (Entity taskEntity in _tasks)
            {
                var task = _manager.GetComponentData<DroneLogisticsTask>(taskEntity);
                if (!DroneSchedulingUtility.IsTaskValid(_manager, task, _sites) ||
                    DroneTaskRequestDecisionUtility.IsCancellationPlanned(_manager, _candidates, taskEntity))
                {
                    continue;
                }
                bool crossNetwork = DroneStationRelocationUtility.IsStationDemolition(_manager, task);
                _eligibleTasks.Add(new NetworkTask
                {
                    Demand = new DemandTask { Entity = taskEntity, Task = task },
                    AllowsCrossNetworkAccess = crossNetwork,
                    Bounds = crossNetwork ? default : DroneTaskNetworkUtility.ReadTargetBounds(_manager, task.Target)
                });
            }
        }

        public void Dispose()
        {
            _ranges.Dispose();
            _networkTasks.Dispose();
            _eligibleTasks.Dispose();
            _bounds.Dispose();
        }

        private struct NetworkTask
        {
            public DemandTask Demand;
            public DroneStationActivityBounds Bounds;
            public bool AllowsCrossNetworkAccess;
        }
    }

    internal struct DemandTask
    {
        public Entity Entity;
        public DroneLogisticsTask Task;
    }

    public static void BuildStations(EntityManager manager, NativeArray<Entity> owners, NativeList<DroneFlightStation> flights)
    {
        foreach (Entity station in owners)
        {
            if (!DroneTargetPointUtility.TryGetTargetPoint(manager, station, out float2 point, out int2 anchor)) continue;
            var network = manager.GetComponentData<DroneStationNetworkDecision>(station);
            var items = manager.GetBuffer<StorageSlotElement>(station, true);
            var docks = manager.GetBuffer<DockedDroneElement>(station, true);
            int slotCount = manager.GetComponentData<Storage>(station).SlotCount;
            bool hasReservations = slotCount > 0 && manager.HasBuffer<DroneStationReservedSlotElement>(station);
            var reservations = hasReservations ? manager.GetBuffer<DroneStationReservedSlotElement>(station, true) : default;
            bool hasReadPreparing = false;
            bool preparing = false;
            bool canDock = false;
            for (int slot = 0; slot < slotCount; slot++)
            {
                bool occupied = hasReservations && DroneStationRelocationUtility.IsReserved(reservations, slot);
                occupied |= items[slot].Quantity > 0;
                foreach (var dock in docks) occupied |= dock.SlotIndex == slot;
                if (occupied)
                {
                    continue;
                }
                if (!hasReadPreparing)
                {
                    preparing = manager.HasComponent<PreparingStationDemolition>(station);
                    hasReadPreparing = true;
                }
                if (!preparing)
                {
                    canDock = true;
                    break;
                }
            }
            bool hasStamp = manager.HasComponent<PlacementStamp>(station);
            flights.Add(new DroneFlightStation { Entity = station, Position = point, Network = network.RepresentativeStation,
                Bounds = network.ActivityBounds, Anchor = anchor,
                CanDock = canDock, HasStamp = hasStamp,
                Stamp = hasStamp ? manager.GetComponentData<PlacementStamp>(station) : default });
        }
    }

    public static void AddWorkersWithTaskDecisionPriority(EntityManager manager, Entity candidates,
        ComponentLookup<DroneWorkerObservation> observations, NativeHashSet<Entity> workers)
    {
        foreach (var candidate in manager.GetBuffer<DroneTaskCandidateDecisionElement>(candidates, true))
        {
            if (candidate.Quantity > 0) workers.Add(candidate.Worker);
        }
        foreach (var plan in manager.GetBuffer<DroneTransportPlanDecisionElement>(candidates, true))
        {
            if (plan.Candidate.Quantity > 0) workers.Add(plan.Candidate.Worker);
        }
        foreach (var route in manager.GetBuffer<DroneRouteDecisionElement>(candidates, true))
        {
            if (route.Kind == DroneRouteDecisionKindEnum.Create)
            {
                workers.Add(route.Request.Worker);
                continue;
            }
            if (route.Kind != DroneRouteDecisionKindEnum.Retain)
            {
                continue;
            }
            var request = manager.GetComponentData<DroneRouteEvaluationRequest>(route.ExistingRequest);
            if (workers.Contains(request.Worker)) continue;
            if (!observations.TryGetComponent(request.Worker, out var observation)) continue;
            if (request.WorkerObservationRevision != observation.Revision) continue;
            // 미평가 경로는 실행 불가가 아니다. 출고된 수행자 역할을 평가/선택이 끝날 때까지 유지한다.
            if (DroneSchedulingUtility.ReadCurrentRouteResult(manager, route.ExistingRequest).Status == DroneRouteEvaluationStatusEnum.None)
            {
                workers.Add(request.Worker);
            }
        }
    }

    public static Entity FindCurrentStation(EntityManager manager, Entity worker,
        NativeList<DroneStationDockFootprint> dockFootprints, DroneStationEligibility eligibility)
    {
        if (manager.HasComponent<DroneDocked>(worker)) return manager.GetComponentData<DroneDocked>(worker).Station;
        Entity arrived = manager.GetComponentData<DroneMovementState>(worker).ChargingStation;
        if (arrived != Entity.Null && manager.Exists(arrived) && eligibility.StationMask.MatchesIgnoreFilter(arrived)) return arrived;
        if (manager.GetComponentData<DroneActivityState>(worker).Activity != DroneActivityKind.Idle) return Entity.Null;
        if (!eligibility.TryReadWorkerCell(worker, out var cell)) return Entity.Null;
        foreach (var footprint in dockFootprints)
        {
            if (!footprint.Contains(cell)) continue;
            var request = new DroneStationRequest { Drone = worker, Station = footprint.Station, Operation = DroneStationOperation.Dock };
            if (eligibility.Validate(request, float.MaxValue, true) == DroneStationRequestStatus.Applied) return footprint.Station;
        }
        return Entity.Null;
    }

    public static void PrepareDemand(EntityManager manager, Entity candidates, Entity drone, Entity station,
        in DemandTasksByNetwork demandTasks, NativeArray<Entity> storages, NativeArray<Entity> assignments,
        NativeArray<DroneStationNetworkDecision> networks, Entity registry, int capacity, float maximum,
        NativeList<DroneStationActivationPlan> plans, int stockSlot = -1, int stockOrdinal = 0)
    {
        if (manager.HasComponent<PreparingStationDemolition>(station) || manager.HasComponent<DroneStationRelocation>(drone)) return;
        float charge = manager.HasComponent<DroneBatteryState>(drone) ? manager.GetComponentData<DroneBatteryState>(drone).CurrentCharge : maximum;
        if (charge <= 0f || capacity <= 0) return;
        float2 origin = manager.HasComponent<DroneWorker>(drone)
            ? manager.GetComponentData<LocalTransform>(drone).Position.xy : (float2)manager.GetComponentData<GridPosition>(station).Value;
        Entity network = DroneTaskNetworkUtility.FindWorkerNetwork(networks, new float3(origin, 0));
        if (network == Entity.Null) return;
        foreach (var candidate in demandTasks.GetTasks(network))
        {
            Entity taskEntity = candidate.Entity;
            var task = candidate.Task;
            // 기존 수행자의 평가가 끝나기 전에는 같은 수요로 보관 드론을 추가 출고하지 않는다.
            bool evaluating = false;
            foreach (var route in manager.GetBuffer<DroneRouteDecisionElement>(candidates, true))
                evaluating |= route.Kind == DroneRouteDecisionKindEnum.Create &&
                    (route.Request.Source == task.Target || route.Request.Destination == task.Target);
            if (evaluating) continue;
            int demand = ReadDemand(manager, taskEntity, task, assignments, candidates);
            if (demand <= 0) continue;
            if (task.Kind == DroneLogisticsTaskKindEnum.BuildingDemolition)
            {
                if (DroneTargetPointUtility.TryGetTargetPoint(manager, task.Target, out float2 point))
                    plans.Add(new DroneStationActivationPlan { Drone = drone, Station = station, Task = taskEntity,
                        StockSlot = stockSlot, StockOrdinal = stockOrdinal,
                        Origin = origin, Destination = point, Charge = charge, Quantity = 1, ViaSource = false });
                continue;
            }
            bool sourceStorage = task.Kind == DroneLogisticsTaskKindEnum.ConstructionSupply || task.Kind == DroneLogisticsTaskKindEnum.StorageImport;
            foreach (Entity storage in storages)
            {
                if (storage == task.Target || !DroneSchedulingUtility.IsStorage(manager, storage)) continue;
                if (!DroneTaskNetworkUtility.IsTargetInNetwork(manager, networks, network, storage)) continue;
                Entity source = sourceStorage ? storage : task.Target;
                Entity destination = sourceStorage ? task.Target : storage;
                int quantity = math.min(capacity, demand);
                quantity = math.min(quantity, task.Kind == DroneLogisticsTaskKindEnum.WorldItemRecovery
                    ? 1 : (DroneSchedulingUtility.IsTransportKind(task.Kind)
                        ? DroneTransportEndpointUtility.SourceQuantity(manager, task, source)
                        : DroneSchedulingUtility.CountInventory(manager, source, task.ItemType)));
                if (DroneSchedulingUtility.IsTransportKind(task.Kind))
                {
                    quantity = DroneTransportEndpointUtility.DestinationSpace(manager, task, destination, registry, quantity);
                }
                else if (task.Kind != DroneLogisticsTaskKindEnum.ConstructionSupply)
                {
                    quantity = DroneSchedulingUtility.CountStorageSpace(manager, destination, task.ItemType, registry, quantity);
                }
                if (quantity <= 0 || !DroneTargetPointUtility.TryGetTargetPoint(manager, source, out float2 from) ||
                    !DroneTargetPointUtility.TryGetTargetPoint(manager, destination, out float2 to)) continue;
                plans.Add(new DroneStationActivationPlan { Drone = drone, Station = station, Task = taskEntity,
                        StockSlot = stockSlot, StockOrdinal = stockOrdinal,
                    Origin = origin, Source = from, Destination = to, Charge = charge, Quantity = quantity, ViaSource = true });
            }
        }
    }

    internal static int ReadDemand(EntityManager manager, Entity entity, in DroneLogisticsTask task,
        NativeArray<Entity> assignments, Entity candidates)
    {
        if (task.State != DroneLogisticsTaskStateEnum.Open) return 0;
        if (DroneSchedulingUtility.IsTransportKind(task.Kind))
        {
            var goal = manager.GetComponentData<DroneTransportRequirement>(entity);
            var progress = manager.GetComponentData<DroneTransportProgress>(entity);
            return (int)math.max(0L, (long)goal.Quantity - progress.DeliveredQuantity - progress.ReservedQuantity);
        }
        if (task.Kind == DroneLogisticsTaskKindEnum.ConstructionSupply)
            return ConstructionSupplyReservationUtility.Remaining(manager, task.Target, task.ItemType);
        foreach (Entity owner in assignments)
        {
            var assignment = manager.GetComponentData<DroneTaskAssignment>(owner);
            bool sameTarget = task.Kind == DroneLogisticsTaskKindEnum.BuildingDemolition
                ? assignment.Task == entity : assignment.Source == task.Target;
            if (sameTarget && !DroneSchedulingUtility.IsTerminal(assignment.State)) return 0;
        }
        foreach (var pending in manager.GetBuffer<DroneTaskPendingPublicationElement>(candidates, true))
        {
            bool sameTarget = task.Kind == DroneLogisticsTaskKindEnum.BuildingDemolition
                ? pending.Candidate.Task == entity : pending.Candidate.Source == task.Target;
            if (sameTarget && (pending.Quantity > 0 || pending.PublicationQueued)) return 0;
        }
        return 1;
    }

    public static void PrepareResume(EntityManager manager, Entity worker, Entity station, Entity assignmentEntity,
        NativeArray<DroneStationNetworkDecision> networks, NativeArray<Entity> storages, NativeArray<Entity> sites,
        Entity registry, NativeList<DroneStationActivationPlan> plans)
    {
        var assignment = manager.GetComponentData<DroneTaskAssignment>(assignmentEntity);
        if (DroneSchedulingUtility.IsTerminal(assignment.State)) return;
        var plan = new DroneStationActivationPlan { Drone = worker, Station = station, StockSlot = -1,
            Origin = manager.GetComponentData<LocalTransform>(worker).Position.xy,
            Charge = manager.GetComponentData<DroneBatteryState>(worker).CurrentCharge };
        if (assignment.State == DroneTaskAssignmentStateEnum.Retargeting)
        {
            Entity network = DroneTaskNetworkUtility.FindWorkerNetwork(networks, new float3(plan.Origin, 0));
            if (network == Entity.Null) return;
            foreach (Entity target in storages)
            {
                if (!DroneSchedulingUtility.IsStorage(manager, target) ||
                    !DroneTaskNetworkUtility.IsTargetInNetwork(manager, networks, network, target) ||
                    !DroneSchedulingUtility.HasStorageSpace(manager, target, assignment.ItemType, registry)) continue;
                if (!DroneTargetPointUtility.TryGetTargetPoint(manager, target, out plan.Destination)) continue;
                plans.Add(plan);
            }
            if (manager.GetComponentData<DroneCargoState>(worker).Origin != DroneCargoOriginEnum.Supply) return;
            foreach (Entity site in sites)
            {
                if (!DroneSchedulingUtility.IsSite(manager, site) ||
                    !DroneTaskNetworkUtility.IsTargetInNetwork(manager, networks, network, site) ||
                    ConstructionSupplyReservationUtility.Remaining(manager, site, assignment.ItemType) <= 0) continue;
                if (!DroneTargetPointUtility.TryGetTargetPoint(manager, site, out plan.Destination)) continue;
                plans.Add(plan);
            }
            return;
        }
        plan.ViaSource = assignment.NextAction == DroneActionKindEnum.CollectFromStorage || assignment.NextAction == DroneActionKindEnum.RecoverWorldItem;
        if (plan.ViaSource && !DroneTargetPointUtility.TryGetTargetPoint(manager, assignment.Source, out plan.Source)) return;
        if (assignment.NextAction == DroneActionKindEnum.DropCargo)
        {
            plan.Destination = assignment.DropPosition;
            plans.Add(plan);
            return;
        }
        if (!DroneTargetPointUtility.TryGetTargetPoint(manager, assignment.Destination, out plan.Destination)) return;
        plans.Add(plan);
    }
}
