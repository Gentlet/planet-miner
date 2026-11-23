using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>이전 틱의 실제 정거장 도착과 현재 작업 수요를 정거장 요청으로 전달한다. 실물 전환은 기존 소유자에게 맡긴다.</summary>
[UpdateInGroup(typeof(DroneDecisionGroup))]
[UpdateAfter(typeof(DroneTaskDecisionSystem))]
[UpdateBefore(typeof(DroneStationLifecycleDecisionSystem))]
public partial struct DroneStationAutomationDecisionSystem : ISystem
{
    private EntityQuery _stations, _workers, _tasks, _sites, _storages, _assignments, _requests, _results, _registry;
    private Entity _candidates;
    private EntityQuery _demolitionConfig;
    private EntityQueryMask _stationMask, _workerMask;
    private DroneStationEligibility _eligibility;

    public void OnCreate(ref SystemState state)
    {
        _candidates = DroneSchedulingUtility.GetOrCreateCandidates(state.EntityManager);
        _stations = SystemAPI.QueryBuilder().WithAll<DroneStation, DroneStationNetworkDecision, DroneStationStorage, Storage>()
            .WithAll<GridPosition, StorageSlotElement, DockedDroneElement>().WithNone<PendingBuildingDemolition>().Build();
        _workers = SystemAPI.QueryBuilder().WithAll<DroneWorker, DroneWorkerAssignment, DroneMovementState, DroneBatteryState>()
            .WithAll<DroneCargoState, DroneActivityState, LocalTransform>().Build();
        _tasks = SystemAPI.QueryBuilder().WithAll<DroneLogisticsTask>().Build();
        _sites = SystemAPI.QueryBuilder().WithAll<ConstructionSite, GridPosition>().Build();
        _storages = SystemAPI.QueryBuilder().WithAll<BuildingIdentity, Storage, StorageSlotElement, GridPosition>().Build();
        _assignments = SystemAPI.QueryBuilder().WithAll<DroneTaskAssignment>().Build();
        _requests = SystemAPI.QueryBuilder().WithAll<DroneStationRequest>().Build();
        _results = SystemAPI.QueryBuilder().WithAll<DroneAutomaticStationRequest, DroneStationResult>().Build();
        _registry = SystemAPI.QueryBuilder().WithAll<ItemRegistry, ItemConfigElement>().Build();
        _demolitionConfig = state.GetEntityQuery(ComponentType.ReadOnly<BuildingConstructionMaterialElement>());
        DroneStationEligibility.CreateMasks(ref state, out _stationMask, out _workerMask);
        _eligibility = DroneStationEligibility.Create(ref state, _stationMask, _workerMask);
        state.RequireForUpdate<PrefabDatabaseReady>();
        state.RequireForUpdate<DroneMovementConfig>();
        state.RequireForUpdate<DroneBatteryConfig>();
        state.RequireForUpdate<DroneCapacityState>();
        state.RequireForUpdate(_registry);
    }

    public void OnUpdate(ref SystemState state)
    {
        state.CompleteDependency();
        var manager = state.EntityManager;
        using (var results = _results.ToEntityArray(Allocator.Temp))
            foreach (Entity result in results) DroneStationRequestUtility.TryConsumeResult(manager, result, out _);
        using var stations = _stations.ToEntityArray(Allocator.Temp);
        using var networks = _stations.ToComponentDataArray<DroneStationNetworkDecision>(Allocator.Temp);
        using var workers = _workers.ToEntityArray(Allocator.Temp);
        using var tasks = _tasks.ToEntityArray(Allocator.Temp);
        using var sites = _sites.ToEntityArray(Allocator.Temp);
        using var storages = _storages.ToEntityArray(Allocator.Temp);
        using var assignments = _assignments.ToEntityArray(Allocator.Temp);
        using var requests = _requests.ToComponentDataArray<DroneStationRequest>(Allocator.Temp);
        using var excludedWorkers = new NativeHashSet<Entity>(math.max(1, workers.Length), Allocator.Temp);
        foreach (var request in requests) excludedWorkers.Add(request.Drone);
        using var dockedRetargets = new NativeHashMap<Entity, DroneTaskCandidateDecisionElement>(16, Allocator.Temp);
        using var evaluatingTargets = new NativeHashSet<Entity>(16, Allocator.Temp);
        DroneStationAutomationUtility.CollectTaskDecisionPriority(manager, _candidates,
            SystemAPI.GetComponentLookup<DroneWorkerObservation>(true), excludedWorkers, dockedRetargets, evaluatingTargets);
        var targetsUnderEvaluation = evaluatingTargets.AsReadOnly();
        using var flights = new NativeList<DroneFlightStation>(Allocator.TempJob);
        DroneStationAutomationUtility.BuildStations(manager, stations, flights);
        using var plans = new NativeList<DroneStationActivationPlan>(Allocator.TempJob);
        Entity demolitionConfig = _demolitionConfig.IsEmptyIgnoreFilter ? Entity.Null : _demolitionConfig.GetSingletonEntity();
        using var intents = new NativeList<DroneStationAutomationIntent>(Allocator.Temp);
        float maximum = SystemAPI.GetSingleton<DroneBatteryConfig>().MaximumCharge;
        int capacity = SystemAPI.GetSingleton<DroneCapacityState>().CarryingCapacity;
        var config = SystemAPI.GetSingleton<DroneMovementConfig>();
        Entity registry = _registry.GetSingletonEntity();
        _eligibility.Update(ref state);
        var eligibility = _eligibility;
        using var dockFootprints = new NativeList<DroneStationDockFootprint>(stations.Length, Allocator.Temp);
        // 겹치는 footprint에서도 현재 정거장 쿼리 순서대로 첫 유효한 후보를 선택한다.
        foreach (Entity station in stations)
        {
            if (eligibility.TryReadDockFootprint(station, out var footprint)) dockFootprints.Add(footprint);
        }
        var dedicated = SystemAPI.GetComponentLookup<DroneStationStorage>(true);
        var storageLookup = SystemAPI.GetBufferLookup<StorageSlotElement>(true);
        int maximumStack = ItemRegistry.GetMaxStack(manager.GetBuffer<ItemConfigElement>(registry, true), ItemTypeEnum.Drone);
        using var slotRanges = new NativeParallelHashMap<Entity, DroneStationSlotRange>(1, Allocator.Temp);
        using var slotPreview = new NativeList<DroneStationSlot>(Allocator.Temp);
        bool hasReadInvalidations = false;
        DynamicBuffer<DroneTaskInvalidationDecisionElement> invalidations = default;
        using var demandTasks = new DroneStationAutomationUtility.DemandTasksByNetwork(manager, _candidates, tasks, sites, assignments, networks);
        using var storagesByNetwork = new StorageCandidatesByNetwork(manager, storages, requireStorageRole: true);
        using var endpointReads = new DroneStationAutomationUtility.DemandEndpointReads(manager, registry);

        foreach (Entity worker in workers)
        {
            if (manager.HasComponent<DroneStationRelocation>(worker)) continue;
            if (excludedWorkers.Contains(worker)) continue;
            Entity station = DroneStationAutomationUtility.FindCurrentStation(manager, worker, dockFootprints, eligibility);
            if (station == Entity.Null) continue;
            bool docked = manager.HasComponent<DroneDocked>(worker);
            Entity assignment = manager.GetComponentData<DroneWorkerAssignment>(worker).Assignment;
            bool linked = assignment != Entity.Null && manager.Exists(assignment);
            bool invalidated = false;
            bool cancellationPlanned = false;
            if (!hasReadInvalidations)
            {
                invalidations = manager.GetBuffer<DroneTaskInvalidationDecisionElement>(_candidates, true);
                hasReadInvalidations = true;
            }
            foreach (var invalidation in invalidations)
            {
                invalidated |= invalidation.Kind == DroneTaskInvalidationDecisionKindEnum.InvalidateAssignment && invalidation.Target == assignment;
                cancellationPlanned |= invalidation.Kind == DroneTaskInvalidationDecisionKindEnum.CancelAssignment && invalidation.Target == assignment;
            }
            if (cancellationPlanned) continue;
            if (invalidated)
            {
                if (!docked) continue;
                if (!linked) continue;
                // Retargeting의 반복 정리 의도는 보존된 화물의 정박 해제/재배정을 막지 않는다.
                if (manager.GetComponentData<DroneTaskAssignment>(assignment).State != DroneTaskAssignmentStateEnum.Retargeting)
                {
                    continue;
                }
            }
            if (docked && linked && manager.GetComponentData<DroneBatteryState>(worker).CurrentCharge > 0f)
            {
                DroneStationAutomationUtility.PrepareResume(manager, worker, station, assignment, dockedRetargets, plans);
                continue;
            }
            if (docked && !linked && manager.GetComponentData<DroneCargoState>(worker).Quantity == 0)
            {
                DroneStationAutomationUtility.PrepareDemand(manager, worker, station, demandTasks, storagesByNetwork,
                    endpointReads, targetsUnderEvaluation, networks, registry, capacity, maximum, plans, demolitionConfig: demolitionConfig);
            }
            var request = new DroneStationRequest { Drone = worker, Station = station, Operation = DroneStationOperation.Store };
            if (eligibility.Validate(request, maximum, true) != DroneStationRequestStatus.Applied)
            {
                if (docked) continue;
                request.Operation = DroneStationOperation.Dock;
                if (eligibility.Validate(request, maximum, true) != DroneStationRequestStatus.Applied) continue;
            }
            // 만석 정거장에 실패 요청을 매 틱 만들면 귀환 재선택을 계속 막는다.
            // 한 수행자의 자격만 확인하고 실제 공유 공간 중재는 기존 Reservation에 맡긴다.
            slotRanges.Clear();
            slotPreview.Clear();
            int preferred = docked ? manager.GetComponentData<DroneDocked>(worker).SlotIndex : -1;
            if (DroneStationLifecycleUtility.ReserveSlot(request, preferred, maximumStack,
                eligibility.Storages, dedicated, eligibility.FuelSlots, storageLookup, eligibility.Docked, slotRanges, slotPreview, eligibility.Reserved) < 0) continue;
            intents.Add(new DroneStationAutomationIntent { Drone = request.Drone, Station = request.Station, Operation = request.Operation });
        }
        if (tasks.Length > 0)
        {
            foreach (Entity station in stations)
            {
                if (manager.HasComponent<PreparingStationDemolition>(station)) continue;
                var inventory = storageLookup[station];
                for (int slot = 0; slot < inventory.Length; slot++)
                {
                    if (inventory[slot].ItemType != ItemTypeEnum.Drone) continue;
                    int alreadyRequested = 0;
                    foreach (var request in requests)
                    {
                        if (request.Operation == DroneStationOperation.ActivateStored &&
                            request.Station == station && request.SourceSlotIndex == slot) alreadyRequested++;
                    }
                    int count = inventory[slot].Quantity - alreadyRequested;
                    if (count <= 0) continue;
                    DroneStationAutomationUtility.PrepareDemand(manager, Entity.Null, station, demandTasks, storagesByNetwork,
                        endpointReads, targetsUnderEvaluation, networks, registry, capacity, maximum, plans, slot, count, demolitionConfig);
                }
            }
        }
        using var resultIndexes = new NativeArray<int>(plans.Length, Allocator.TempJob);
        using var outcomes = new NativeArray<DroneFlightPlan>(plans.Length, Allocator.TempJob);
        JobHandle indexed = default;
        if (plans.Length > 1)
        {
            indexed = new IndexActivationRoutesJob
            {
                Plans = plans.AsArray(),
                ResultIndexes = resultIndexes
            }.Schedule();
        }
        if (plans.Length > 0)
        {
            new EvaluateActivationRoutesJob
            {
                Plans = plans.AsArray(),
                ResultIndexes = resultIndexes,
                Stations = flights,
                Results = outcomes,
                Maximum = maximum,
                Rate = config.EnergyPerDistance
            }.Schedule(plans.Length, 32, indexed).Complete();
        }
        using var activated = new NativeHashSet<Entity>(math.max(1, plans.Length), Allocator.Temp);
        var plannedStock = new NativeHashMap<int3, int>(math.max(1, plans.Length), Allocator.Temp);
        var plannedDemand = new NativeHashMap<Entity, int>(math.max(1, tasks.Length), Allocator.Temp);
        for (int i = 0; i < plans.Length; i++)
        {
            var plan = plans[i];
            if (plan.StockSlot < 0 && activated.Contains(plan.Drone))
            {
                continue;
            }
            var outcome = outcomes[resultIndexes[i]];
            if (outcome.Status != DroneRouteEvaluationStatusEnum.Reachable)
            {
                continue;
            }
            if (outcome.GetChargingStationAtOrigin(plan.Origin) == plan.Station)
            {
                continue;
            }
            int demandUsed = 0;
            int remaining = 0;
            if (plan.Task != Entity.Null)
            {
                plannedDemand.TryGetValue(plan.Task, out demandUsed);
                remaining = demandTasks.GetPreparedDemand(plan.Task) - demandUsed;
                if (remaining <= 0)
                {
                    continue;
                }
            }
            if (plan.StockSlot < 0)
            {
                // 기존 배정의 재개는 수요를 새로 점유하지 않는다. 신규 정박 출고는 보관 재고와 같은 수요를 쓴다.
                int quantity = plan.Task == Entity.Null ? plan.Quantity : math.min(plan.Quantity, remaining);
                activated.Add(plan.Drone);
                intents.Add(new DroneStationAutomationIntent { Drone = plan.Drone, Station = plan.Station,
                    Operation = DroneStationOperation.Activate, DemandTask = plan.Task, Quantity = quantity });
                if (plan.Task != Entity.Null)
                {
                    plannedDemand[plan.Task] = demandUsed + quantity;
                }
                continue;
            }
            var stockKey = new int3(plan.Station.Index, plan.Station.Version, plan.StockSlot);
            plannedStock.TryGetValue(stockKey, out int departing);
            while (departing < plan.StockOrdinal && remaining > 0)
            {
                int quantity = math.min(plan.Quantity, remaining);
                intents.Add(new DroneStationAutomationIntent { Station = plan.Station, StockSlot = plan.StockSlot,
                    Operation = DroneStationOperation.ActivateStored, DemandTask = plan.Task, Quantity = quantity });
                departing++;
                remaining -= quantity;
                demandUsed += quantity;
            }
            plannedStock[stockKey] = departing;
            plannedDemand[plan.Task] = demandUsed;
        }
        plannedStock.Dispose();
        plannedDemand.Dispose();
        // 요청 접수의 구조 변경 전에 모든 원본/Lookup 읽기를 끝낸다.
        foreach (var intent in intents)
        {
            if ((intent.Operation is DroneStationOperation.Dock or DroneStationOperation.Store) && activated.Contains(intent.Drone)) continue;
            Entity request = DroneStationRequestUtility.Submit(manager, intent.Drone, intent.Station, intent.Operation, intent.StockSlot);
            manager.AddComponentData(request, new DroneAutomaticStationRequest { DemandTask = intent.DemandTask, Quantity = intent.Quantity });
        }
    }

    private readonly struct ActivationRouteKey : IEquatable<ActivationRouteKey>
    {
        private readonly uint2 _origin, _source, _destination;
        private readonly uint _charge;
        private readonly bool _viaSource;

        public ActivationRouteKey(in DroneStationActivationPlan plan)
        {
            _origin = math.asuint(plan.Origin);
            _source = plan.ViaSource ? math.asuint(plan.Source) : default;
            _destination = math.asuint(plan.Destination);
            _charge = math.asuint(plan.Charge);
            _viaSource = plan.ViaSource;
        }

        public bool Equals(ActivationRouteKey other) =>
            math.all(_origin == other._origin) && math.all(_source == other._source) &&
            math.all(_destination == other._destination) && _charge == other._charge && _viaSource == other._viaSource;

        public override bool Equals(object obj) => obj is ActivationRouteKey other && Equals(other);

        public override int GetHashCode() => (int)math.hash(new uint4(math.hash(_origin), math.hash(_source),
            math.hash(_destination), _charge ^ (_viaSource ? 1u : 0u)));
    }

    [BurstCompile]
    private struct IndexActivationRoutesJob : IJob
    {
        [ReadOnly] public NativeArray<DroneStationActivationPlan> Plans;
        [WriteOnly] public NativeArray<int> ResultIndexes;

        public void Execute()
        {
            // 평가 입력만 공유한다. 출고 후보의 순서, 작업과 정거장별 차감은 원래 계획을 사용한다.
            using var routes = new NativeHashMap<ActivationRouteKey, int>(Plans.Length, Allocator.Temp);
            for (int i = 0; i < Plans.Length; i++)
            {
                var key = new ActivationRouteKey(Plans[i]);
                if (!routes.TryGetValue(key, out int first))
                {
                    first = i;
                    routes.Add(key, first);
                }
                ResultIndexes[i] = first;
            }
        }
    }

    [BurstCompile]
    private struct EvaluateActivationRoutesJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<DroneStationActivationPlan> Plans;
        [ReadOnly] public NativeArray<int> ResultIndexes;
        [ReadOnly] public NativeList<DroneFlightStation> Stations;
        public NativeArray<DroneFlightPlan> Results;
        public float Maximum, Rate;
        public void Execute(int index)
        {
            if (ResultIndexes[index] != index)
            {
                return;
            }
            var plan = Plans[index];
            Results[index] = plan.ViaSource
                ? DroneFlightPlanningUtility.PlanViaSource(plan.Origin, plan.Source, plan.Destination, plan.Charge, Maximum, Rate, Stations)
                : DroneFlightPlanningUtility.Plan(plan.Origin, plan.Destination, plan.Charge, Maximum, Rate, Stations);
        }
    }
}
