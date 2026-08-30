using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;

public enum DroneScheduledCandidateKind : byte
{
    Reservation,
    DirectTask
}

public struct DroneScheduledCandidate
{
    public DroneScheduledCandidateKind kind;
    public Entity candidateEntity;
    public Entity taskEntity;
    public Entity sourceOwner;
    public Entity destinationOwner;
    public int networkId;
    public int2 workCell;
    public int2 secondWaypointCell;
    public int2 returnStationCell;
    public bool hasSecondWaypoint;
    public int quantity;
    public DroneTaskPriority priority;
    public ulong creationOrder;
}

public partial class DroneTaskSchedulingSystem : SystemBase
{
    private const int EmergencyBucketIndex = 0;
    private const int BucketCount =
        DroneTaskPriorityUtility.MaximumNormalPriority + 1;
    private static readonly ProfilerMarker PrepareCandidatesMarker =
        new("DroneTaskScheduler.PrepareCandidates");
    private static readonly ProfilerMarker IndexDirectTasksMarker =
        new("DroneTaskScheduler.IndexDirectTasks");
    private static readonly ProfilerMarker IndexReservationsMarker =
        new("DroneTaskScheduler.IndexReservations");
    private static readonly ProfilerMarker MatchCandidateMarker =
        new("DroneTaskScheduler.MatchCandidate");

    private EntityQuery _directTaskQuery;
    private EntityQuery _reservationQuery;
    private EntityQuery _completionEventQuery;
    private DroneStationNetworkSystem _networkSystem;
    private readonly Dictionary<int, NetworkCandidateIndex>
        _candidatesByNetwork = new();
    private readonly Dictionary<Entity, CandidateRegistration>
        _registeredCandidates = new();
    private readonly List<Entity> _staleCandidateEntities = new();
    private uint _candidateGeneration;

    protected override void OnCreate()
    {
        _directTaskQuery = GetEntityQuery(
            ComponentType.ReadOnly<DroneTask>(),
            ComponentType.ReadOnly<DroneTaskStatus>(),
            ComponentType.ReadOnly<DroneTaskPriority>(),
            ComponentType.ReadOnly<DroneTaskCreationOrder>(),
            ComponentType.ReadOnly<DroneDirectTaskPlan>());
        _reservationQuery = GetEntityQuery(new EntityQueryDesc
        {
            All = new[]
            {
                ComponentType.ReadOnly<DroneTaskReservation>(),
                ComponentType.ReadOnly<DroneTaskReservedItemElement>()
            },
            None = new[]
            {
                ComponentType.ReadOnly<DroneReservationAssignment>()
            }
        });
        _completionEventQuery = GetEntityQuery(
            ComponentType.ReadOnly<DroneTaskCompletionEvent>());
        _networkSystem = World.GetOrCreateSystemManaged<
            DroneStationNetworkSystem>();
    }

    protected override void OnUpdate()
    {
    }

    public void PrepareCandidates()
    {
        using ProfilerMarker.AutoScope prepareScope =
            PrepareCandidatesMarker.Auto();
        _candidateGeneration++;
        ConsumeCompletionEvents();

        using (IndexDirectTasksMarker.Auto())
            IndexDirectTasks();

        using (IndexReservationsMarker.Auto())
            IndexReservations();

        RemoveStaleCandidates();

        foreach (NetworkCandidateIndex networkCandidates in
                 _candidatesByNetwork.Values)
            networkCandidates.FinalizeCandidates();
    }

    public bool TryGetNextCandidate(
        Entity droneEntity,
        int networkId,
        out DroneScheduledCandidate candidate)
    {
        using ProfilerMarker.AutoScope matchScope = MatchCandidateMarker.Auto();
        candidate = default;

        if (!_candidatesByNetwork.TryGetValue(
                networkId,
                out NetworkCandidateIndex networkCandidates))
            return false;

        if (!TryGetAvailableDroneData(
                droneEntity,
                out int2 droneCell,
                out DroneBattery battery,
                out int carryingCapacity))
            return false;

        for (int bucketIndex = 0;
             bucketIndex < BucketCount;
             bucketIndex++)
        {
            if (networkCandidates.Buckets[bucketIndex].TryFindNearestFeasible(
                    droneCell,
                    battery,
                    carryingCapacity,
                    out candidate))
                return true;
        }

        return false;
    }

    public void RemoveCandidate(DroneScheduledCandidate candidate)
    {
        if (!_registeredCandidates.TryGetValue(
                candidate.candidateEntity,
                out CandidateRegistration registration))
            return;

        RemoveCandidateFromIndex(registration.Candidate);
        _registeredCandidates.Remove(candidate.candidateEntity);
    }

    private void IndexDirectTasks()
    {
        using NativeArray<Entity> taskEntities =
            _directTaskQuery.ToEntityArray(Allocator.Temp);

        for (int i = 0; i < taskEntities.Length; i++)
        {
            Entity taskEntity = taskEntities[i];
            DroneTaskStatus status = EntityManager
                .GetComponentData<DroneTaskStatus>(taskEntity);

            if (status.state != DroneTaskStateEnum.Pending)
                continue;

            DroneTask task = EntityManager
                .GetComponentData<DroneTask>(taskEntity);

            if (task.type != DroneTaskTypeEnum.Demolition &&
                task.type != DroneTaskTypeEnum.RecoverWorldItem)
                continue;

            DroneDirectTaskPlan plan = EntityManager
                .GetComponentData<DroneDirectTaskPlan>(taskEntity);

            if (!TryGetReturnCell(
                    plan.networkId,
                    plan.destinationOwner,
                    plan.workCell,
                    out int2 returnCell,
                    out int2 destinationCell,
                    out bool hasDestination))
                continue;

            AddCandidate(new DroneScheduledCandidate
            {
                kind = DroneScheduledCandidateKind.DirectTask,
                candidateEntity = taskEntity,
                taskEntity = taskEntity,
                destinationOwner = plan.destinationOwner,
                networkId = plan.networkId,
                workCell = plan.workCell,
                secondWaypointCell = destinationCell,
                returnStationCell = returnCell,
                hasSecondWaypoint = hasDestination,
                quantity = 1,
                priority = EntityManager
                    .GetComponentData<DroneTaskPriority>(taskEntity),
                creationOrder = EntityManager
                    .GetComponentData<DroneTaskCreationOrder>(taskEntity)
                    .value
            });
        }
    }

    private void IndexReservations()
    {
        using NativeArray<Entity> reservationEntities =
            _reservationQuery.ToEntityArray(Allocator.Temp);

        for (int i = 0; i < reservationEntities.Length; i++)
        {
            Entity reservationEntity = reservationEntities[i];
            DroneTaskReservation reservation = EntityManager
                .GetComponentData<DroneTaskReservation>(reservationEntity);

            if (!TryGetReservationTaskData(
                    reservation,
                    out DroneTaskPriority priority,
                    out ulong creationOrder))
                continue;

            if (!TryGetReservationSource(
                    reservationEntity,
                    reservation.quantity,
                    out Entity sourceOwner))
                continue;

            if (!TryGetEntityCell(sourceOwner, out int2 sourceCell))
                continue;

            if (!TryGetEntityCell(
                    reservation.destinationOwner,
                    out int2 destinationCell))
                continue;

            if (!_networkSystem.TryGetNetworkIdAtCell(
                    sourceCell,
                    out int networkId))
                continue;

            if (!_networkSystem.TryGetNetworkIdAtCell(
                    destinationCell,
                    out int destinationNetworkId))
                continue;

            if (networkId != destinationNetworkId)
                continue;

            if (!_networkSystem.TryFindNearestStation(
                    networkId,
                    destinationCell,
                    out Entity returnStation))
                continue;

            if (!TryGetEntityCell(returnStation, out int2 returnCell))
                continue;

            AddCandidate(new DroneScheduledCandidate
            {
                kind = DroneScheduledCandidateKind.Reservation,
                candidateEntity = reservationEntity,
                taskEntity = reservation.taskEntity,
                sourceOwner = sourceOwner,
                destinationOwner = reservation.destinationOwner,
                networkId = networkId,
                workCell = sourceCell,
                secondWaypointCell = destinationCell,
                returnStationCell = returnCell,
                hasSecondWaypoint = true,
                quantity = reservation.quantity,
                priority = priority,
                creationOrder = creationOrder
            });
        }
    }

    private bool TryGetReservationTaskData(
        DroneTaskReservation reservation,
        out DroneTaskPriority priority,
        out ulong creationOrder)
    {
        priority = default;
        creationOrder = 0;

        if (!EntityManager.Exists(reservation.taskEntity))
            return false;

        if (!EntityManager.HasComponent<DroneTaskStatus>(
                reservation.taskEntity))
            return false;

        DroneTaskStatus status = EntityManager
            .GetComponentData<DroneTaskStatus>(reservation.taskEntity);

        if (status.state != DroneTaskStateEnum.Pending &&
            status.state != DroneTaskStateEnum.InProgress)
            return false;

        if (!EntityManager.HasComponent<DroneTaskPriority>(
                reservation.taskEntity))
            return false;

        if (!EntityManager.HasComponent<DroneTaskCreationOrder>(
                reservation.taskEntity))
            return false;

        priority = EntityManager.GetComponentData<DroneTaskPriority>(
            reservation.taskEntity);
        creationOrder = EntityManager
            .GetComponentData<DroneTaskCreationOrder>(reservation.taskEntity)
            .value;
        return true;
    }

    private bool TryGetReservationSource(
        Entity reservationEntity,
        int expectedQuantity,
        out Entity sourceOwner)
    {
        sourceOwner = Entity.Null;
        DynamicBuffer<DroneTaskReservedItemElement> reservedItems =
            EntityManager.GetBuffer<DroneTaskReservedItemElement>(
                reservationEntity,
                true);

        if (reservedItems.Length != expectedQuantity)
            return false;

        for (int i = 0; i < reservedItems.Length; i++)
        {
            Entity candidateOwner = reservedItems[i].sourceOwner;

            if (candidateOwner == Entity.Null)
                return false;

            if (sourceOwner == Entity.Null)
            {
                sourceOwner = candidateOwner;
                continue;
            }

            if (sourceOwner != candidateOwner)
                return false;
        }

        return sourceOwner != Entity.Null;
    }

    private bool TryGetReturnCell(
        int networkId,
        Entity destinationOwner,
        int2 workCell,
        out int2 returnCell,
        out int2 destinationCell,
        out bool hasDestination)
    {
        returnCell = default;
        destinationCell = workCell;
        hasDestination = destinationOwner != Entity.Null;

        if (hasDestination)
        {
            if (!TryGetEntityCell(destinationOwner, out destinationCell))
                return false;
        }

        int2 finalCell = hasDestination ? destinationCell : workCell;

        if (!_networkSystem.TryFindNearestStation(
                networkId,
                finalCell,
                out Entity returnStation))
            return false;

        return TryGetEntityCell(returnStation, out returnCell);
    }

    private bool TryGetAvailableDroneData(
        Entity droneEntity,
        out int2 droneCell,
        out DroneBattery battery,
        out int carryingCapacity)
    {
        droneCell = default;
        battery = default;
        carryingCapacity = 0;

        if (!TryGetEntityCell(droneEntity, out droneCell))
            return false;

        if (!EntityManager.HasComponent<DroneBattery>(droneEntity))
            return false;

        if (!EntityManager.HasComponent<ActiveDrone>(droneEntity))
            return false;

        battery = EntityManager.GetComponentData<DroneBattery>(droneEntity);
        carryingCapacity = EntityManager
            .GetComponentData<ActiveDrone>(droneEntity)
            .carryingCapacity;
        return true;
    }

    private bool TryGetEntityCell(Entity entity, out int2 cell)
    {
        cell = default;

        if (entity == Entity.Null)
            return false;

        if (!EntityManager.Exists(entity))
            return false;

        if (!EntityManager.HasComponent<GridPosition>(entity))
            return false;

        cell = EntityManager.GetComponentData<GridPosition>(entity)
            .gridPosition;
        return true;
    }

    private void AddCandidate(DroneScheduledCandidate candidate)
    {
        if (_registeredCandidates.TryGetValue(
                candidate.candidateEntity,
                out CandidateRegistration registration))
        {
            registration.SeenGeneration = _candidateGeneration;

            if (AreEquivalent(registration.Candidate, candidate))
                return;

            RemoveCandidateFromIndex(registration.Candidate);
            registration.Candidate = candidate;
            AddCandidateToIndex(candidate);
            return;
        }

        registration = new CandidateRegistration
        {
            Candidate = candidate,
            SeenGeneration = _candidateGeneration
        };
        _registeredCandidates.Add(candidate.candidateEntity, registration);
        AddCandidateToIndex(candidate);
    }

    private void AddCandidateToIndex(DroneScheduledCandidate candidate)
    {
        if (!_candidatesByNetwork.TryGetValue(
                candidate.networkId,
                out NetworkCandidateIndex networkCandidates))
        {
            networkCandidates = new NetworkCandidateIndex(BucketCount);
            _candidatesByNetwork.Add(candidate.networkId, networkCandidates);
        }

        int bucketIndex = ResolveBucketIndex(candidate.priority);
        networkCandidates.Buckets[bucketIndex].Add(candidate);
    }

    private void RemoveCandidateFromIndex(DroneScheduledCandidate candidate)
    {
        if (!_candidatesByNetwork.TryGetValue(
                candidate.networkId,
                out NetworkCandidateIndex networkCandidates))
            return;

        int bucketIndex = ResolveBucketIndex(candidate.priority);
        networkCandidates.Buckets[bucketIndex].Remove(candidate);
    }

    private void RemoveStaleCandidates()
    {
        _staleCandidateEntities.Clear();

        foreach (KeyValuePair<Entity, CandidateRegistration> pair in
                 _registeredCandidates)
        {
            if (pair.Value.SeenGeneration == _candidateGeneration)
                continue;

            _staleCandidateEntities.Add(pair.Key);
        }

        for (int i = 0; i < _staleCandidateEntities.Count; i++)
        {
            Entity candidateEntity = _staleCandidateEntities[i];
            CandidateRegistration registration =
                _registeredCandidates[candidateEntity];
            RemoveCandidateFromIndex(registration.Candidate);
            _registeredCandidates.Remove(candidateEntity);
        }

        _staleCandidateEntities.Clear();
    }

    private void ConsumeCompletionEvents()
    {
        if (_completionEventQuery.IsEmptyIgnoreFilter)
            return;

        using NativeArray<Entity> eventEntities =
            _completionEventQuery.ToEntityArray(Allocator.Temp);

        for (int i = 0; i < eventEntities.Length; i++)
        {
            DroneTaskCompletionEvent completionEvent = EntityManager
                .GetComponentData<DroneTaskCompletionEvent>(eventEntities[i]);
            RemoveRegisteredCandidate(completionEvent.completedTaskEntity);
            RemoveRegisteredCandidate(
                completionEvent.completedReservationEntity);
            EntityManager.DestroyEntity(eventEntities[i]);
        }
    }

    private void RemoveRegisteredCandidate(Entity candidateEntity)
    {
        if (candidateEntity == Entity.Null)
            return;

        if (!_registeredCandidates.TryGetValue(
                candidateEntity,
                out CandidateRegistration registration))
            return;

        RemoveCandidateFromIndex(registration.Candidate);
        _registeredCandidates.Remove(candidateEntity);
    }

    private static bool AreEquivalent(
        DroneScheduledCandidate left,
        DroneScheduledCandidate right)
    {
        return left.kind == right.kind &&
               left.taskEntity == right.taskEntity &&
               left.sourceOwner == right.sourceOwner &&
               left.destinationOwner == right.destinationOwner &&
               left.networkId == right.networkId &&
               left.workCell.Equals(right.workCell) &&
               left.secondWaypointCell.Equals(right.secondWaypointCell) &&
               left.returnStationCell.Equals(right.returnStationCell) &&
               left.hasSecondWaypoint == right.hasSecondWaypoint &&
               left.quantity == right.quantity &&
               left.priority.priorityClass == right.priority.priorityClass &&
               left.priority.normalPriority == right.priority.normalPriority &&
               left.creationOrder == right.creationOrder;
    }

    private static int ResolveBucketIndex(DroneTaskPriority priority)
    {
        if (priority.priorityClass == DroneTaskPriorityClassEnum.Emergency)
            return EmergencyBucketIndex;

        return math.clamp(
            priority.normalPriority,
            DroneTaskPriorityUtility.MinimumNormalPriority,
            DroneTaskPriorityUtility.MaximumNormalPriority);
    }

    private sealed class NetworkCandidateIndex
    {
        public readonly SpatialCandidateBucket[] Buckets;

        public NetworkCandidateIndex(int bucketCount)
        {
            Buckets = new SpatialCandidateBucket[bucketCount];

            for (int i = 0; i < bucketCount; i++)
                Buckets[i] = new SpatialCandidateBucket();
        }

        public void Clear()
        {
            for (int i = 0; i < Buckets.Length; i++)
                Buckets[i].Clear();
        }

        public void FinalizeCandidates()
        {
            for (int i = 0; i < Buckets.Length; i++)
                Buckets[i].FinalizeCandidates();
        }
    }

    private sealed class CandidateRegistration
    {
        public DroneScheduledCandidate Candidate;
        public uint SeenGeneration;
    }

    private sealed class SpatialCandidateBucket
    {
        private readonly Dictionary<int2, CellCandidateBucket>
            _candidatesByChunk = new();
        private readonly List<int2> _activeChunks = new();
        private int2 _minimumChunk;
        private int2 _maximumChunk;
        private bool _hasBounds;
        private int _count;

        public void Add(DroneScheduledCandidate candidate)
        {
            int2 chunk = ChunkUtility.ToChunkPosition(candidate.workCell);

            if (!_candidatesByChunk.TryGetValue(
                    chunk,
                    out CellCandidateBucket candidates))
            {
                candidates = new CellCandidateBucket();
                _candidatesByChunk.Add(chunk, candidates);
                _activeChunks.Add(chunk);

                if (!_hasBounds)
                {
                    _minimumChunk = chunk;
                    _maximumChunk = chunk;
                    _hasBounds = true;
                }
                else
                {
                    _minimumChunk = math.min(_minimumChunk, chunk);
                    _maximumChunk = math.max(_maximumChunk, chunk);
                }
            }

            candidates.Add(candidate);
            _count++;
        }

        public void FinalizeCandidates()
        {
            for (int i = 0; i < _activeChunks.Count; i++)
                _candidatesByChunk[_activeChunks[i]].FinalizeCandidates();
        }

        public bool TryFindNearestFeasible(
            int2 droneCell,
            DroneBattery battery,
            int carryingCapacity,
            out DroneScheduledCandidate selectedCandidate)
        {
            selectedCandidate = default;

            if (!_hasBounds || _count == 0)
                return false;

            bool found = false;
            float selectedDistanceSquared = float.MaxValue;
            int2 centerChunk = ChunkUtility.ToChunkPosition(droneCell);
            int maximumRing = GetMaximumRing(centerChunk);

            for (int ring = 0; ring <= maximumRing; ring++)
            {
                VisitRing(
                    centerChunk,
                    ring,
                    droneCell,
                    battery,
                    carryingCapacity,
                    ref found,
                    ref selectedDistanceSquared,
                    ref selectedCandidate);

                if (!found)
                    continue;

                float outsideDistanceSquared =
                    GetMinimumOutsideDistanceSquared(
                        droneCell,
                        centerChunk,
                        ring);

                if (outsideDistanceSquared > selectedDistanceSquared)
                    break;
            }

            return found;
        }

        public void Remove(DroneScheduledCandidate candidate)
        {
            int2 chunk = ChunkUtility.ToChunkPosition(candidate.workCell);

            if (!_candidatesByChunk.TryGetValue(
                    chunk,
                    out CellCandidateBucket candidates))
                return;

            if (candidates.Remove(candidate))
                _count--;
        }

        public void Clear()
        {
            for (int i = 0; i < _activeChunks.Count; i++)
                _candidatesByChunk[_activeChunks[i]].Clear();

            _activeChunks.Clear();
            _hasBounds = false;
            _count = 0;
        }

        private int GetMaximumRing(int2 centerChunk)
        {
            int2 minimumDelta = math.abs(centerChunk - _minimumChunk);
            int2 maximumDelta = math.abs(centerChunk - _maximumChunk);
            return math.max(
                math.cmax(minimumDelta),
                math.cmax(maximumDelta));
        }

        private void VisitRing(
            int2 centerChunk,
            int ring,
            int2 droneCell,
            DroneBattery battery,
            int carryingCapacity,
            ref bool found,
            ref float selectedDistanceSquared,
            ref DroneScheduledCandidate selectedCandidate)
        {
            if (ring == 0)
            {
                VisitChunk(
                    centerChunk,
                    droneCell,
                    battery,
                    carryingCapacity,
                    ref found,
                    ref selectedDistanceSquared,
                    ref selectedCandidate);
                return;
            }

            int minimumX = centerChunk.x - ring;
            int maximumX = centerChunk.x + ring;
            int minimumY = centerChunk.y - ring;
            int maximumY = centerChunk.y + ring;

            for (int x = minimumX; x <= maximumX; x++)
            {
                VisitChunk(
                    new int2(x, minimumY),
                    droneCell,
                    battery,
                    carryingCapacity,
                    ref found,
                    ref selectedDistanceSquared,
                    ref selectedCandidate);
                VisitChunk(
                    new int2(x, maximumY),
                    droneCell,
                    battery,
                    carryingCapacity,
                    ref found,
                    ref selectedDistanceSquared,
                    ref selectedCandidate);
            }

            for (int y = minimumY + 1; y < maximumY; y++)
            {
                VisitChunk(
                    new int2(minimumX, y),
                    droneCell,
                    battery,
                    carryingCapacity,
                    ref found,
                    ref selectedDistanceSquared,
                    ref selectedCandidate);
                VisitChunk(
                    new int2(maximumX, y),
                    droneCell,
                    battery,
                    carryingCapacity,
                    ref found,
                    ref selectedDistanceSquared,
                    ref selectedCandidate);
            }
        }

        private void VisitChunk(
            int2 chunk,
            int2 droneCell,
            DroneBattery battery,
            int carryingCapacity,
            ref bool found,
            ref float selectedDistanceSquared,
            ref DroneScheduledCandidate selectedCandidate)
        {
            if (!_candidatesByChunk.TryGetValue(
                    chunk,
                    out CellCandidateBucket candidates))
                return;

            if (!candidates.TryFindNearestFeasible(
                    droneCell,
                    battery,
                    carryingCapacity,
                    out DroneScheduledCandidate candidate,
                    out float distanceSquared))
                return;

            if (found && distanceSquared > selectedDistanceSquared)
                return;

            if (found && distanceSquared == selectedDistanceSquared &&
                candidate.creationOrder >= selectedCandidate.creationOrder)
                return;

            selectedCandidate = candidate;
            selectedDistanceSquared = distanceSquared;
            found = true;
        }

        private static float GetMinimumOutsideDistanceSquared(
            int2 droneCell,
            int2 centerChunk,
            int ring)
        {
            int2 minimumCell =
                (centerChunk - new int2(ring)) * GameConstants.chunkSize;
            int2 maximumCell =
                (centerChunk + new int2(ring + 1)) * GameConstants.chunkSize -
                new int2(1);
            int minimumOutsideDistance = math.min(
                math.min(
                    droneCell.x - minimumCell.x + 1,
                    maximumCell.x - droneCell.x + 1),
                math.min(
                    droneCell.y - minimumCell.y + 1,
                    maximumCell.y - droneCell.y + 1));
            return minimumOutsideDistance * minimumOutsideDistance;
        }
    }

    private sealed class CellCandidateBucket
    {
        private readonly Dictionary<int2, List<DroneScheduledCandidate>>
            _candidatesByCell = new();
        private readonly List<int2> _activeCells = new();
        private readonly HashSet<int2> _dirtyCellSet = new();
        private readonly List<int2> _dirtyCells = new();
        private int _count;

        public void Add(DroneScheduledCandidate candidate)
        {
            if (!_candidatesByCell.TryGetValue(
                    candidate.workCell,
                    out List<DroneScheduledCandidate> candidates))
            {
                candidates = new List<DroneScheduledCandidate>();
                _candidatesByCell.Add(candidate.workCell, candidates);
                _activeCells.Add(candidate.workCell);
            }

            candidates.Add(candidate);
            _count++;

            if (_dirtyCellSet.Add(candidate.workCell))
                _dirtyCells.Add(candidate.workCell);
        }

        public void FinalizeCandidates()
        {
            for (int i = 0; i < _dirtyCells.Count; i++)
            {
                _candidatesByCell[_dirtyCells[i]].Sort(
                    CompareCreationOrderDescending);
            }

            _dirtyCells.Clear();
            _dirtyCellSet.Clear();
        }

        public bool TryFindNearestFeasible(
            int2 droneCell,
            DroneBattery battery,
            int carryingCapacity,
            out DroneScheduledCandidate selectedCandidate,
            out float selectedDistanceSquared)
        {
            selectedCandidate = default;
            selectedDistanceSquared = float.MaxValue;
            bool found = false;

            for (int cellIndex = 0;
                 cellIndex < _activeCells.Count;
                 cellIndex++)
            {
                int2 workCell = _activeCells[cellIndex];
                List<DroneScheduledCandidate> candidates =
                    _candidatesByCell[workCell];

                if (!TryGetFirstFeasible(
                        candidates,
                        droneCell,
                        battery,
                        carryingCapacity,
                        out DroneScheduledCandidate candidate))
                    continue;

                float distanceSquared = math.distancesq(
                    new float2(droneCell),
                    new float2(workCell));

                if (found && distanceSquared > selectedDistanceSquared)
                    continue;

                if (found && distanceSquared == selectedDistanceSquared &&
                    candidate.creationOrder >=
                    selectedCandidate.creationOrder)
                    continue;

                selectedCandidate = candidate;
                selectedDistanceSquared = distanceSquared;
                found = true;
            }

            return found;
        }

        public bool Remove(DroneScheduledCandidate candidate)
        {
            if (!_candidatesByCell.TryGetValue(
                    candidate.workCell,
                    out List<DroneScheduledCandidate> candidates))
                return false;

            for (int i = candidates.Count - 1; i >= 0; i--)
            {
                DroneScheduledCandidate current = candidates[i];

                if (current.kind != candidate.kind)
                    continue;

                if (current.candidateEntity != candidate.candidateEntity)
                    continue;

                candidates.RemoveAt(i);
                _count--;
                return true;
            }

            return false;
        }

        public void Clear()
        {
            for (int i = 0; i < _activeCells.Count; i++)
                _candidatesByCell[_activeCells[i]].Clear();

            _activeCells.Clear();
            _dirtyCells.Clear();
            _dirtyCellSet.Clear();
            _count = 0;
        }

        private static bool TryGetFirstFeasible(
            List<DroneScheduledCandidate> candidates,
            int2 droneCell,
            DroneBattery battery,
            int carryingCapacity,
            out DroneScheduledCandidate candidate)
        {
            candidate = default;

            for (int i = candidates.Count - 1; i >= 0; i--)
            {
                DroneScheduledCandidate current = candidates[i];

                if (current.quantity > carryingCapacity)
                    continue;

                if (!CanCompleteRoute(droneCell, battery, current))
                    continue;

                candidate = current;
                return true;
            }

            return false;
        }

        private static bool CanCompleteRoute(
            int2 droneCell,
            DroneBattery battery,
            DroneScheduledCandidate candidate)
        {
            if (battery.consumptionPerDistance <= 0f)
                return true;

            float routeDistance = math.distance(
                new float2(droneCell),
                new float2(candidate.workCell));
            int2 finalCell = candidate.workCell;

            if (candidate.hasSecondWaypoint)
            {
                routeDistance += math.distance(
                    new float2(candidate.workCell),
                    new float2(candidate.secondWaypointCell));
                finalCell = candidate.secondWaypointCell;
            }

            routeDistance += math.distance(
                new float2(finalCell),
                new float2(candidate.returnStationCell));
            return battery.current >=
                   routeDistance * battery.consumptionPerDistance;
        }

        private static int CompareCreationOrderDescending(
            DroneScheduledCandidate left,
            DroneScheduledCandidate right)
        {
            return right.creationOrder.CompareTo(left.creationOrder);
        }
    }
}
