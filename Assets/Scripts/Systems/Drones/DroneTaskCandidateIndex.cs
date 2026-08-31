using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;

public sealed class DroneTaskCandidateIndex
{
    private const int EmergencyBucketIndex = 0;
    private const int BucketCount =
        DroneTaskPriorityUtility.MaximumNormalPriority + 1;

    private readonly Dictionary<int, NetworkCandidateIndex>
        _candidatesByNetwork = new();
    private readonly Dictionary<Entity, CandidateRegistration>
        _registeredCandidates = new();
    private readonly List<Entity> _staleCandidateEntities = new();
    private uint _candidateGeneration;

    public void BeginGeneration()
    {
        _candidateGeneration++;
    }

    public void AddOrRefresh(DroneScheduledCandidate candidate)
    {
        if (_registeredCandidates.TryGetValue(
                candidate.candidateEntity,
                out CandidateRegistration registration))
        {
            registration.SeenGeneration = _candidateGeneration;

            if (AreEquivalent(registration.Candidate, candidate))
                return;

            RemoveCandidateFromBuckets(registration.Candidate);
            registration.Candidate = candidate;
            AddCandidateToBuckets(candidate);
            return;
        }

        registration = new CandidateRegistration
        {
            Candidate = candidate,
            SeenGeneration = _candidateGeneration
        };
        _registeredCandidates.Add(candidate.candidateEntity, registration);
        AddCandidateToBuckets(candidate);
    }

    public bool TryFindNearestFeasible(
        int networkId,
        int2 droneCell,
        DroneBattery battery,
        int carryingCapacity,
        out DroneScheduledCandidate candidate)
    {
        candidate = default;

        if (!_candidatesByNetwork.TryGetValue(
                networkId,
                out NetworkCandidateIndex networkCandidates))
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

    public void Remove(Entity candidateEntity)
    {
        if (candidateEntity == Entity.Null)
            return;

        if (!_registeredCandidates.TryGetValue(
                candidateEntity,
                out CandidateRegistration registration))
            return;

        RemoveCandidateFromBuckets(registration.Candidate);
        _registeredCandidates.Remove(candidateEntity);
    }

    public void RemoveStaleCandidates()
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
            Remove(_staleCandidateEntities[i]);

        _staleCandidateEntities.Clear();
    }

    public void FinalizeCandidates()
    {
        foreach (NetworkCandidateIndex networkCandidates in
                 _candidatesByNetwork.Values)
            networkCandidates.FinalizeCandidates();
    }

    private void AddCandidateToBuckets(DroneScheduledCandidate candidate)
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

    private void RemoveCandidateFromBuckets(DroneScheduledCandidate candidate)
    {
        if (!_candidatesByNetwork.TryGetValue(
                candidate.networkId,
                out NetworkCandidateIndex networkCandidates))
            return;

        int bucketIndex = ResolveBucketIndex(candidate.priority);
        networkCandidates.Buckets[bucketIndex].Remove(candidate);
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
