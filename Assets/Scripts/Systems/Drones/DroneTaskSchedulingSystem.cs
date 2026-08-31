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
    private readonly DroneTaskCandidateIndex _candidateIndex = new();

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
        _candidateIndex.BeginGeneration();
        ConsumeCompletionEvents();

        using (IndexDirectTasksMarker.Auto())
            IndexDirectTasks();

        using (IndexReservationsMarker.Auto())
            IndexReservations();

        _candidateIndex.RemoveStaleCandidates();
        _candidateIndex.FinalizeCandidates();
    }

    public bool TryGetNextCandidate(
        Entity droneEntity,
        int networkId,
        out DroneScheduledCandidate candidate)
    {
        using ProfilerMarker.AutoScope matchScope = MatchCandidateMarker.Auto();
        candidate = default;

        if (!TryGetAvailableDroneData(
                droneEntity,
                out int2 droneCell,
                out DroneBattery battery,
                out int carryingCapacity))
            return false;

        return _candidateIndex.TryFindNearestFeasible(
            networkId,
            droneCell,
            battery,
            carryingCapacity,
            out candidate);
    }

    public void RemoveCandidate(DroneScheduledCandidate candidate)
    {
        _candidateIndex.Remove(candidate.candidateEntity);
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

            _candidateIndex.AddOrRefresh(new DroneScheduledCandidate
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

            _candidateIndex.AddOrRefresh(new DroneScheduledCandidate
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
            _candidateIndex.Remove(completionEvent.completedTaskEntity);
            _candidateIndex.Remove(
                completionEvent.completedReservationEntity);
            EntityManager.DestroyEntity(eventEntities[i]);
        }
    }

}
