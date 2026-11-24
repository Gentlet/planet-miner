using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

/// <summary>접수 순서에 따라 수행자 전환과 전용 수납/출고 예산을 중재한다. 정박은 슬롯을 확보하지 않는다.</summary>
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
            RequestLookup = SystemAPI.GetComponentLookup<DroneStationRequest>(true),
            Decisions = SystemAPI.GetComponentLookup<DroneStationDecision>(false),
            PendingLookup = SystemAPI.GetBufferLookup<DroneTaskPendingPublicationElement>(true),
            TransportPendingLookup = SystemAPI.GetBufferLookup<DroneTransportPendingReservationElement>(true),
            ActionLookup = SystemAPI.GetComponentLookup<DroneActionReadyRequest>(true),
            FuelSlots = SystemAPI.GetComponentLookup<FuelStorageSlots>(true),
            Storages = SystemAPI.GetComponentLookup<Storage>(true), Dedicated = SystemAPI.GetComponentLookup<DroneStationStorage>(true),
            Items = SystemAPI.GetBufferLookup<StorageSlotElement>(true),
            Reserved = SystemAPI.GetBufferLookup<DroneStationReservedSlotElement>(true),
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
    [ReadOnly] public ComponentLookup<FuelStorageSlots> FuelSlots;
    [ReadOnly] public ComponentLookup<DroneStationStorage> Dedicated;
    [ReadOnly] public BufferLookup<StorageSlotElement> Items;
    [ReadOnly] public BufferLookup<DroneStationReservedSlotElement> Reserved;
    public int MaximumStack;

    public void Execute()
    {
        using var records = new NativeList<DroneStationRequestRecord>(Requests.Length, Allocator.Temp);
        DroneStationLifecycleUtility.ReadSortedRequests(Requests, RequestLookup, Decisions, records);
        var withdrawals = new NativeHashMap<int3, int>(math.max(1, Requests.Length), Allocator.Temp);
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
            if (request.Operation == DroneStationOperation.ActivateStored)
            {
                var key = new int3(request.Station.Index, request.Station.Version, request.SourceSlotIndex);
                withdrawals.TryGetValue(key, out int used);
                var slot = Items[request.Station][request.SourceSlotIndex];
                decision.Approved = slot.ItemType == ItemTypeEnum.Drone && slot.Quantity > used;
                decision.SlotIndex = request.SourceSlotIndex;
                if (decision.Approved) withdrawals[key] = used + 1;
                else decision.Status = DroneStationRequestStatus.NoSpace;
                Decisions[record.Entity] = decision;
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
            else if (request.Operation is DroneStationOperation.Activate or DroneStationOperation.Dock)
            {
                decision.Approved = true;
                decision.SlotIndex = -1;
            }
            else if (MaximumStack > 0)
            {
                decision.SlotIndex = DroneStationLifecycleUtility.ReserveSlot(request, -1, MaximumStack,
                    Storages, Dedicated, FuelSlots, Items, ranges, slots, Reserved);
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
        withdrawals.Dispose();
    }
}
