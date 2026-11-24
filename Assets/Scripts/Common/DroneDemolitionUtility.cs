using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

public static class DroneDemolitionUtility
{
    public static bool IsTarget(EntityManager manager, Entity target)
    {
        if (target == Entity.Null || !manager.HasComponent<BuildingIdentity>(target)) return false;
        if (manager.HasComponent<Prefab>(target) || manager.HasComponent<IndestructibleBuilding>(target)) return false;
        return manager.HasComponent<GridPosition>(target) && manager.HasComponent<BuildingFootprint>(target) &&
            manager.HasComponent<Direction>(target);
    }

    public static bool IsApprovedTarget(EntityManager manager, Entity target) =>
        IsTarget(manager, target) && (manager.HasComponent<PendingBuildingDemolition>(target) ||
            manager.HasComponent<PreparingStationDemolition>(target));

    public static bool IsCurrentTask(EntityManager manager, in DroneLogisticsTask task)
    {
        if (IsCommittedPickup(manager, task.Target, task)) return true;
        if (!IsApprovedTarget(manager, task.Target)) return false;
        Entity linked = manager.HasComponent<PreparingStationDemolition>(task.Target)
            ? manager.GetComponentData<PreparingStationDemolition>(task.Target).Task
            : manager.GetComponentData<PendingBuildingDemolition>(task.Target).Task;
        return manager.HasComponent<DroneLogisticsTask>(linked) &&
            manager.GetComponentData<DroneLogisticsTask>(linked).CreationSequence == task.CreationSequence;
    }

    public static bool TryGetDemolitionWork(EntityManager manager, Entity root, out DroneLogisticsTask task)
    {
        task = manager.HasComponent<DroneLogisticsTask>(root)
            ? manager.GetComponentData<DroneLogisticsTask>(root) : default;
        return task.Kind == DroneLogisticsTaskKindEnum.BuildingDemolition &&
            ((task.State == DroneLogisticsTaskStateEnum.Open && IsCurrentTask(manager, task)) ||
             IsCommittedPickup(manager, root, task));
    }

    internal static bool IsCommittedPickup(EntityManager manager, Entity root, in DroneLogisticsTask task) =>
        task.Target == root && task.State == DroneLogisticsTaskStateEnum.ExecutionConfirmed &&
        manager.HasBuffer<DroneDemolitionRefundElement>(root) &&
        !manager.GetBuffer<DroneDemolitionRefundElement>(root, true).IsEmpty;

    internal static NativeList<DroneDemolitionRefundElement> ReadRefunds(EntityManager manager, Entity root,
        in DroneLogisticsTask task, Entity config)
    {
        var costs = new NativeList<DroneDemolitionRefundElement>(Allocator.Temp);
        if (IsCommittedPickup(manager, root, task))
        {
            foreach (var material in manager.GetBuffer<DroneDemolitionRefundElement>(root, true))
                costs.Add(material);
            return costs;
        }

        if (!manager.HasBuffer<BuildingConstructionMaterialElement>(config) ||
            !manager.HasComponent<BuildingIdentity>(task.Target)) return costs;
        int id = manager.GetComponentData<BuildingIdentity>(task.Target).BuildingId;
        foreach (var material in manager.GetBuffer<BuildingConstructionMaterialElement>(config, true))
        {
            if (material.BuildingId != id || material.Quantity <= 0) continue;
            int index = -1;
            for (int i = 0; i < costs.Length; i++)
            {
                if (costs[i].ItemType != material.ItemType) continue;
                index = i;
                break;
            }
            if (index < 0)
                costs.Add(new DroneDemolitionRefundElement { ItemType = material.ItemType, Quantity = material.Quantity });
            else
            {
                var cost = costs[index];
                cost.Quantity = (int)math.min(int.MaxValue, (long)cost.Quantity + material.Quantity);
                costs[index] = cost;
            }
        }
        return costs;
    }

    // 빈 슬롯이나 수행자 하나는 한 품목에만 쓴다. 기존 품목의 대안을 찾아 옮길 때 같은 품목은 다시 방문하지 않는다.
    internal static bool TryMatchRefundUnit(ItemTypeEnum type, NativeArray<FixedBitSet> choices,
        NativeArray<ItemTypeEnum> owners, ref FixedBitSet visited)
    {
        if (visited.IsSet((byte)type)) return false;
        visited.Set((byte)type, true);
        for (int i = 0; i < choices.Length; i++)
        {
            if (owners[i] == ItemTypeEnum.None && choices[i].IsSet((byte)type))
            {
                owners[i] = type;
                return true;
            }
        }
        for (int i = 0; i < choices.Length; i++)
        {
            if (choices[i].IsSet((byte)type) && TryMatchRefundUnit(owners[i], choices, owners, ref visited))
            {
                owners[i] = type;
                return true;
            }
        }
        return false;
    }

    internal static int RequiredWorkers(NativeList<DroneDemolitionRefundElement> costs, int capacity)
    {
        if (capacity <= 0) return 0;
        long required = 0;
        foreach (var cost in costs) required += 1L + (cost.Quantity - 1) / capacity;
        return (int)math.clamp(required, 1L, int.MaxValue);
    }

    internal static int CandidateQuantity(EntityManager manager, in DroneTaskCandidateDecisionElement candidate,
        Entity registry, int capacity, bool checkSpace = true)
    {
        if (!TryGetDemolitionWork(manager, candidate.Task, out var task) ||
            !DroneSchedulingUtility.IsIdleWorker(manager, candidate.Worker, capacity) ||
            !DroneSchedulingUtility.TryReadRoute(manager, candidate.RouteRequest, capacity, out var result) ||
            result.Status != DroneRouteEvaluationStatusEnum.Reachable) return 0;

        var request = manager.GetComponentData<DroneRouteEvaluationRequest>(candidate.RouteRequest);
        if (request.Worker != candidate.Worker || request.Source != candidate.Source ||
            request.Destination != candidate.Destination ||
            request.WorkerObservationRevision != candidate.WorkerObservationRevision) return 0;
        if (candidate.ItemType == ItemTypeEnum.None)
            return candidate.Destination == task.Target && request.Kind == DroneRouteKindEnum.DirectToBuilding ? 1 : 0;
        if (candidate.Source != task.Target || request.DemolitionRoot != candidate.Task) return 0;
        int quantity = math.min(candidate.Quantity, capacity);
        return checkSpace
            ? DroneSchedulingUtility.CountStorageSpace(manager, candidate.Destination, candidate.ItemType, registry,
                quantity, generalOnly: true) : quantity;
    }

    internal static bool CanDrain(EntityManager manager, in DroneLogisticsTask task) =>
        (!manager.HasComponent<PreparingStationDemolition>(task.Target) ||
            DroneStationRelocationUtility.IsComplete(manager, task.Target));

    internal static DroneTransportAreaEnum StorageArea(EntityManager manager, Entity source)
    {
        return manager.HasComponent<GeneralItemStorage>(source)
            ? DroneTransportAreaEnum.GeneralStorage : DroneTransportAreaEnum.Input;
    }

    internal static bool IsInventorySource(EntityManager manager, Entity root, Entity source, DroneTransportAreaEnum area)
    {
        return TryGetDemolitionWork(manager, root, out var task) && task.Target == source && CanDrain(manager, task) &&
            (area == DroneTransportAreaEnum.Product
                ? manager.HasBuffer<ProductSlotElement>(source)
                : manager.HasBuffer<StorageSlotElement>(source) &&
                  (area == StorageArea(manager, source) ||
                   area == DroneTransportAreaEnum.Input && manager.HasComponent<FuelStorageSlots>(source)));
    }

    internal static bool IsStoppedBeltItem(EntityManager manager, Entity building, Entity item)
    {
        return manager.HasComponent<BeltComponent>(building) &&
            manager.HasComponent<BeltMovementState>(item) &&
            manager.HasComponent<GridPosition>(item) && manager.HasComponent<ItemIdentity>(item) &&
            math.all(manager.GetComponentData<GridPosition>(building).Value ==
                manager.GetComponentData<GridPosition>(item).Value) &&
            DroneSchedulingUtility.IsWorldItem(manager, item, manager.GetComponentData<ItemIdentity>(item).Type);
    }

    internal static bool IsBeltItemSource(EntityManager manager, Entity root, Entity item) =>
        TryGetDemolitionWork(manager, root, out var task) && IsStoppedBeltItem(manager, task.Target, item);

    internal static bool IsRouteSource(EntityManager manager, Entity root, Entity source) =>
        TryGetDemolitionWork(manager, root, out var task) &&
        (source == task.Target ? CanDrain(manager, task) : IsStoppedBeltItem(manager, task.Target, source));

    internal static int CountInventory(EntityManager manager, Entity building)
    {
        long count = 0;
        if (manager.HasBuffer<StorageSlotElement>(building))
            foreach (var slot in manager.GetBuffer<StorageSlotElement>(building, true))
                count += slot.Quantity;
        if (manager.HasBuffer<ProductSlotElement>(building))
            foreach (var slot in manager.GetBuffer<ProductSlotElement>(building, true))
                count += slot.Quantity;
        return (int)math.min(int.MaxValue, count);
    }

    internal static bool IsDemolitionChildCancellationPlanned(EntityManager manager, Entity candidates, Entity task)
    {
        if (!manager.HasComponent<DroneLogisticsTask>(task)) return false;
        Entity root = manager.GetComponentData<DroneLogisticsTask>(task).DemolitionRoot;
        return root != Entity.Null && DroneTaskRequestDecisionUtility.IsCancellationPlanned(manager, candidates, root);
    }

    public static bool CanConfirm(EntityManager manager, Entity entity, uint revision, uint observationRevision)
    {
        if (!manager.HasComponent<DroneTaskAssignment>(entity)) return false;
        var assignment = manager.GetComponentData<DroneTaskAssignment>(entity);
        return assignment.Revision == revision &&
            TryGetDemolitionWork(manager, assignment.Task, out var task) && CanDrain(manager, task) &&
            CanReceiveAtSite(manager, entity, assignment, task.Target, observationRevision);
    }

    internal static bool CanReceiveAtSite(EntityManager manager, Entity entity, in DroneTaskAssignment assignment,
        Entity site, uint observationRevision = 0)
    {
        if (assignment.State != DroneTaskAssignmentStateEnum.AwaitingDelivery ||
            assignment.NextAction != DroneActionKindEnum.DemolishBuilding) return false;
        Entity worker = assignment.Worker;
        if (!DroneSchedulingUtility.IsActiveEntity(manager, worker) ||
            !manager.HasComponent<DroneWorkerAssignment>(worker) ||
            manager.GetComponentData<DroneWorkerAssignment>(worker).Assignment != entity ||
            !manager.HasComponent<DroneCargoState>(worker) ||
            manager.GetComponentData<DroneCargoState>(worker).Quantity != 0 ||
            !manager.HasComponent<DroneWorkerObservation>(worker) ||
            !manager.HasComponent<DroneBatteryState>(worker) || !manager.HasComponent<LocalTransform>(worker)) return false;
        var observation = manager.GetComponentData<DroneWorkerObservation>(worker);
        if (observationRevision != 0 && observation.Revision != observationRevision) return false;
        float charge = manager.GetComponentData<DroneBatteryState>(worker).CurrentCharge;
        if (!math.isfinite(charge) || charge < 0f) return false;
        return DroneTargetPointUtility.TryGetTargetPoint(manager, site, out float2 point) &&
            math.all(manager.GetComponentData<LocalTransform>(worker).Position.xy == point) &&
            math.all(observation.Position.xy == point);
    }

    internal static bool CanConfirmDemolitionCrew(EntityManager manager, Entity root, in DroneLogisticsTask task,
        NativeArray<Entity> assignments, Entity config, Entity registry, int capacity)
    {
        using var costs = ReadRefunds(manager, root, task, config);
        var remaining = costs.AsArray();
        int members = 0;
        foreach (Entity entity in assignments)
        {
            var assignment = manager.GetComponentData<DroneTaskAssignment>(entity);
            if (assignment.Task != root || assignment.NextAction != DroneActionKindEnum.DemolishBuilding ||
                DroneSchedulingUtility.IsTerminal(assignment.State)) continue;
            members++;
            if (!CanReceiveAtSite(manager, entity, assignment, task.Target)) return false;
            if (assignment.ItemType == ItemTypeEnum.None)
            {
                if (remaining.Length != 0 || assignment.Destination != task.Target) return false;
                continue;
            }
            if (assignment.Source != task.Target || assignment.AssignedQuantity <= 0 ||
                assignment.AssignedQuantity > capacity || !HasDestinationClaim(manager, entity, assignment, registry) ||
                !CanReachRefundStorage(manager, assignment.Worker, task.Target, assignment.Destination, arrived: true))
                return false;
            bool found = false;
            for (int i = 0; i < remaining.Length; i++)
            {
                var row = remaining[i];
                if (row.ItemType != assignment.ItemType) continue;
                row.Quantity -= assignment.AssignedQuantity;
                if (row.Quantity < 0) return false;
                remaining[i] = row;
                found = true;
                break;
            }
            if (!found) return false;
        }
        if (remaining.Length == 0) return members == 1;
        if (members == 0) return false;
        foreach (var material in remaining)
            if (material.Quantity != 0) return false;
        return true;
    }

    internal static bool CanReachRefundStorage(EntityManager manager, Entity worker, Entity site, Entity storage,
        bool arrived = false)
    {
        var flightReads = new RefundFlightReadContext();
        try
        {
            return CanReachRefundStorage(manager, worker, site, storage, ref flightReads, arrived);
        }
        finally
        {
            flightReads.Dispose();
        }
    }

    internal static bool CanReachRefundStorage(EntityManager manager, Entity worker, Entity site, Entity storage,
        ref RefundFlightReadContext flightReads, bool arrived = false)
    {
        if (!DroneTargetPointUtility.TryGetTargetPoint(manager, storage, out float2 destination)) return false;
        float2 origin = manager.GetComponentData<LocalTransform>(worker).Position.xy;
        float2 source = origin;
        if (site != Entity.Null && !DroneTargetPointUtility.TryGetTargetPoint(manager, site, out source)) return false;
        if (!flightReads.TryReadStations(manager)) return false;

        float charge = manager.GetComponentData<DroneBatteryState>(worker).CurrentCharge;
        var plan = arrived || site == Entity.Null
            ? DroneFlightPlanningUtility.Plan(source, destination, charge, flightReads.MaximumCharge,
                flightReads.EnergyPerDistance, flightReads.Stations)
            : DroneFlightPlanningUtility.PlanViaSource(origin, source, destination, charge, flightReads.MaximumCharge,
                flightReads.EnergyPerDistance, flightReads.Stations);
        return plan.Status == DroneRouteEvaluationStatusEnum.Reachable;
    }

    internal struct RefundFlightReadContext : IDisposable
    {
        public NativeList<DroneFlightStation> Stations { get; private set; }
        public float MaximumCharge { get; private set; }
        public float EnergyPerDistance { get; private set; }

        public bool TryReadStations(EntityManager manager)
        {
            if (Stations.IsCreated) return true;

            using var movement = manager.CreateEntityQuery(ComponentType.ReadOnly<DroneMovementConfig>());
            using var battery = manager.CreateEntityQuery(ComponentType.ReadOnly<DroneBatteryConfig>());
            if (movement.IsEmptyIgnoreFilter || battery.IsEmptyIgnoreFilter) return false;

            MaximumCharge = battery.GetSingleton<DroneBatteryConfig>().MaximumCharge;
            EnergyPerDistance = movement.GetSingleton<DroneMovementConfig>().EnergyPerDistance;
            using var stations = manager.CreateEntityQuery(ComponentType.ReadOnly<DroneStation>(),
                ComponentType.ReadOnly<DroneStationNetworkDecision>(), ComponentType.ReadOnly<Storage>(),
                ComponentType.ReadOnly<StorageSlotElement>(), ComponentType.ReadOnly<DockedDroneElement>(),
                ComponentType.Exclude<PendingBuildingDemolition>());
            using var owners = stations.ToEntityArray(Allocator.Temp);
            Stations = new NativeList<DroneFlightStation>(owners.Length, Allocator.Temp);
            DroneStationAutomationUtility.BuildStations(manager, owners, Stations);
            return true;
        }

        public void RefreshDockAvailability(EntityManager manager, Entity station)
        {
            var stations = Stations;
            if (!stations.IsCreated || !manager.HasComponent<DroneStation>(station)) return;
            for (int i = 0; i < stations.Length; i++)
            {
                var flight = stations[i];
                if (flight.Entity != station) continue;
                flight.CanDock = DroneStationAutomationUtility.CanDockAtStation(manager, station);
                stations[i] = flight;
                return;
            }
        }

        public void Invalidate()
        {
            if (!Stations.IsCreated) return;
            Stations.Dispose();
            Stations = default;
        }

        public void Dispose() => Invalidate();
    }

    internal static bool HasDestinationClaim(EntityManager manager, Entity entity, in DroneTaskAssignment assignment, Entity registry) =>
        DroneDemolitionStorageUtility.Quantity(manager, assignment.Destination, assignment.Worker) == assignment.AssignedQuantity &&
        DroneDemolitionStorageUtility.AvailableTransportSpace(manager, assignment.Destination, assignment.ItemType,
            registry, assignment.AssignedQuantity, entity) == assignment.AssignedQuantity;
}
