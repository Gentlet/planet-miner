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
    private EntityQuery _sources, _stations, _rosters, _results, _assignments;
    private Entity _candidates;

    public void OnCreate(ref SystemState state)
    {
        _candidates = DroneSchedulingUtility.GetOrCreateCandidates(state.EntityManager);
        _sources = state.GetEntityQuery(typeof(PreparingStationDemolition), typeof(StorageSlotElement), typeof(DockedDroneElement));
        _stations = state.GetEntityQuery(typeof(DroneStation), typeof(DroneStationStorage), typeof(Storage),
            typeof(DroneStationNetworkDecision), typeof(GridPosition), typeof(StorageSlotElement), typeof(DockedDroneElement),
            typeof(DroneStationReservedSlotElement));
        _rosters = state.GetEntityQuery(new EntityQueryDesc
        {
            All = new[] { ComponentType.ReadOnly<DroneStationRelocationMemberElement>() },
            Options = EntityQueryOptions.IncludeDisabledEntities
        });
        _assignments = state.GetEntityQuery(typeof(DroneTaskAssignment));
        state.RequireForUpdate<DroneBatteryConfig>();
        state.RequireForUpdate<DroneMovementConfig>();
    }

    public void OnUpdate(ref SystemState state)
    {
        state.CompleteDependency();
        var manager = state.EntityManager;
        if (_sources.IsEmptyIgnoreFilter && _rosters.IsEmptyIgnoreFilter)
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
            if (!manager.HasBuffer<DroneStationRelocationMemberElement>(preparation.Task)) continue;
            using var members = new NativeList<DroneStationRelocationCandidateElement>(Allocator.Temp);
            var inventory = manager.GetBuffer<StorageSlotElement>(source, true);
            for (int slot = 0; slot < inventory.Length; slot++)
            {
                var entry = inventory[slot];
                if (entry.ItemType != ItemTypeEnum.Drone) continue;
                for (int copy = 0; copy < entry.Quantity; copy++)
                    members.Add(new DroneStationRelocationCandidateElement
                    {
                        Source = source, Task = preparation.Task, SourceSlotIndex = slot, MemberIndex = members.Length
                    });
            }
            bool ready = true;
            foreach (var row in manager.GetBuffer<DockedDroneElement>(source, true))
            {
                Entity drone = row.Drone;
                if (!DroneSchedulingUtility.IsActiveEntity(manager, drone) || manager.HasComponent<DroneStationRelocation>(drone) ||
                    manager.GetComponentData<DroneWorkerAssignment>(drone).Assignment != Entity.Null ||
                    manager.GetComponentData<DroneCargoState>(drone).Quantity != 0 ||
                    manager.GetComponentData<DroneCargoState>(drone).Origin != DroneCargoOriginEnum.None ||
                    manager.GetComponentData<DroneBatteryState>(drone).CurrentCharge != maximum)
                {
                    ready = false;
                    break;
                }
                members.Add(new DroneStationRelocationCandidateElement
                {
                    Source = source, Task = preparation.Task, Drone = drone,
                    SourceSlotIndex = -1, MemberIndex = members.Length
                });
            }
            if (!ready) continue;
            if (members.IsEmpty) proposals.Add(new DroneStationRelocationCandidateElement
                { Source = source, Task = preparation.Task, MemberIndex = -1 });
            else proposals.AddRange(members.AsArray());
        }
        using var tasks = _rosters.ToEntityArray(Allocator.Temp);
        foreach (Entity task in tasks)
        {
            // 요청 접수·결과 소비가 구조를 바꾸므로 조회한 버퍼를 반복 중에 보관하지 않는다.
            using var roster = manager.GetBuffer<DroneStationRelocationMemberElement>(task, true).ToNativeArray(Allocator.Temp);
            for (int index = 0; index < roster.Length; index++)
            {
                var relocation = roster[index];
                if (relocation.Finished) continue;
                bool stopUnstarted = DroneStationRelocationUtility.ShouldStopUnstartedRelocation(manager, relocation, _candidates);
                bool destinationValid = DroneSchedulingUtility.IsStorage(manager, relocation.Destination) &&
                    DroneStationRelocationUtility.ReservedSlot(manager, relocation.Destination, task, index) == relocation.SlotIndex;
                bool settledResult = relocation.Request != Entity.Null && manager.HasComponent<DroneStationResult>(relocation.Request);
                if (!destinationValid && !settledResult && !(stopUnstarted && !relocation.DepartureCommitted))
                    proposals.Add(new DroneStationRelocationCandidateElement
                    {
                        Source = relocation.Source, Task = task, Drone = relocation.Drone, Replan = true,
                        MemberIndex = index, SourceSlotIndex = relocation.SourceSlotIndex
                    });
                PrepareActor(manager, index, relocation, stopUnstarted, destinationValid, maximum, config, flights, intents);
            }
        }
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
            return order != 0 ? order : a.MemberIndex.CompareTo(b.MemberIndex);
        }
    }

    private static void PrepareActor(EntityManager manager, int memberIndex, in DroneStationRelocationMemberElement relocation,
        bool stopUnstarted, bool destinationValid, float maximum, in DroneMovementConfig config,
        NativeList<DroneFlightStation> allFlights, NativeList<DroneStationRelocationIntentElement> intents)
    {
        Entity drone = relocation.Drone;
        bool hasDocked = manager.HasComponent<DroneDocked>(drone);
        Entity dockedStation = hasDocked ? manager.GetComponentData<DroneDocked>(drone).Station : Entity.Null;
        Hold(manager, drone, dockedStation);
        var intent = new DroneStationRelocationIntentElement
        {
            Task = relocation.Task, MemberIndex = memberIndex, Drone = drone, Stage = relocation.Stage
        };
        if (relocation.Request != Entity.Null)
        {
            if (!DroneStationRequestUtility.TryConsumeResult(manager, relocation.Request, out var result)) return;
            if (result.Status == DroneStationRequestStatus.Applied)
            {
                if ((result.Operation is DroneStationOperation.Activate or DroneStationOperation.ActivateStored))
                {
                    intent.Drone = result.Drone;
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
            bool activatedStock = result.Status == DroneStationRequestStatus.Applied &&
                result.Operation == DroneStationOperation.ActivateStored;
            if (stopUnstarted && !relocation.DepartureCommitted && !intent.Completed && !activatedStock)
            {
                intent.Finish = true;
                intent.Completed = false;
            }
            intents.Add(intent);
            return;
        }
        if (stopUnstarted && !relocation.DepartureCommitted)
        {
            intent.Finish = true;
            intents.Add(intent);
            return;
        }
        if (relocation.Stage == DroneStationRelocationStageEnum.WaitingForActivation && !destinationValid)
        {
            return;
        }
        if (relocation.Stage == DroneStationRelocationStageEnum.WaitingForActivation && drone == Entity.Null)
        {
            Entity request = DroneStationRequestUtility.Submit(manager, Entity.Null, relocation.Source,
                DroneStationOperation.ActivateStored, relocation.SourceSlotIndex, relocation.Task, memberIndex);
            manager.AddComponent<DroneStationRelocationRequest>(request);
            intent.Request = request;
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
            if (!hasDocked)
            {
                intent.Stage = DroneStationRelocationStageEnum.Travelling;
                intents.Add(intent);
                return;
            }
            if (charge < maximum) return;
            Submit(manager, drone, dockedStation, destinationValid && dockedStation == relocation.Destination ? DroneStationOperation.Store : DroneStationOperation.Activate,
                ref intent, intents);
            return;
        }
        float2 position = manager.GetComponentData<LocalTransform>(drone).Position.xy;
        Entity arrived = manager.GetComponentData<DroneMovementState>(drone).ChargingStation;
        if (arrived != Entity.Null && DroneTargetPointUtility.TryGetTargetPoint(manager, arrived, out var arrivedPoint) &&
            math.all(position == arrivedPoint) && (arrived == relocation.Destination || charge < maximum))
        {
            Hold(manager, drone, arrived);
            Submit(manager, drone, arrived, DroneStationOperation.Dock, ref intent, intents);
            return;
        }

        using var flights = new NativeList<DroneFlightStation>(Allocator.Temp);
        foreach (var row in allFlights)
        {
            if (charge >= maximum && math.all(row.Position == position)) continue;
            var station = row;
            if (destinationValid && station.Entity == relocation.Destination) station.CanDock = true;
            if (station.Entity == relocation.Source)
            {
                var sourceStorage = StationStorageUtility.StorageReadContext.Create(manager, station.Entity);
                int slotCount = sourceStorage.SlotCount;
                for (int slot = 0; slot < slotCount; slot++)
                {
                    bool occupied = sourceStorage.IsDockOccupied(slot) || sourceStorage.IsReserved(slot);
                    occupied |= sourceStorage.StoredItems[slot].Quantity > 0;
                    if (!occupied)
                    {
                        station.CanDock = true;
                        break;
                    }
                }
            }
            flights.Add(station);
        }
        if (destinationValid && DroneTargetPointUtility.TryGetTargetPoint(manager, relocation.Destination, out var goal))
        {
            var plan = DroneFlightPlanningUtility.Plan(position, goal, charge, maximum, config.EnergyPerDistance, flights);
            if (plan.Status == DroneRouteEvaluationStatusEnum.Reachable)
            {
                manager.SetComponentData(drone, new DroneMovementDecision
                {
                    Ready = true, ClearFailure = true, Purpose = DroneFlightPurposeEnum.StationRelocation,
                    Target = plan.NextPosition, Station = plan.ChargingStation == Entity.Null ? relocation.Destination : plan.ChargingStation
                });
                return;
            }
        }
        // 목적지 예약이 없는 재계획 중에도 방전된 수행자는 비상 귀환해 충전할 수 있다.
        if (charge > 0f)
        {
            if (!destinationValid)
            {
                return;
            }
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
        Entity request = DroneStationRequestUtility.Submit(manager, drone, station, operation,
            relocationTask: intent.Task, relocationMemberIndex: intent.MemberIndex);
        manager.AddComponent<DroneStationRelocationRequest>(request);
        intent.Request = request;
        intents.Add(intent);
    }
}
