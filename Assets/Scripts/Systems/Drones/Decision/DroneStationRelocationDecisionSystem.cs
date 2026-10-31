using Unity.Collections;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>전원 이관 자격과 재배치 진행 의도를 준비한다. 실제 역할 전환은 기존 Station Lifecycle이 소유한다.</summary>
[UpdateInGroup(typeof(DroneDecisionGroup))]
[UpdateAfter(typeof(DroneTaskDecisionSystem))]
[UpdateBefore(typeof(DroneStationAutomationDecisionSystem))]
[UpdateBefore(typeof(DroneStationLifecycleDecisionSystem))]
public partial struct DroneStationRelocationDecisionSystem : ISystem
{
    private EntityQuery _sources, _stations, _actors, _results, _assignments;
    private Entity _candidates;

    public void OnCreate(ref SystemState state)
    {
        _candidates = DroneSchedulingUtility.GetOrCreateCandidates(state.EntityManager);
        _sources = state.GetEntityQuery(typeof(PreparingStationDemolition), typeof(StoredItemElement), typeof(DockedDroneElement));
        _stations = state.GetEntityQuery(typeof(DroneStation), typeof(DroneStationStorage), typeof(Storage),
            typeof(DroneStationNetworkDecision), typeof(GridPosition), typeof(StoredItemElement), typeof(DockedDroneElement),
            typeof(DroneStationReservedSlotElement));
        _actors = state.GetEntityQuery(new EntityQueryDesc
        {
            All = new[] { ComponentType.ReadOnly<DroneStationRelocation>() },
            Options = EntityQueryOptions.IncludeDisabledEntities
        });
        _assignments = state.GetEntityQuery(typeof(DroneTaskAssignment));
        _results = state.GetEntityQuery(typeof(DroneStationRelocationRequest), typeof(DroneStationResult));
        state.RequireForUpdate<DroneBatteryConfig>();
        state.RequireForUpdate<DroneMovementConfig>();
    }

    public void OnUpdate(ref SystemState state)
    {
        state.CompleteDependency();
        var manager = state.EntityManager;
        if (_sources.IsEmptyIgnoreFilter && _actors.IsEmptyIgnoreFilter && _results.IsEmptyIgnoreFilter)
        {
            manager.GetBuffer<DroneStationRelocationCandidateElement>(_candidates).Clear();
            manager.GetBuffer<DroneStationRelocationIntentElement>(_candidates).Clear();
            return;
        }
        using var stations = _stations.ToEntityArray(Allocator.Temp);
        using var proposals = new NativeList<DroneStationRelocationCandidateElement>(Allocator.Temp);
        using var intents = new NativeList<DroneStationRelocationIntentElement>(Allocator.Temp);
        using var flights = new NativeList<DroneFlightStation>(Allocator.Temp);
        DroneStationAutomationUtility.BuildStations(manager, stations, flights);
        float maximum = SystemAPI.GetSingleton<DroneBatteryConfig>().MaximumCharge;
        var config = SystemAPI.GetSingleton<DroneMovementConfig>();
        using var sources = _sources.ToEntityArray(Allocator.Temp);
        using var incomingAssignments = _assignments.ToEntityArray(Allocator.Temp);
        foreach (Entity source in sources)
        {
            if (!DroneSchedulingUtility.IsActiveEntity(manager, source)) continue;
            var preparation = manager.GetComponentData<PreparingStationDemolition>(source);
            if (!manager.HasComponent<DroneLogisticsTask>(preparation.Task)) continue;
            if (preparation.Started) continue;
            if (manager.GetComponentData<DroneLogisticsTask>(preparation.Task).State != DroneLogisticsTaskStateEnum.Open ||
                DroneTaskRequestDecisionUtility.IsCancellationPlanned(manager, _candidates, preparation.Task)) continue;
            if (!manager.GetBuffer<DroneStationReservedSlotElement>(source, true).IsEmpty) continue;
            bool receivingCommittedCargo = false;
            foreach (Entity assignment in incomingAssignments)
                receivingCommittedCargo |= DroneStationRelocationUtility.CanReceiveCommittedDroneCargo(manager, source, assignment);
            if (receivingCommittedCargo) continue;
            using var members = new NativeList<Entity>(Allocator.Temp);
            bool ready = true;
            foreach (var row in manager.GetBuffer<StoredItemElement>(source, true))
            {
                if (row.ItemType != ItemTypeEnum.Drone) continue;
                if (!DroneSchedulingUtility.IsActiveEntity(manager, row.ItemEntity) ||
                    DroneSchedulingUtility.HasDestroy(manager, row.ItemEntity) ||
                    manager.HasComponent<DroneStationRelocation>(row.ItemEntity))
                {
                    ready = false;
                    break;
                }
                if (manager.HasComponent<DroneBatteryState>(row.ItemEntity) &&
                    manager.GetComponentData<DroneBatteryState>(row.ItemEntity).CurrentCharge != maximum)
                {
                    ready = false;
                    break;
                }
                members.Add(row.ItemEntity);
            }
            if (!ready) continue;
            foreach (var row in manager.GetBuffer<DockedDroneElement>(source, true))
            {
                Entity drone = row.Drone;
                if (!DroneSchedulingUtility.IsActiveEntity(manager, drone) || manager.HasComponent<DroneStationRelocation>(drone) ||
                    manager.GetComponentData<DroneWorkerAssignment>(drone).Assignment != Entity.Null ||
                    !manager.GetBuffer<StoredItemElement>(drone, true).IsEmpty ||
                    manager.GetComponentData<DroneCargoState>(drone).Origin != DroneCargoOriginEnum.None ||
                    manager.GetComponentData<DroneBatteryState>(drone).CurrentCharge != maximum)
                {
                    ready = false;
                    break;
                }
                members.Add(drone);
            }
            if (!ready) continue;
            if (members.IsEmpty) proposals.Add(new DroneStationRelocationCandidateElement { Source = source, Task = preparation.Task });
            foreach (Entity drone in members)
                proposals.Add(new DroneStationRelocationCandidateElement { Source = source, Task = preparation.Task, Drone = drone });
        }
        using var actors = _actors.ToEntityArray(Allocator.Temp);
        foreach (Entity drone in actors)
        {
            var relocation = manager.GetComponentData<DroneStationRelocation>(drone);
            bool stopUnstarted = DroneStationRelocationUtility.ShouldStopUnstartedRelocation(manager, relocation, _candidates);
            bool destinationValid = DroneSchedulingUtility.IsStorage(manager, relocation.Destination) &&
                DroneStationRelocationUtility.ReservedSlot(manager, relocation.Destination, drone) == relocation.SlotIndex;
            if (!destinationValid && !(stopUnstarted && !relocation.HasDeparted))
                proposals.Add(new DroneStationRelocationCandidateElement
                { Source = relocation.Source, Task = relocation.Task, Drone = drone, Replan = true });
            PrepareActor(manager, drone, relocation, stopUnstarted, destinationValid, maximum, config, flights, intents);
        }
        using (var results = _results.ToEntityArray(Allocator.Temp))
            foreach (Entity result in results)
                if (!manager.HasComponent<DroneStationRelocation>(manager.GetComponentData<DroneStationResult>(result).Drone))
                    DroneStationRequestUtility.TryConsumeResult(manager, result, out _);
        var proposalValues = proposals.AsArray();
        for (int i = 0; i < proposalValues.Length; i++)
        {
            var proposal = proposals[i];
            proposal.CreationSequence = manager.HasComponent<DroneLogisticsTask>(proposal.Task)
                ? manager.GetComponentData<DroneLogisticsTask>(proposal.Task).CreationSequence : ulong.MaxValue;
            proposalValues[i] = proposal;
        }
        proposals.AsArray().Sort(new CandidateComparer());
        manager.GetBuffer<DroneStationRelocationCandidateElement>(_candidates).Clear();
        manager.GetBuffer<DroneStationRelocationCandidateElement>(_candidates).AddRange(proposals.AsArray());
        manager.GetBuffer<DroneStationRelocationIntentElement>(_candidates).Clear();
        manager.GetBuffer<DroneStationRelocationIntentElement>(_candidates).AddRange(intents.AsArray());
    }

    private static void Hold(EntityManager manager, Entity drone, Entity station = default)
    {
        if (!manager.HasComponent<DroneWorker>(drone)) return;
        manager.SetComponentData(drone, new DroneMovementDecision
        {
            Ready = true, ClearFailure = true, Target = manager.GetComponentData<LocalTransform>(drone).Position.xy,
            Purpose = station == Entity.Null ? DroneFlightPurposeEnum.None : DroneFlightPurposeEnum.WaitingForCharge,
            Station = station
        });
    }

    private struct CandidateComparer : IComparer<DroneStationRelocationCandidateElement>
    {
        public int Compare(DroneStationRelocationCandidateElement a, DroneStationRelocationCandidateElement b)
        {
            if (a.Replan != b.Replan) return a.Replan ? -1 : 1;
            int order = a.CreationSequence.CompareTo(b.CreationSequence);
            if (order != 0) return order;
            order = a.Source.Index.CompareTo(b.Source.Index);
            if (order != 0) return order;
            order = a.Task.Index.CompareTo(b.Task.Index);
            return order != 0 ? order : a.Drone.Index.CompareTo(b.Drone.Index);
        }
    }

    private static void PrepareActor(EntityManager manager, Entity drone, in DroneStationRelocation relocation,
        bool stopUnstarted, bool destinationValid, float maximum, in DroneMovementConfig config,
        NativeList<DroneFlightStation> allFlights, NativeList<DroneStationRelocationIntentElement> intents)
    {
        Hold(manager, drone, manager.HasComponent<DroneDocked>(drone) ? manager.GetComponentData<DroneDocked>(drone).Station : Entity.Null);
        var intent = new DroneStationRelocationIntentElement { Drone = drone, Stage = relocation.Stage };
        if (relocation.Request != Entity.Null)
        {
            if (!DroneStationRequestUtility.TryConsumeResult(manager, relocation.Request, out var result)) return;
            if (result.Status == DroneStationRequestStatus.Applied)
            {
                if (result.Operation == DroneStationOperation.Activate)
                {
                    intent.Stage = DroneStationRelocationStageEnum.Travelling;
                    intent.SetDeparturePosition = !relocation.HasDeparted;
                }
                else if (result.Operation == DroneStationOperation.Dock)
                {
                    intent.Stage = DroneStationRelocationStageEnum.Charging;
                }
                else
                {
                    intent.Finish = true;
                    intent.Completed = true;
                }
            }
            if (stopUnstarted && !relocation.HasDeparted)
            {
                intent.Finish = true;
                intent.Completed = false;
            }
            intents.Add(intent);
            return;
        }
        if (stopUnstarted && !relocation.HasDeparted)
        {
            intent.Finish = true;
            intents.Add(intent);
            return;
        }
        if (!DroneSchedulingUtility.IsActiveEntity(manager, drone)) return;
        if (relocation.Stage == DroneStationRelocationStageEnum.WaitingForActivation)
        {
            Submit(manager, drone, relocation.Source, DroneStationOperation.Activate, ref intent, intents);
            return;
        }
        if (!manager.HasComponent<DroneWorker>(drone)) return;
        float charge = manager.GetComponentData<DroneBatteryState>(drone).CurrentCharge;
        if (relocation.Stage == DroneStationRelocationStageEnum.Charging)
        {
            // 소실된 정박은 Station Lifecycle이 EndSimulation에 해제한 뒤 비행 판단을 재개한다.
            if (!manager.HasComponent<DroneDocked>(drone))
            {
                intent.Stage = DroneStationRelocationStageEnum.Travelling;
                intents.Add(intent);
                return;
            }
            Entity dock = manager.GetComponentData<DroneDocked>(drone).Station;
            if (charge < maximum) return;
            Submit(manager, drone, dock, dock == relocation.Destination ? DroneStationOperation.Store : DroneStationOperation.Activate,
                ref intent, intents);
            return;
        }
        if (!destinationValid) return;
        float2 position = manager.GetComponentData<LocalTransform>(drone).Position.xy;
        Entity arrived = manager.GetComponentData<DroneMovementState>(drone).ChargingStation;
        if (arrived != Entity.Null && DroneTargetPointUtility.TryGetTargetPoint(manager, arrived, out var arrivedPoint) &&
            math.all(position == arrivedPoint) && (arrived == relocation.Destination || charge < maximum))
        {
            Hold(manager, drone, arrived);
            Submit(manager, drone, arrived, DroneStationOperation.Dock, ref intent, intents);
            return;
        }
        if (!DroneTargetPointUtility.TryGetTargetPoint(manager, relocation.Destination, out var goal)) return;
        using var flights = new NativeList<DroneFlightStation>(Allocator.Temp);
        foreach (var row in allFlights)
        {
            if (charge >= maximum && math.all(row.Position == position)) continue;
            var station = row;
            if (station.Entity == relocation.Destination) station.CanDock = true;
            if (station.Entity == relocation.Source)
            {
                for (int slot = 0; slot < manager.GetComponentData<Storage>(station.Entity).SlotCount; slot++)
                {
                    bool occupied = StationStorageUtility.IsDockOccupied(manager, station.Entity, slot) ||
                        DroneStationRelocationUtility.IsReserved(manager, station.Entity, slot);
                    foreach (var item in manager.GetBuffer<StoredItemElement>(station.Entity, true)) occupied |= item.SlotIndex == slot;
                    if (!occupied)
                    {
                        station.CanDock = true;
                        break;
                    }
                }
            }
            flights.Add(station);
        }
        var plan = DroneFlightPlanningUtility.Plan(position, goal, charge, maximum, config.EnergyPerDistance, flights);
        if (plan.Status == DroneRouteEvaluationStatusEnum.Reachable)
        {
            manager.SetComponentData(drone, new DroneMovementDecision
            {
                Ready = true, ClearFailure = true, Purpose = DroneFlightPurposeEnum.StationRelocation,
                Target = plan.NextPosition, Station = plan.ChargingStation == Entity.Null ? relocation.Destination : plan.ChargingStation
            });
        }
        else if (charge > 0f)
        {
            int fallback = DroneFlightPlanningUtility.PlanReturn(position, flights, charge, maximum,
                config.EnergyPerDistance, out var returnPlan);
            if (fallback >= 0)
                manager.SetComponentData(drone, new DroneMovementDecision
                {
                    Ready = true, ClearFailure = true, Purpose = DroneFlightPurposeEnum.StationRelocation,
                    Target = returnPlan.NextPosition,
                    Station = returnPlan.ChargingStation == Entity.Null ? flights[fallback].Entity : returnPlan.ChargingStation
                });
        }
        else
        {
            int emergency = DroneFlightPlanningUtility.FindReturnStation(position, flights);
            if (emergency >= 0)
                manager.SetComponentData(drone, new DroneMovementDecision
                {
                    Ready = true, ClearFailure = true, Purpose = DroneFlightPurposeEnum.EmergencyReturn,
                    Target = flights[emergency].Position, Station = flights[emergency].Entity
                });
        }
    }

    private static void Submit(EntityManager manager, Entity drone, Entity station, DroneStationOperation operation,
        ref DroneStationRelocationIntentElement intent, NativeList<DroneStationRelocationIntentElement> intents)
    {
        Entity request = DroneStationRequestUtility.Submit(manager, drone, station, operation);
        manager.AddComponent<DroneStationRelocationRequest>(request);
        intent.Request = request;
        intents.Add(intent);
    }
}
