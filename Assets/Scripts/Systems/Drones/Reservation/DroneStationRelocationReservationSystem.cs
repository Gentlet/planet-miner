using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>전원분 도착 수량이 함께 확보될 때만 배치를 시작한다. 예약은 실물/정박과 별도 원본이다.</summary>
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
            typeof(GridPosition), typeof(DroneStationNetworkDecision), typeof(StorageSlotElement), typeof(DockedDroneElement),
            typeof(DroneStationReservedSlotElement), typeof(BuildingFootprint), typeof(Direction));
        state.RequireForUpdate<DroneBatteryConfig>();
        state.RequireForUpdate<DroneMovementConfig>();
        state.RequireForUpdate<ItemRegistry>();
    }

    public void OnUpdate(ref SystemState state)
    {
        if (state.EntityManager.GetBuffer<DroneStationRelocationCandidateElement>(_candidates, true).IsEmpty) return;
        var stations = _stations.ToEntityArray(Allocator.TempJob);
        var end = state.World.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>();
        var handle = new ReserveRelocationsJob
        {
            CandidateOwner = _candidates, Stations = stations,
            Candidates = SystemAPI.GetBufferLookup<DroneStationRelocationCandidateElement>(true),
            Preparations = SystemAPI.GetComponentLookup<PreparingStationDemolition>(),
            Members = SystemAPI.GetBufferLookup<DroneStationRelocationMemberElement>(),
            Reserved = SystemAPI.GetBufferLookup<DroneStationReservedSlotElement>(),
            Positions = SystemAPI.GetComponentLookup<GridPosition>(true),
            Footprints = SystemAPI.GetComponentLookup<BuildingFootprint>(true), Directions = SystemAPI.GetComponentLookup<Direction>(true),
            FuelSlots = SystemAPI.GetComponentLookup<FuelStorageSlots>(true),
            Networks = SystemAPI.GetComponentLookup<DroneStationNetworkDecision>(true), Storages = SystemAPI.GetComponentLookup<Storage>(true),
            Dedicated = SystemAPI.GetComponentLookup<DroneStationStorage>(true), Stamps = SystemAPI.GetComponentLookup<PlacementStamp>(true),
            Pending = SystemAPI.GetComponentLookup<PendingBuildingDemolition>(true),
            Items = SystemAPI.GetBufferLookup<StorageSlotElement>(true),
            Batteries = SystemAPI.GetComponentLookup<DroneBatteryState>(true),
            Workers = SystemAPI.GetComponentLookup<DroneWorker>(true), Transforms = SystemAPI.GetComponentLookup<LocalTransform>(true),
            Maximum = SystemAPI.GetSingleton<DroneBatteryConfig>().MaximumCharge,
            MaximumStack = ItemRegistry.GetMaxStack(SystemAPI.GetSingletonBuffer<ItemConfigElement>(true), ItemTypeEnum.Drone),
            Rate = SystemAPI.GetSingleton<DroneMovementConfig>().EnergyPerDistance, ECB = end.CreateCommandBuffer()
        }.Schedule(state.Dependency);
        end.AddJobHandleForProducer(handle);
        state.Dependency = stations.Dispose(handle);
    }

    [BurstCompile]
    private struct ReserveRelocationsJob : Unity.Jobs.IJob
    {
        public Entity CandidateOwner;

        private struct SlotReservationPlan
        {
            public DroneStationRelocationCandidateElement Member;
            public Entity Destination;
            public int Slot;
        }
        private struct AvailableStation
        {
            public Entity Station;
            public int Capacity;
            public int NextSlot;
            public bool SameNetwork;
        }

        private struct AvailableSlot
        {
            public int Index;
            public int Quantity;
        }
        [ReadOnly] public NativeArray<Entity> Stations;
        [ReadOnly] public BufferLookup<DroneStationRelocationCandidateElement> Candidates;
        public ComponentLookup<PreparingStationDemolition> Preparations;
        public BufferLookup<DroneStationRelocationMemberElement> Members;
        public BufferLookup<DroneStationReservedSlotElement> Reserved;
        [ReadOnly] public ComponentLookup<GridPosition> Positions;
        [ReadOnly] public ComponentLookup<BuildingFootprint> Footprints;
        [ReadOnly] public ComponentLookup<Direction> Directions;
        [ReadOnly] public ComponentLookup<DroneStationNetworkDecision> Networks;
        [ReadOnly] public ComponentLookup<Storage> Storages;
        [ReadOnly] public ComponentLookup<FuelStorageSlots> FuelSlots;
        [ReadOnly] public ComponentLookup<DroneStationStorage> Dedicated;
        [ReadOnly] public ComponentLookup<PlacementStamp> Stamps;
        [ReadOnly] public ComponentLookup<PendingBuildingDemolition> Pending;
        [ReadOnly] public BufferLookup<StorageSlotElement> Items;
        [ReadOnly] public ComponentLookup<DroneWorker> Workers;
        [ReadOnly] public ComponentLookup<DroneBatteryState> Batteries;
        [ReadOnly] public ComponentLookup<LocalTransform> Transforms;
        public float Maximum, Rate;
        public int MaximumStack;
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
                PreparingStationDemolition preparation = default;
                if (!replan && (!Preparations.TryGetComponent(seed.Source, out preparation) || preparation.Started))
                {
                    continue;
                }
                if (!Members.TryGetBuffer(seed.Task, out var roster)) continue;
                using var previousReservations = new NativeList<SlotReservationPlan>(Allocator.Temp);
                if (replan)
                {
                    foreach (var candidate in members)
                    {
                        var old = roster[candidate.MemberIndex];
                        if (Reserved.TryGetBuffer(old.Destination, out var reservations) &&
                            DroneStationRelocationUtility.ReservedSlot(reservations, seed.Task, candidate.MemberIndex) == old.SlotIndex)
                        {
                            previousReservations.Add(new SlotReservationPlan
                                { Member = candidate, Destination = old.Destination, Slot = old.SlotIndex });
                            DroneStationRelocationUtility.ReleaseSlots(reservations, seed.Task, candidate.MemberIndex);
                        }
                    }
                }
                using var plans = new NativeList<SlotReservationPlan>(Allocator.Temp);
                using var flights = new NativeList<DroneFlightStation>(Allocator.Temp);
                if (!TryBuildCompleteActorSlotPlan(members, flights, plans))
                {
                    foreach (var previous in previousReservations)
                        Reserved[previous.Destination].Add(new DroneStationReservedSlotElement
                            { Task = seed.Task, MemberIndex = previous.Member.MemberIndex, SlotIndex = previous.Slot });
                    continue;
                }
                if (!replan) roster.Resize(plans.Length, NativeArrayOptions.ClearMemory);
                foreach (var plan in plans)
                {
                    int index = plan.Member.MemberIndex;
                    Reserved[plan.Destination].Add(new DroneStationReservedSlotElement
                    {
                        Task = seed.Task, MemberIndex = index, SlotIndex = plan.Slot
                    });
                    var member = roster[index];
                    if (!replan)
                    {
                        member = new DroneStationRelocationMemberElement
                        {
                            Source = seed.Source, Task = seed.Task, Drone = plan.Member.Drone,
                            SourceSlotIndex = plan.Member.SourceSlotIndex,
                            Stage = DroneStationRelocationStageEnum.WaitingForActivation
                        };
                        if (member.Drone != Entity.Null)
                            ECB.AddComponent(member.Drone, new DroneStationRelocation { Task = seed.Task, MemberIndex = index });
                    }
                    member.Destination = plan.Destination;
                    member.SlotIndex = plan.Slot;
                    roster[index] = member;
                }
                if (!replan)
                {
                    preparation.Started = true; preparation.TotalCount = plans.Length; preparation.CompletedCount = 0;
                    Preparations[seed.Source] = preparation;
                }
            }
        }

        private bool TryBuildCompleteActorSlotPlan(NativeList<DroneStationRelocationCandidateElement> members,
            NativeList<DroneFlightStation> flights, NativeList<SlotReservationPlan> plans)
        {
            using var actors = new NativeList<DroneStationRelocationCandidateElement>(Allocator.Temp);
            foreach (var member in members) if (member.MemberIndex >= 0) actors.Add(member);
            if (actors.IsEmpty) return true;
            using var destinations = new NativeList<AvailableStation>(Stations.Length, Allocator.Temp);
            using var slots = new NativeList<AvailableSlot>(Allocator.Temp);
            if (!ReadAvailableDestinations(actors[0].Source, actors.Length, destinations, slots)) return false;
            // 출고 재고는 앵커에서 완충으로 생성된다. 이미 출고된 멤버는 실제 위치·배터리로 첫 구간을 판단한다.
            using var origins = new NativeList<ActorRouteOrigin>(actors.Length, Allocator.Temp);
            foreach (var actor in actors)
            {
                origins.Add(Workers.HasComponent(actor.Drone)
                    ? new ActorRouteOrigin { Position = Transforms[actor.Drone].Position.xy, Charge = Batteries[actor.Drone].CurrentCharge }
                    : new ActorRouteOrigin { Position = Positions[actor.Source].Value, Charge = Maximum });
            }
            using var geometry = new NativeHashMap<Entity, StationGeometryRead>(Stations.Length, Allocator.Temp);
            using var occupancyStorage = new NativeArray<int>(destinations.Length, Allocator.Temp);
            using var actorDestinationStorage = new NativeArray<int>(actors.Length, Allocator.Temp);
            using var parentStorage = new NativeArray<int>(destinations.Length, Allocator.Temp);
            using var seenActorStorage = new NativeArray<byte>(actors.Length, Allocator.Temp);
            // 해제 소유자는 using 변수이며 쓰기 뷰는 같은 메모리를 참조한다.
            var occupancy = occupancyStorage;
            var actorDestinations = actorDestinationStorage;
            var parentActors = parentStorage;
            var visitedActors = seenActorStorage;
            using var queue = new NativeList<int>(Allocator.Temp);
            using var distances = new NativeHashMap<int2, float>(16, Allocator.Temp);
            BuildChargingStationsForCohort(flights, actors[0].Source, geometry);
            for (int i = 0; i < actorDestinations.Length; i++) actorDestinations[i] = -1;
            // 같은 정거장의 슬롯들은 도달 조건이 같다. 정거장이 가득 차면 배정된 드론의 대체 경로를 탐색한다.
            for (int start = 0; start < actors.Length; start++)
            {
                for (int i = 0; i < parentActors.Length; i++) parentActors[i] = -1;
                for (int i = 0; i < visitedActors.Length; i++) visitedActors[i] = 0;
                queue.Clear(); queue.Add(start); visitedActors[start] = 1;
                bool matched = false;
                for (int queueIndex = 0; queueIndex < queue.Length && !matched; queueIndex++)
                {
                    int actor = queue[queueIndex];
                    for (int step = 0; step < destinations.Length; step++)
                    {
                        int best = -1;
                        float bestDistance = float.MaxValue;
                        for (int index = 0; index < destinations.Length; index++)
                        {
                            if (parentActors[index] >= 0) continue;
                            var key = new int2(actor, index);
                            if (!distances.TryGetValue(key, out float distance))
                            {
                                var path = DroneFlightPlanningUtility.Plan(origins[actor].Position, geometry[destinations[index].Station].Center,
                                    origins[actor].Charge, Maximum, Rate, flights);
                                distance = path.Status == DroneRouteEvaluationStatusEnum.Reachable ? path.Distance : float.MaxValue;
                                distances.Add(key, distance);
                            }
                            if (distance == float.MaxValue) continue;
                            if (best < 0 || IsPreferredDestination(destinations[index], distance, destinations[best], bestDistance, geometry))
                            {
                                best = index;
                                bestDistance = distance;
                            }
                        }
                        if (best < 0) break;
                        parentActors[best] = actor;
                        if (occupancy[best] < destinations[best].Capacity)
                        {
                            // 경로의 중간 정거장은 한 기씩 교체된다. 빈자리를 제공한 끝 정거장의 점유만 증가한다.
                            occupancy[best]++;
                            for (int destination = best; destination >= 0;)
                            {
                                int assigned = parentActors[destination];
                                int previous = actorDestinations[assigned];
                                actorDestinations[assigned] = destination;
                                destination = previous;
                            }
                            matched = true;
                            break;
                        }
                        for (int assigned = 0; assigned < start; assigned++)
                        {
                            if (actorDestinations[assigned] != best || visitedActors[assigned] != 0) continue;
                            visitedActors[assigned] = 1;
                            queue.Add(assigned);
                        }
                    }
                }
                if (!matched) return false;
            }
            // 전원 매칭 뒤 실제 슬롯의 잔량을 한 개씩 배정한다.
            var availableStations = destinations.AsArray();
            var availableSlots = slots.AsArray();
            for (int i = 0; i < actors.Length; i++)
            {
                int destinationIndex = actorDestinations[i];
                var station = availableStations[destinationIndex];
                var slot = availableSlots[station.NextSlot];
                plans.Add(new SlotReservationPlan { Member = actors[i], Destination = station.Station, Slot = slot.Index });
                slot.Quantity--;
                availableSlots[station.NextSlot] = slot;
                if (slot.Quantity == 0)
                {
                    station.NextSlot++;
                    availableStations[destinationIndex] = station;
                }
            }
            return true;
        }

        private bool ReadAvailableDestinations(Entity source, int actorCount, NativeList<AvailableStation> destinations,
            NativeList<AvailableSlot> slots)
        {
            bool hasSourceNetwork = Networks.TryGetComponent(source, out var sourceNetwork);
            int totalCapacity = 0;
            foreach (Entity station in Stations)
            {
                if (Preparations.HasComponent(station) || Pending.HasComponent(station)) continue;
                int slotCount = FuelSlots.TryGetComponent(station, out var fuel) ? fuel.Start : Storages[station].SlotCount;
                int firstSlot = slotCount - Dedicated[station].DedicatedSlotCount;
                var storedItems = Items[station];
                Reserved.TryGetBuffer(station, out var reservations);
                int nextSlot = slots.Length;
                int capacity = 0;
                for (int slot = firstSlot; slot < slotCount && capacity < actorCount; slot++)
                {
                    int quantity = math.min(actorCount - capacity,
                        StationStorageUtility.FreeDroneQuantity(storedItems[slot], reservations, slot, MaximumStack));
                    if (quantity <= 0) continue;
                    slots.Add(new AvailableSlot { Index = slot, Quantity = quantity });
                    capacity += quantity;
                }
                if (capacity == 0) continue;
                destinations.Add(new AvailableStation
                {
                    Station = station, Capacity = capacity, NextSlot = nextSlot,
                    SameNetwork = hasSourceNetwork && Networks[station].RepresentativeStation == sourceNetwork.RepresentativeStation
                });
                totalCapacity += math.min(actorCount - totalCapacity, capacity);
            }
            return totalCapacity == actorCount;
        }

        private bool IsPreferredDestination(in AvailableStation candidate, float distance,
            in AvailableStation current, float currentDistance, NativeHashMap<Entity, StationGeometryRead> geometry)
        {
            if (candidate.SameNetwork != current.SameNetwork) return candidate.SameNetwork;
            if (distance != currentDistance) return distance < currentDistance;
            return IsStationEarlierForTieBreak(candidate.Station, current.Station, geometry);
        }

        private struct ActorRouteOrigin
        {
            public float Charge;
            public float2 Position;
        }

        private struct StationGeometryRead
        {
            public int2 Position;
            public float2 Center;
            public Entity Network;
            public bool HasStamp;
            public PlacementStamp Stamp;
        }

        private static bool IsStationEarlierForTieBreak(Entity left, Entity right,
            NativeHashMap<Entity, StationGeometryRead> geometry)
        {
            var leftGeometry = geometry[left];
            var rightGeometry = geometry[right];
            if (leftGeometry.HasStamp && rightGeometry.HasStamp)
            {
                int order = PlacementStamp.Compare(true, leftGeometry.Stamp, leftGeometry.Position,
                    true, rightGeometry.Stamp, rightGeometry.Position);
                return order < 0;
            }
            return left.Index < right.Index || (left.Index == right.Index && left.Version < right.Version);
        }

        /// <summary>전용 수납칸 확보와 독립적인 이관 충전 경유 후보를 준비한다.</summary>
        private void BuildChargingStationsForCohort(NativeList<DroneFlightStation> flights, Entity source,
            NativeHashMap<Entity, StationGeometryRead> geometry)
        {
            flights.Clear();
            foreach (Entity station in Stations)
            {
                if (Pending.HasComponent(station)) continue;
                bool hasGeometry = geometry.TryGetValue(station, out var row);
                if (!hasGeometry)
                {
                    row.HasStamp = Stamps.TryGetComponent(station, out row.Stamp);
                    row.Position = Positions[station].Value;
                    row.Center = (float2)row.Position + (float2)Footprints[station].GetEffectiveSize(Directions[station].dir) * 0.5f;
                }
                // 목적지 보관 예약은 경유 충전을 막지 않는다. 철거 준비 원점은 기존 이관 기체만 쓴다.
                bool canDock = !Preparations.HasComponent(station) || station == source;
                if (!hasGeometry)
                {
                    row.Network = Networks[station].RepresentativeStation;
                    geometry.Add(station, row);
                }
                flights.Add(new DroneFlightStation
                {
                    Entity = station, Position = row.Center, CanDock = canDock,
                    Network = row.Network, HasStamp = row.HasStamp, Stamp = row.Stamp
                });
            }
        }
    }
}
