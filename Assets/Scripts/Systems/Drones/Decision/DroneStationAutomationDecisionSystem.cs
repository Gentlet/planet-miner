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
    private EntityQuery _stations, _workers, _stored, _tasks, _sites, _storages, _assignments, _requests, _results, _registry;
    private Entity _candidates;
    private EntityQueryMask _stationMask, _itemMask, _workerMask;

    public void OnCreate(ref SystemState state)
    {
        _candidates = DroneSchedulingUtility.GetOrCreateCandidates(state.EntityManager);
        _stations = SystemAPI.QueryBuilder().WithAll<DroneStation, DroneStationNetworkDecision, DroneStationStorage, Storage>()
            .WithAll<GridPosition, StoredItemElement, DockedDroneElement>().WithNone<PendingBuildingDemolition>().Build();
        _workers = SystemAPI.QueryBuilder().WithAll<DroneWorker, DroneWorkerAssignment, DroneMovementState, DroneBatteryState>()
            .WithAll<DroneCargoState, DroneActivityState, LocalTransform, StoredItemElement>().Build();
        _stored = SystemAPI.QueryBuilder().WithAll<ItemIdentity, ItemOwnership, LocalTransform>().WithNone<DroneWorker>().Build();
        _tasks = SystemAPI.QueryBuilder().WithAll<DroneLogisticsTask>().Build();
        _sites = SystemAPI.QueryBuilder().WithAll<ConstructionSite, GridPosition>().Build();
        _storages = SystemAPI.QueryBuilder().WithAll<BuildingType, Storage, StoredItemElement, GridPosition>().Build();
        _assignments = SystemAPI.QueryBuilder().WithAll<DroneTaskAssignment>().Build();
        _requests = SystemAPI.QueryBuilder().WithAll<DroneStationRequest>().Build();
        _results = SystemAPI.QueryBuilder().WithAll<DroneAutomaticStationRequest, DroneStationResult>().Build();
        _registry = SystemAPI.QueryBuilder().WithAll<ItemRegistry, ItemConfigElement>().Build();
        DroneStationEligibility.CreateMasks(ref state, out _stationMask, out _itemMask, out _workerMask);
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
        using var stored = _stored.ToEntityArray(Allocator.Temp);
        using var tasks = _tasks.ToEntityArray(Allocator.Temp);
        using var sites = _sites.ToEntityArray(Allocator.Temp);
        using var storages = _storages.ToEntityArray(Allocator.Temp);
        using var assignments = _assignments.ToEntityArray(Allocator.Temp);
        using var requests = _requests.ToComponentDataArray<DroneStationRequest>(Allocator.Temp);
        using var busy = new NativeHashSet<Entity>(math.max(1, requests.Length), Allocator.Temp);
        foreach (var request in requests) busy.Add(request.Drone);
        using var flights = new NativeList<DroneFlightStation>(Allocator.TempJob);
        DroneStationAutomationUtility.BuildStations(manager, stations, flights);
        using var plans = new NativeList<DroneStationActivationPlan>(Allocator.TempJob);
        using var intents = new NativeList<DroneStationAutomationIntent>(Allocator.Temp);
        float maximum = SystemAPI.GetSingleton<DroneBatteryConfig>().MaximumCharge;
        int capacity = SystemAPI.GetSingleton<DroneCapacityState>().CarryingCapacity;
        var config = SystemAPI.GetSingleton<DroneMovementConfig>();
        Entity registry = _registry.GetSingletonEntity();
        var eligibility = DroneStationEligibility.Create(ref state, _stationMask, _itemMask, _workerMask);
        var dedicated = SystemAPI.GetComponentLookup<DroneStationStorage>(true);
        int maximumStack = ItemRegistry.GetMaxStack(manager.GetBuffer<ItemConfigElement>(registry, true), ItemTypeEnum.Drone);
        using var slotRanges = new NativeParallelHashMap<Entity, DroneStationSlotRange>(1, Allocator.Temp);
        using var slotPreview = new NativeList<DroneStationSlot>(Allocator.Temp);

        foreach (Entity worker in workers)
        {
            if (busy.Contains(worker)) continue;
            if (DroneStationAutomationUtility.HasTaskDecisionPriority(manager, _candidates, worker))
            {
                continue;
            }
            Entity station = DroneStationAutomationUtility.FindCurrentStation(manager, worker, stations, eligibility);
            if (station == Entity.Null) continue;
            bool docked = manager.HasComponent<DroneDocked>(worker);
            Entity assignment = manager.GetComponentData<DroneWorkerAssignment>(worker).Assignment;
            bool linked = assignment != Entity.Null && manager.Exists(assignment);
            bool invalidated = false;
            foreach (var invalidation in manager.GetBuffer<DroneTaskInvalidationDecisionElement>(_candidates, true))
                invalidated |= invalidation.Kind == DroneTaskInvalidationDecisionKindEnum.InvalidateAssignment && invalidation.Target == assignment;
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
                DroneStationAutomationUtility.PrepareResume(manager, worker, station, assignment, networks, storages, sites, registry, plans);
                continue;
            }
            if (docked && !linked && manager.GetBuffer<StoredItemElement>(worker, true).IsEmpty)
                DroneStationAutomationUtility.PrepareDemand(manager, _candidates, worker, station, tasks, sites, storages,
                    assignments, networks, registry, capacity, maximum, plans);
            var operation = DroneStationOperation.Store;
            var request = new DroneStationRequest { Drone = worker, Station = station, Operation = operation };
            if (eligibility.Validate(request, maximum, true) != DroneStationRequestStatus.Applied)
            {
                if (docked) continue;
                operation = DroneStationOperation.Dock;
                request.Operation = operation;
                if (eligibility.Validate(request, maximum, true) != DroneStationRequestStatus.Applied) continue;
            }
            // 만석 정거장에 실패 요청을 매 틱 만들면 귀환 재선택을 계속 막는다.
            // 한 수행자의 자격만 확인하고 실제 공유 공간 중재는 기존 Reservation에 맡긴다.
            slotRanges.Clear();
            slotPreview.Clear();
            int preferred = docked ? manager.GetComponentData<DroneDocked>(worker).SlotIndex : -1;
            if (DroneStationLifecycleUtility.ReserveSlot(station, worker, operation, preferred, maximumStack,
                eligibility.Storages, dedicated, eligibility.Items, eligibility.Docked, slotRanges, slotPreview) < 0) continue;
            intents.Add(new DroneStationAutomationIntent { Drone = worker, Station = station, Operation = operation });
        }
        foreach (Entity item in stored)
        {
            if (busy.Contains(item) || manager.GetComponentData<ItemIdentity>(item).Type != ItemTypeEnum.Drone) continue;
            Entity station = manager.GetComponentData<ItemOwnership>(item).Owner;
            if (station == Entity.Null || !manager.Exists(station) || !eligibility.StationMask.MatchesIgnoreFilter(station)) continue;
            var request = new DroneStationRequest { Drone = item, Station = station, Operation = DroneStationOperation.Activate };
            if (eligibility.Validate(request, maximum, true) != DroneStationRequestStatus.Applied) continue;
            DroneStationAutomationUtility.PrepareDemand(manager, _candidates, item, station, tasks, sites, storages,
                assignments, networks, registry, capacity, maximum, plans);
        }
        using var outcomes = new NativeArray<DroneFlightPlan>(plans.Length, Allocator.TempJob);
        new EvaluateActivationRoutesJob { Plans = plans.AsArray(), Stations = flights, Results = outcomes,
            Maximum = maximum, Rate = config.EnergyPerDistance }.Schedule(plans.Length, 32).Complete();
        using var activated = new NativeHashSet<Entity>(math.max(1, plans.Length), Allocator.Temp);
        for (int i = 0; i < plans.Length; i++)
        {
            var plan = plans[i];
            if (activated.Contains(plan.Drone) || outcomes[i].Status != DroneRouteEvaluationStatusEnum.Reachable) continue;
            // 현재 정거장에서 충전을 더 기다려야 하는 경로는 출고하지 않는다.
            if (outcomes[i].ChargingStation == plan.Station && math.all(outcomes[i].NextPosition == plan.Origin)) continue;
            activated.Add(plan.Drone);
            intents.Add(new DroneStationAutomationIntent { Drone = plan.Drone, Station = plan.Station,
                Operation = DroneStationOperation.Activate, DemandTask = plan.Task, Quantity = plan.Quantity });
        }
        // 요청 접수의 구조 변경 전에 모든 원본/Lookup 읽기를 끝낸다.
        foreach (var intent in intents)
        {
            if (intent.Operation != DroneStationOperation.Activate && activated.Contains(intent.Drone)) continue;
            Entity request = DroneStationRequestUtility.Submit(manager, intent.Drone, intent.Station, intent.Operation);
            manager.AddComponentData(request, new DroneAutomaticStationRequest { DemandTask = intent.DemandTask, Quantity = intent.Quantity });
        }
    }

    [BurstCompile]
    private struct EvaluateActivationRoutesJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<DroneStationActivationPlan> Plans;
        [ReadOnly] public NativeList<DroneFlightStation> Stations;
        public NativeArray<DroneFlightPlan> Results;
        public float Maximum, Rate;
        public void Execute(int index)
        {
            var plan = Plans[index];
            Results[index] = plan.ViaSource
                ? DroneFlightPlanningUtility.PlanViaSource(plan.Origin, plan.Source, plan.Destination, plan.Charge, Maximum, Rate, Stations)
                : DroneFlightPlanningUtility.Plan(plan.Origin, plan.Destination, plan.Charge, Maximum, Rate, Stations);
        }
    }
}
