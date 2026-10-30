using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

internal struct DroneStationAutomationIntent
{
    public Entity Drone, Station, DemandTask;
    public DroneStationOperation Operation;
    public int Quantity;
}

internal struct DroneStationActivationPlan
{
    public Entity Drone, Station, Task;
    public float2 Origin, Source, Destination;
    public float Charge;
    public int Quantity;
    public bool ViaSource;
}

/// <summary>자동 출고의 현재 수요/공간/경로 입력을 준비한다. 배정이나 실물 수량을 선점하지 않는다.</summary>
internal static class DroneStationAutomationUtility
{
    public static void BuildStations(EntityManager manager, NativeArray<Entity> owners, NativeList<DroneFlightStation> flights)
    {
        foreach (Entity station in owners)
        {
            if (!DroneTargetPointUtility.TryGetTargetPoint(manager, station, out float2 point)) continue;
            var network = manager.GetComponentData<DroneStationNetworkDecision>(station);
            var items = manager.GetBuffer<StoredItemElement>(station, true);
            var docks = manager.GetBuffer<DockedDroneElement>(station, true);
            bool canDock = false;
            for (int slot = 0; slot < manager.GetComponentData<Storage>(station).SlotCount; slot++)
            {
                bool occupied = false;
                foreach (var item in items) occupied |= item.SlotIndex == slot;
                foreach (var dock in docks) occupied |= dock.SlotIndex == slot;
                if (!occupied)
                {
                    canDock = true;
                    break;
                }
            }
            flights.Add(new DroneFlightStation { Entity = station, Position = point, Network = network.RepresentativeStation,
                Bounds = network.ActivityBounds, Anchor = manager.GetComponentData<GridPosition>(station).Value,
                CanDock = canDock, HasStamp = manager.HasComponent<PlacementStamp>(station),
                Stamp = manager.HasComponent<PlacementStamp>(station) ? manager.GetComponentData<PlacementStamp>(station) : default });
        }
    }

    public static bool HasTaskDecisionPriority(EntityManager manager, Entity candidates, Entity worker)
    {
        foreach (var candidate in manager.GetBuffer<DroneTaskCandidateDecisionElement>(candidates, true))
        {
            if (candidate.Worker == worker && candidate.Quantity > 0)
            {
                return true;
            }
        }
        foreach (var plan in manager.GetBuffer<DroneTransportPlanDecisionElement>(candidates, true))
        {
            if (plan.Candidate.Worker == worker && plan.Candidate.Quantity > 0)
            {
                return true;
            }
        }
        uint revision = manager.GetComponentData<DroneWorkerObservation>(worker).Revision;
        foreach (var route in manager.GetBuffer<DroneRouteDecisionElement>(candidates, true))
        {
            if (route.Kind == DroneRouteDecisionKindEnum.Create)
            {
                if (route.Request.Worker == worker)
                {
                    return true;
                }
                continue;
            }
            if (route.Kind != DroneRouteDecisionKindEnum.Retain)
            {
                continue;
            }
            var request = manager.GetComponentData<DroneRouteEvaluationRequest>(route.ExistingRequest);
            if (request.Worker != worker || request.WorkerObservationRevision != revision)
            {
                continue;
            }
            // 미평가 경로는 실행 불가가 아니다. 출고된 수행자 역할을 평가/선택이 끝날 때까지 유지한다.
            if (DroneSchedulingUtility.ReadCurrentRouteResult(manager, route.ExistingRequest).Status == DroneRouteEvaluationStatusEnum.None)
            {
                return true;
            }
        }
        return false;
    }

    public static Entity FindCurrentStation(EntityManager manager, Entity worker, NativeArray<Entity> stations, DroneStationEligibility eligibility)
    {
        if (manager.HasComponent<DroneDocked>(worker)) return manager.GetComponentData<DroneDocked>(worker).Station;
        Entity arrived = manager.GetComponentData<DroneMovementState>(worker).ChargingStation;
        if (arrived != Entity.Null && manager.Exists(arrived) && eligibility.StationMask.MatchesIgnoreFilter(arrived)) return arrived;
        if (manager.GetComponentData<DroneActivityState>(worker).Activity != DroneActivityKind.Idle) return Entity.Null;
        foreach (Entity station in stations)
        {
            var request = new DroneStationRequest { Drone = worker, Station = station, Operation = DroneStationOperation.Dock };
            if (eligibility.Validate(request, float.MaxValue, true) == DroneStationRequestStatus.Applied) return station;
        }
        return Entity.Null;
    }

    public static void PrepareDemand(EntityManager manager, Entity candidates, Entity drone, Entity station,
        NativeArray<Entity> tasks, NativeArray<Entity> sites, NativeArray<Entity> storages, NativeArray<Entity> assignments,
        NativeArray<DroneStationNetworkDecision> networks, Entity registry, int capacity, float maximum,
        NativeList<DroneStationActivationPlan> plans)
    {
        float charge = manager.HasComponent<DroneBatteryState>(drone) ? manager.GetComponentData<DroneBatteryState>(drone).CurrentCharge : maximum;
        if (charge <= 0f || capacity <= 0) return;
        float2 origin = manager.HasComponent<DroneWorker>(drone)
            ? manager.GetComponentData<LocalTransform>(drone).Position.xy : (float2)manager.GetComponentData<GridPosition>(station).Value;
        Entity network = DroneTaskNetworkUtility.FindWorkerNetwork(networks, new float3(origin, 0));
        if (network == Entity.Null) return;
        foreach (Entity taskEntity in tasks)
        {
            var task = manager.GetComponentData<DroneLogisticsTask>(taskEntity);
            if (!DroneSchedulingUtility.IsTaskValid(manager, task, sites) ||
                DroneTaskRequestDecisionUtility.IsCancellationPlanned(manager, candidates, taskEntity)) continue;
            if (!DroneTaskNetworkUtility.IsTargetInNetwork(manager, networks, network, task.Target)) continue;
            // 기존 수행자의 평가가 끝나기 전에는 같은 수요로 보관 드론을 추가 출고하지 않는다.
            bool evaluating = false;
            foreach (var route in manager.GetBuffer<DroneRouteDecisionElement>(candidates, true))
                evaluating |= route.Kind == DroneRouteDecisionKindEnum.Create &&
                    (route.Request.Source == task.Target || route.Request.Destination == task.Target);
            if (evaluating) continue;
            int demand = ReadDemand(manager, taskEntity, task, assignments, candidates);
            if (demand <= 0) continue;
            bool sourceStorage = task.Kind == DroneLogisticsTaskKindEnum.ConstructionSupply || task.Kind == DroneLogisticsTaskKindEnum.StorageImport;
            foreach (Entity storage in storages)
            {
                if (storage == task.Target || !DroneSchedulingUtility.IsStorage(manager, storage)) continue;
                if (!DroneTaskNetworkUtility.IsTargetInNetwork(manager, networks, network, storage)) continue;
                Entity source = sourceStorage ? storage : task.Target;
                Entity destination = sourceStorage ? task.Target : storage;
                int quantity = math.min(capacity, demand);
                quantity = math.min(quantity, task.Kind == DroneLogisticsTaskKindEnum.WorldItemRecovery
                    ? 1 : DroneSchedulingUtility.CountInventory(manager, source, task.ItemType));
                if (task.Kind != DroneLogisticsTaskKindEnum.ConstructionSupply)
                    quantity = DroneSchedulingUtility.CountStorageSpace(manager, destination, task.ItemType, registry, quantity);
                if (quantity <= 0 || !DroneTargetPointUtility.TryGetTargetPoint(manager, source, out float2 from) ||
                    !DroneTargetPointUtility.TryGetTargetPoint(manager, destination, out float2 to)) continue;
                plans.Add(new DroneStationActivationPlan { Drone = drone, Station = station, Task = taskEntity,
                    Origin = origin, Source = from, Destination = to, Charge = charge, Quantity = quantity, ViaSource = true });
            }
        }
    }

    private static int ReadDemand(EntityManager manager, Entity entity, in DroneLogisticsTask task,
        NativeArray<Entity> assignments, Entity candidates)
    {
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
            if (assignment.Source == task.Target && !DroneSchedulingUtility.IsTerminal(assignment.State)) return 0;
        }
        foreach (var pending in manager.GetBuffer<DroneTaskPendingPublicationElement>(candidates, true))
            if (pending.Candidate.Source == task.Target && (pending.Quantity > 0 || pending.PublicationQueued)) return 0;
        return 1;
    }

    public static void PrepareResume(EntityManager manager, Entity worker, Entity station, Entity assignmentEntity,
        NativeArray<DroneStationNetworkDecision> networks, NativeArray<Entity> storages, NativeArray<Entity> sites,
        Entity registry, NativeList<DroneStationActivationPlan> plans)
    {
        var assignment = manager.GetComponentData<DroneTaskAssignment>(assignmentEntity);
        if (DroneSchedulingUtility.IsTerminal(assignment.State)) return;
        var plan = new DroneStationActivationPlan { Drone = worker, Station = station,
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
        if (assignment.NextAction == DroneActionKindEnum.DropCargo) return;
        if (!DroneTargetPointUtility.TryGetTargetPoint(manager, assignment.Destination, out plan.Destination)) return;
        plans.Add(plan);
    }
}
