using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

/// <summary>접수 순서와 공유 슬롯 예산을 단일 Burst Job에서 중재한다. 새 출고 공간을 같은 계획에 재사용하지 않는다.</summary>
[UpdateInGroup(typeof(DroneReservationGroup))]
[UpdateAfter(typeof(DroneTaskReservationSystem))]
public partial struct DroneStationLifecycleReservationSystem : ISystem
{
    private EntityQuery _requests;
    private EntityQuery _pending;
    private EntityQuery _actions;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        _requests = SystemAPI.QueryBuilder().WithAll<DroneStationRequest>().WithAllRW<DroneStationDecision>().Build();
        _pending = SystemAPI.QueryBuilder().WithAll<DroneTaskPendingPublicationElement>().Build();
        _actions = SystemAPI.QueryBuilder().WithAll<DroneActionReadyRequest>().Build();
        state.RequireForUpdate(_requests);
        state.RequireForUpdate<ItemRegistry>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var requests = _requests.ToEntityListAsync(Allocator.TempJob, state.Dependency, out var requestsReady);
        var pending = _pending.ToEntityListAsync(Allocator.TempJob, state.Dependency, out var pendingReady);
        var actions = _actions.ToEntityListAsync(Allocator.TempJob, state.Dependency, out var actionsReady);
        var dependency = JobHandle.CombineDependencies(requestsReady, pendingReady, actionsReady);
        var handle = new DroneStationLifecycleReservationJob
        {
            Requests = requests.AsDeferredJobArray(), PendingOwners = pending.AsDeferredJobArray(), Actions = actions.AsDeferredJobArray(),
            RequestLookup = state.GetComponentLookup<DroneStationRequest>(true),
            Decisions = state.GetComponentLookup<DroneStationDecision>(false),
            PendingLookup = state.GetBufferLookup<DroneTaskPendingPublicationElement>(true),
            TransportPendingLookup = state.GetBufferLookup<DroneTransportPendingReservationElement>(true),
            ActionLookup = state.GetComponentLookup<DroneActionReadyRequest>(true),
            Storages = state.GetComponentLookup<Storage>(true), Dedicated = state.GetComponentLookup<DroneStationStorage>(true),
            Items = state.GetBufferLookup<StoredItemElement>(true), Docked = state.GetBufferLookup<DockedDroneElement>(true),
            Docks = state.GetComponentLookup<DroneDocked>(true),
            MaximumStack = ItemRegistry.GetMaxStack(SystemAPI.GetSingletonBuffer<ItemConfigElement>(true), ItemTypeEnum.Drone)
        }.Schedule(JobHandle.CombineDependencies(state.Dependency, dependency));
        state.Dependency = JobHandle.CombineDependencies(requests.Dispose(handle), pending.Dispose(handle), actions.Dispose(handle));
    }
}

[BurstCompile]
internal struct DroneStationLifecycleReservationJob : IJob
{
    [ReadOnly] public NativeArray<Entity> Requests;
    [ReadOnly] public NativeArray<Entity> PendingOwners;
    [ReadOnly] public NativeArray<Entity> Actions;
    [ReadOnly] public ComponentLookup<DroneStationRequest> RequestLookup;
    public ComponentLookup<DroneStationDecision> Decisions;
    [ReadOnly] public BufferLookup<DroneTaskPendingPublicationElement> PendingLookup;
    [ReadOnly] public BufferLookup<DroneTransportPendingReservationElement> TransportPendingLookup;
    [ReadOnly] public ComponentLookup<DroneActionReadyRequest> ActionLookup;
    [ReadOnly] public ComponentLookup<Storage> Storages;
    [ReadOnly] public ComponentLookup<DroneStationStorage> Dedicated;
    [ReadOnly] public BufferLookup<StoredItemElement> Items;
    [ReadOnly] public BufferLookup<DockedDroneElement> Docked;
    [ReadOnly] public ComponentLookup<DroneDocked> Docks;
    public int MaximumStack;

    public void Execute()
    {
        using var records = new NativeList<DroneStationRequestRecord>(Requests.Length, Allocator.Temp);
        DroneStationLifecycleUtility.ReadSortedRequests(Requests, RequestLookup, Decisions, records);
        using var chosen = new NativeHashSet<Entity>(16, Allocator.Temp);
        using var busy = new NativeHashSet<Entity>(16, Allocator.Temp);
        DroneStationLifecycleUtility.GatherBusyWorkers(PendingOwners, PendingLookup, TransportPendingLookup, Actions, ActionLookup, busy);
        using var ranges = new NativeParallelHashMap<Entity, DroneStationSlotRange>(math.max(1, Requests.Length), Allocator.Temp);
        using var slots = new NativeList<DroneStationSlot>(Allocator.Temp);
        foreach (var record in records)
        {
            var request = record.Request;
            var decision = record.Decision;
            if (!decision.Eligible)
            {
                continue;
            }
            if (chosen.Contains(request.Drone))
            {
                decision.Status = DroneStationRequestStatus.Duplicate;
            }
            else if (busy.Contains(request.Drone))
            {
                decision.Status = DroneStationRequestStatus.Busy;
            }
            else if (request.Operation == DroneStationOperation.Activate)
            {
                decision.Approved = true;
            }
            else if (MaximumStack > 0)
            {
                int preferred = Docks.HasComponent(request.Drone) ? Docks[request.Drone].SlotIndex : -1;
                decision.SlotIndex = DroneStationLifecycleUtility.ReserveSlot(request.Station, request.Drone, request.Operation,
                    preferred, MaximumStack, Storages, Dedicated, Items, Docked, ranges, slots);
                decision.Approved = decision.SlotIndex >= 0;
                if (!decision.Approved)
                {
                    decision.Status = DroneStationRequestStatus.NoSpace;
                }
            }
            else
            {
                decision.Status = DroneStationRequestStatus.Rejected;
            }
            if (decision.Approved)
            {
                chosen.Add(request.Drone);
            }
            Decisions[record.Entity] = decision;
        }
    }
}
