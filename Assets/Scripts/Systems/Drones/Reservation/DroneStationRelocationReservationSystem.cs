using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>전원분 도착 슬롯이 함께 확보될 때만 배치를 시작한다. 예약은 실물/정박과 별도 원본이다.</summary>
[UpdateInGroup(typeof(DroneReservationGroup))]
[UpdateBefore(typeof(DroneTaskReservationSystem))]
[UpdateBefore(typeof(DroneStationLifecycleReservationSystem))]
public partial struct DroneStationRelocationReservationSystem : ISystem
{
    private Entity _candidates;
    private EntityQuery _stations;

    public void OnCreate(ref SystemState state)
    {
        _candidates = DroneSchedulingUtility.GetOrCreateCandidates(state.EntityManager);
        _stations = state.GetEntityQuery(typeof(DroneStation), typeof(DroneStationStorage), typeof(Storage),
            typeof(GridPosition), typeof(DroneStationNetworkDecision), typeof(StoredItemElement), typeof(DockedDroneElement),
            typeof(DroneStationReservedSlotElement), typeof(BuildingFootprint), typeof(Direction));
        state.RequireForUpdate<DroneBatteryConfig>();
        state.RequireForUpdate<DroneMovementConfig>();
    }

    public void OnUpdate(ref SystemState state)
    {
        if (state.EntityManager.GetBuffer<DroneStationRelocationCandidateElement>(_candidates, true).IsEmpty) return;
        var stations = _stations.ToEntityArray(Allocator.TempJob);
        var end = state.World.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>();
        var handle = new ReserveRelocationsJob
        {
            CandidateOwner = _candidates, Stations = stations,
            Candidates = state.GetBufferLookup<DroneStationRelocationCandidateElement>(true),
            Preparations = state.GetComponentLookup<PreparingStationDemolition>(),
            Relocations = state.GetComponentLookup<DroneStationRelocation>(),
            Reserved = state.GetBufferLookup<DroneStationReservedSlotElement>(),
            Positions = state.GetComponentLookup<GridPosition>(true),
            Footprints = state.GetComponentLookup<BuildingFootprint>(true), Directions = state.GetComponentLookup<Direction>(true),
            Networks = state.GetComponentLookup<DroneStationNetworkDecision>(true), Storages = state.GetComponentLookup<Storage>(true),
            Dedicated = state.GetComponentLookup<DroneStationStorage>(true), Stamps = state.GetComponentLookup<PlacementStamp>(true),
            Pending = state.GetComponentLookup<PendingBuildingDemolition>(true),
            Items = state.GetBufferLookup<StoredItemElement>(true), Docks = state.GetBufferLookup<DockedDroneElement>(true),
            Workers = state.GetComponentLookup<DroneWorker>(true), Transforms = state.GetComponentLookup<LocalTransform>(true),
            Maximum = SystemAPI.GetSingleton<DroneBatteryConfig>().MaximumCharge,
            Rate = SystemAPI.GetSingleton<DroneMovementConfig>().EnergyPerDistance, ECB = end.CreateCommandBuffer()
        }.Schedule(state.Dependency);
        end.AddJobHandleForProducer(handle);
        state.Dependency = stations.Dispose(handle);
    }

    [BurstCompile]
    private struct ReserveRelocationsJob : Unity.Jobs.IJob
    {
        private struct SlotReservationPlan
        {
            public Entity Drone;
            public Entity Destination;
            public int Slot;
        }
        private struct AvailableSlot
        {
            public Entity Station;
            public int StationIndex;
            public int Slot;
            public bool SameNetwork;
        }
        private struct StationRouteOption
        {
            public int FirstSlotIndex;
            public float Distance;
        }
        public Entity CandidateOwner;
        [ReadOnly] public NativeArray<Entity> Stations;
        [ReadOnly] public BufferLookup<DroneStationRelocationCandidateElement> Candidates;
        public ComponentLookup<PreparingStationDemolition> Preparations;
        public ComponentLookup<DroneStationRelocation> Relocations;
        public BufferLookup<DroneStationReservedSlotElement> Reserved;
        [ReadOnly] public ComponentLookup<GridPosition> Positions;
        [ReadOnly] public ComponentLookup<BuildingFootprint> Footprints;
        [ReadOnly] public ComponentLookup<Direction> Directions;
        [ReadOnly] public ComponentLookup<DroneStationNetworkDecision> Networks;
        [ReadOnly] public ComponentLookup<Storage> Storages;
        [ReadOnly] public ComponentLookup<DroneStationStorage> Dedicated;
        [ReadOnly] public ComponentLookup<PlacementStamp> Stamps;
        [ReadOnly] public ComponentLookup<PendingBuildingDemolition> Pending;
        [ReadOnly] public BufferLookup<StoredItemElement> Items;
        [ReadOnly] public BufferLookup<DockedDroneElement> Docks;
        [ReadOnly] public ComponentLookup<DroneWorker> Workers;
        [ReadOnly] public ComponentLookup<LocalTransform> Transforms;
        public float Maximum, Rate;
        public EntityCommandBuffer ECB;

        public void Execute()
        {
            var candidates = Candidates[CandidateOwner];
            for (int first = 0; first < candidates.Length; first++)
            {
                var seed = candidates[first];
                // 초기 배치는 정거장별 하나다. 목적지 소실의 재계획은 같은 원본 정거장의 미완료 개체를 묶는다.
                bool visited = false;
                for (int earlier = 0; earlier < first; earlier++)
                    visited |= candidates[earlier].Source == seed.Source && candidates[earlier].Task == seed.Task &&
                        candidates[earlier].Replan == seed.Replan;
                if (visited) continue;
                using var members = new NativeList<DroneStationRelocationCandidateElement>(Allocator.Temp);
                for (int i = first; i < candidates.Length; i++)
                    if (candidates[i].Source == seed.Source && candidates[i].Task == seed.Task && candidates[i].Replan == seed.Replan)
                        members.Add(candidates[i]);
                bool replan = seed.Replan;
                if (!replan && (!Preparations.HasComponent(seed.Source) || Preparations[seed.Source].Started)) continue;
                foreach (var member in members)
                {
                    if (!member.Replan || !Relocations.HasComponent(member.Drone)) continue;
                    var old = Relocations[member.Drone];
                    RemoveReservation(old.Destination, member.Drone);
                }
                using var plans = new NativeList<SlotReservationPlan>(Allocator.Temp);
                using var flights = new NativeList<DroneFlightStation>(Allocator.Temp);
                bool complete = TryBuildCompleteActorSlotPlan(members, flights, plans);
                if (!complete) continue;
                foreach (var plan in plans)
                {
                    Reserved[plan.Destination].Add(new DroneStationReservedSlotElement { Drone = plan.Drone, SlotIndex = plan.Slot });
                    if (Relocations.HasComponent(plan.Drone))
                    {
                        var relocation = Relocations[plan.Drone];
                        relocation.Destination = plan.Destination; relocation.SlotIndex = plan.Slot;
                        Relocations[plan.Drone] = relocation;
                    }
                    else
                    {
                        ECB.AddComponent(plan.Drone, new DroneStationRelocation
                        {
                            Source = seed.Source, Task = seed.Task, Destination = plan.Destination, SlotIndex = plan.Slot,
                            Stage = DroneStationRelocationStageEnum.WaitingForActivation
                        });
                    }
                }
                if (!replan)
                {
                    var preparation = Preparations[seed.Source];
                    preparation.Started = true; preparation.TotalCount = plans.Length; preparation.CompletedCount = 0;
                    Preparations[seed.Source] = preparation;
                }
            }
        }

        private bool TryBuildCompleteActorSlotPlan(NativeList<DroneStationRelocationCandidateElement> members,
            NativeList<DroneFlightStation> flights, NativeList<SlotReservationPlan> plans)
        {
            using var actors = new NativeList<DroneStationRelocationCandidateElement>(Allocator.Temp);
            foreach (var member in members) if (member.Drone != Entity.Null) actors.Add(member);
            if (actors.IsEmpty) return true;
            using var sourceChargingSlotsAfterDeparture = new NativeHashSet<int>(16, Allocator.Temp);
            CollectSourceChargingSlotsAfterDeparture(actors, sourceChargingSlotsAfterDeparture);
            using var slots = new NativeList<AvailableSlot>(Allocator.Temp);
            for (int stationIndex = 0; stationIndex < Stations.Length; stationIndex++)
            {
                Entity station = Stations[stationIndex];
                if (Preparations.HasComponent(station) || Pending.HasComponent(station)) continue;
                for (int index = 0; index < Storages[station].SlotCount; index++)
                {
                    int slot = StationStorageUtility.SearchSlot(index, Storages[station].SlotCount,
                        Dedicated[station].DedicatedSlotCount, ItemTypeEnum.Drone);
                    bool occupied = DroneStationRelocationUtility.IsReserved(Reserved, station, slot);
                    foreach (var item in Items[station]) occupied |= item.SlotIndex == slot;
                    foreach (var dock in Docks[station]) occupied |= dock.SlotIndex == slot;
                    if (occupied) continue;
                    slots.Add(new AvailableSlot
                    {
                        Station = station, StationIndex = stationIndex, Slot = slot,
                        SameNetwork = Networks.HasComponent(actors[0].Source) &&
                            Networks[station].RepresentativeStation == Networks[actors[0].Source].RepresentativeStation
                    });
                }
            }
            if (slots.Length < actors.Length) return false;
            using var ownerStorage = new NativeArray<int>(slots.Length, Allocator.Temp);
            using var actorSlotStorage = new NativeArray<int>(actors.Length, Allocator.Temp);
            using var parentStorage = new NativeArray<int>(slots.Length, Allocator.Temp);
            using var seenSlotStorage = new NativeArray<byte>(slots.Length, Allocator.Temp);
            using var seenActorStorage = new NativeArray<byte>(actors.Length, Allocator.Temp);
            // 해제 소유자는 using 변수이며 쓰기 뷰는 같은 메모리를 참조한다.
            var slotOwners = ownerStorage;
            var actorSlots = actorSlotStorage;
            var parentActors = parentStorage;
            var visitedSlots = seenSlotStorage;
            var visitedActors = seenActorStorage;
            using var queue = new NativeList<int>(Allocator.Temp);
            using var distances = new NativeHashMap<int2, float>(16, Allocator.Temp);
            for (int i = 0; i < slotOwners.Length; i++) slotOwners[i] = -1;
            for (int i = 0; i < actorSlots.Length; i++) actorSlots[i] = -1;
            // 가까운 슬롯의 개별 선택이 나머지를 막으면 기존 매칭을 옮기는 증가 경로를 찾는다.
            for (int start = 0; start < actors.Length; start++)
            {
                for (int i = 0; i < visitedSlots.Length; i++) visitedSlots[i] = 0;
                for (int i = 0; i < visitedActors.Length; i++) visitedActors[i] = 0;
                queue.Clear(); queue.Add(start); visitedActors[start] = 1;
                bool matched = false;
                for (int queueIndex = 0; queueIndex < queue.Length && !matched; queueIndex++)
                {
                    int actor = queue[queueIndex];
                    for (int step = 0; step < slots.Length; step++)
                    {
                        int best = -1;
                        float bestDistance = float.MaxValue;
                        for (int slot = 0; slot < slots.Length; slot++)
                        {
                            if (visitedSlots[slot] != 0) continue;
                            var key = new int2(actor, slots[slot].StationIndex);
                            if (!distances.TryGetValue(key, out float distance))
                            {
                                BuildChargingStationsForCohort(flights, actors[actor].Source, plans, sourceChargingSlotsAfterDeparture);
                                var path = DroneFlightPlanningUtility.Plan(GetActorRouteOriginPosition(actors[actor]), GetStationCenterPosition(slots[slot].Station),
                                    Maximum, Maximum, Rate, flights);
                                distance = path.Status == DroneRouteEvaluationStatusEnum.Reachable ? path.Distance : float.MaxValue;
                                distances.Add(key, distance);
                            }
                            if (distance == float.MaxValue) continue;
                            if (best < 0 || IsPreferredDestination(slots[slot], distance, slots[best], bestDistance))
                            {
                                best = slot;
                                bestDistance = distance;
                            }
                        }
                        if (best < 0) break;
                        visitedSlots[best] = 1; parentActors[best] = actor;
                        if (slotOwners[best] < 0)
                        {
                            for (int slot = best; slot >= 0;)
                            {
                                int assigned = parentActors[slot];
                                int previous = actorSlots[assigned];
                                slotOwners[slot] = assigned; actorSlots[assigned] = slot; slot = previous;
                            }
                            matched = true;
                            break;
                        }
                        if (visitedActors[slotOwners[best]] == 0)
                        {
                            visitedActors[slotOwners[best]] = 1;
                            queue.Add(slotOwners[best]);
                        }
                    }
                }
                if (!matched) return false;
            }
            for (int i = 0; i < actors.Length; i++)
            {
                var slot = slots[actorSlots[i]];
                plans.Add(new SlotReservationPlan { Drone = actors[i].Drone, Destination = slot.Station, Slot = slot.Slot });
            }
            if (AreAssignedRoutesReachable(actors, plans, flights, sourceChargingSlotsAfterDeparture)) return true;
            return TryBuildRoutePreservingAlternative(actors, slots, distances, flights, plans, sourceChargingSlotsAfterDeparture);
        }

        private bool IsPreferredDestination(in AvailableSlot candidate, float distance,
            in AvailableSlot current, float currentDistance)
        {
            if (candidate.SameNetwork != current.SameNetwork) return candidate.SameNetwork;
            if (distance != currentDistance) return distance < currentDistance;
            return IsStationEarlierForTieBreak(candidate.Station, current.Station);
        }

        private bool AreAssignedRoutesReachable(NativeList<DroneStationRelocationCandidateElement> actors,
            NativeList<SlotReservationPlan> plans, NativeList<DroneFlightStation> flights,
            NativeHashSet<int> sourceChargingSlotsAfterDeparture)
        {
            for (int i = 0; i < plans.Length; i++)
            {
                BuildChargingStationsForCohort(flights, actors[i].Source, plans, sourceChargingSlotsAfterDeparture);
                if (DroneFlightPlanningUtility.Plan(GetActorRouteOriginPosition(actors[i]), GetStationCenterPosition(plans[i].Destination),
                    Maximum, Maximum, Rate, flights).Status != DroneRouteEvaluationStatusEnum.Reachable) return false;
            }
            return true;
        }

        private bool TryBuildRoutePreservingAlternative(NativeList<DroneStationRelocationCandidateElement> actors,
            NativeList<AvailableSlot> slots, NativeHashMap<int2, float> distances,
            NativeList<DroneFlightStation> flights, NativeList<SlotReservationPlan> plans,
            NativeHashSet<int> sourceChargingSlotsAfterDeparture)
        {
            plans.Clear();
            using var optionStorage = new NativeList<StationRouteOption>(Allocator.Temp);
            using var rangeStorage = new NativeArray<int>(actors.Length + 1, Allocator.Temp);
            using var nextStorage = new NativeArray<int>(actors.Length, Allocator.Temp);
            var options = optionStorage;
            var ranges = rangeStorage;
            var nextOptions = nextStorage;
            for (int actor = 0; actor < actors.Length; actor++)
            {
                ranges[actor] = options.Length;
                BuildChargingStationsForCohort(flights, actors[actor].Source, plans, sourceChargingSlotsAfterDeparture);
                for (int firstSlotIndex = 0; firstSlotIndex < slots.Length;)
                {
                    var key = new int2(actor, slots[firstSlotIndex].StationIndex);
                    if (!distances.TryGetValue(key, out float distance))
                    {
                        var path = DroneFlightPlanningUtility.Plan(GetActorRouteOriginPosition(actors[actor]),
                            GetStationCenterPosition(slots[firstSlotIndex].Station), Maximum, Maximum, Rate, flights);
                        distance = path.Status == DroneRouteEvaluationStatusEnum.Reachable ? path.Distance : float.MaxValue;
                        distances.Add(key, distance);
                    }
                    if (distance != float.MaxValue)
                    {
                        var option = new StationRouteOption { FirstSlotIndex = firstSlotIndex, Distance = distance };
                        int insertion = options.Length;
                        options.Add(option);
                        while (insertion > ranges[actor] && IsPreferredDestination(slots[firstSlotIndex], distance,
                            slots[options[insertion - 1].FirstSlotIndex], options[insertion - 1].Distance))
                        {
                            options[insertion] = options[insertion - 1];
                            insertion--;
                        }
                        options[insertion] = option;
                    }
                    Entity station = slots[firstSlotIndex].Station;
                    do
                    {
                        firstSlotIndex++;
                    } while (firstSlotIndex < slots.Length && slots[firstSlotIndex].Station == station);
                }
            }
            ranges[actors.Length] = options.Length;
            // 충전 경로를 막은 매칭만 대체한다. 같은 정거장 안의 빈 슬롯은 경로상 동등하므로 순열을 반복하지 않는다.
            int currentActor = 0;
            while (currentActor >= 0)
            {
                if (currentActor == actors.Length) return true;
                int optionIndex = ranges[currentActor] + nextOptions[currentActor];
                if (optionIndex >= ranges[currentActor + 1])
                {
                    nextOptions[currentActor] = 0;
                    currentActor--;
                    if (currentActor >= 0) plans.RemoveAt(plans.Length - 1);
                    continue;
                }
                nextOptions[currentActor]++;
                int firstSlotIndex = options[optionIndex].FirstSlotIndex;
                int availableSlotIndex = FindUnassignedStationSlotIndex(slots, firstSlotIndex, plans);
                if (availableSlotIndex < 0) continue;
                var slot = slots[availableSlotIndex];
                plans.Add(new SlotReservationPlan
                {
                    Drone = actors[currentActor].Drone, Destination = slot.Station, Slot = slot.Slot
                });
                // 추가 예약은 경유 공간을 줄이기만 하므로 부분 계획에서 막힌 가지는 확장하지 않는다.
                if (!AreAssignedRoutesReachable(actors, plans, flights, sourceChargingSlotsAfterDeparture))
                {
                    plans.RemoveAt(plans.Length - 1);
                    continue;
                }
                currentActor++;
                if (currentActor < actors.Length) nextOptions[currentActor] = 0;
            }
            plans.Clear();
            return false;
        }

        private static int FindUnassignedStationSlotIndex(NativeList<AvailableSlot> slots, int firstSlotIndex,
            NativeList<SlotReservationPlan> plans)
        {
            Entity station = slots[firstSlotIndex].Station;
            for (int i = firstSlotIndex; i < slots.Length && slots[i].Station == station; i++)
            {
                bool assigned = false;
                foreach (var plan in plans) assigned |= plan.Destination == station && plan.Slot == slots[i].Slot;
                if (!assigned) return i;
            }
            return -1;
        }

        private float2 GetActorRouteOriginPosition(in DroneStationRelocationCandidateElement actor) => Workers.HasComponent(actor.Drone)
            ? Transforms[actor.Drone].Position.xy : (float2)Positions[actor.Source].Value;

        private float2 GetStationCenterPosition(Entity station) => (float2)Positions[station].Value +
            (float2)Footprints[station].GetEffectiveSize(Directions[station].dir) * 0.5f;

        private bool IsStationEarlierForTieBreak(Entity left, Entity right)
        {
            if (Stamps.HasComponent(left) && Stamps.HasComponent(right))
            {
                int order = PlacementStamp.Compare(true, Stamps[left], Positions[left].Value,
                    true, Stamps[right], Positions[right].Value);
                return order < 0;
            }
            return left.Index < right.Index || (left.Index == right.Index && left.Version < right.Version);
        }

        /// <summary>출고 자격이 준비된 초기 코호트의 자체 점유만 충전 경로에서 제외한다. 재계획은 실제 빈 슬롯을 사용한다.</summary>
        private void CollectSourceChargingSlotsAfterDeparture(NativeList<DroneStationRelocationCandidateElement> actors,
            NativeHashSet<int> slotsAfterDeparture)
        {
            Entity source = actors[0].Source;
            if (!Storages.HasComponent(source)) return;
            using var departingActors = new NativeHashSet<Entity>(actors.Length, Allocator.Temp);
            if (!actors[0].Replan)
                foreach (var actor in actors) departingActors.Add(actor.Drone);
            for (int slot = 0; slot < Storages[source].SlotCount; slot++)
            {
                bool occupied = DroneStationRelocationUtility.IsReserved(Reserved, source, slot);
                foreach (var item in Items[source])
                    occupied |= item.SlotIndex == slot && !departingActors.Contains(item.ItemEntity);
                foreach (var dock in Docks[source])
                    occupied |= dock.SlotIndex == slot && !departingActors.Contains(dock.Drone);
                if (!occupied) slotsAfterDeparture.Add(slot);
            }
        }

        private int FindChargingSlotAfterCohortDeparture(Entity station, Entity source,
            NativeList<SlotReservationPlan> plans, NativeHashSet<int> sourceChargingSlotsAfterDeparture)
        {
            int total = Storages[station].SlotCount;
            for (int index = 0; index < total; index++)
            {
                int slot = StationStorageUtility.SearchSlot(index, total, Dedicated[station].DedicatedSlotCount, ItemTypeEnum.Drone);
                bool occupied;
                if (station == source)
                {
                    occupied = !sourceChargingSlotsAfterDeparture.Contains(slot);
                }
                else
                {
                    occupied = DroneStationRelocationUtility.IsReserved(Reserved, station, slot);
                    foreach (var item in Items[station]) occupied |= item.SlotIndex == slot;
                    foreach (var dock in Docks[station]) occupied |= dock.SlotIndex == slot;
                }
                foreach (var plan in plans) occupied |= plan.Destination == station && plan.Slot == slot;
                if (!occupied) return slot;
            }
            return -1;
        }

        private void BuildChargingStationsForCohort(NativeList<DroneFlightStation> flights, Entity source,
            NativeList<SlotReservationPlan> plans, NativeHashSet<int> sourceChargingSlotsAfterDeparture)
        {
            flights.Clear();
            foreach (Entity station in Stations)
            {
                if (Pending.HasComponent(station)) continue;
                flights.Add(new DroneFlightStation
                {
                    Entity = station, Position = GetStationCenterPosition(station),
                    CanDock = (!Preparations.HasComponent(station) || station == source) &&
                        FindChargingSlotAfterCohortDeparture(station, source, plans, sourceChargingSlotsAfterDeparture) >= 0,
                    Network = Networks[station].RepresentativeStation, HasStamp = Stamps.HasComponent(station),
                    Stamp = Stamps.HasComponent(station) ? Stamps[station] : default
                });
            }
        }

        private void RemoveReservation(Entity station, Entity drone)
        {
            if (!Reserved.HasBuffer(station)) return;
            var rows = Reserved[station];
            for (int i = rows.Length - 1; i >= 0; i--)
                if (rows[i].Drone == drone) rows.RemoveAt(i);
        }
    }
}
