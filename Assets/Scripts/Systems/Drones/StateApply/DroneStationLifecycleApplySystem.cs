using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

/// <summary>정박 관계 또는 수납/출고 재고의 변경과 요청 결과를 EndSimulation에 함께 공개한다.</summary>
[UpdateInGroup(typeof(DroneStateApplyGroup), OrderLast = true)]
[UpdateAfter(typeof(DroneTaskAssignmentPublishSystem))]
public partial struct DroneStationLifecycleApplySystem : ISystem
{
    private EntityQuery _requests;
    private EntityQuery _relocationDocks;
    private EntityQuery _pending;
    private EntityQuery _actions;
    private EntityQuery _droneDatabase;
    private Entity _candidates;
    private EntityQueryMask _stationMask;
    private EntityQueryMask _workerMask;
    private DroneStationEligibility _eligibility;

    public void OnCreate(ref SystemState state)
    {
        _requests = SystemAPI.QueryBuilder().WithAll<DroneStationRequest, DroneStationDecision>().Build();
        _relocationDocks = state.GetEntityQuery(ComponentType.ReadOnly<DroneStationRelocation>(),
            ComponentType.ReadOnly<DroneDocked>(), ComponentType.ReadOnly<DroneWorker>());
        _pending = state.GetEntityQuery(ComponentType.ReadOnly<DroneTaskPendingPublicationElement>());
        _actions = state.GetEntityQuery(ComponentType.ReadOnly<DroneActionReadyRequest>());
        _droneDatabase = state.GetEntityQuery(ComponentType.ReadOnly<DronePrefabDatabase>(), ComponentType.ReadOnly<DronePrefab>());
        _candidates = DroneSchedulingUtility.GetOrCreateCandidates(state.EntityManager);
        DroneStationEligibility.CreateMasks(ref state, out _stationMask, out _workerMask);
        _eligibility = DroneStationEligibility.Create(ref state, _stationMask, _workerMask);
        state.RequireForUpdate<PrefabDatabaseReady>();
        state.RequireForUpdate<DroneBatteryConfig>();
        state.RequireForUpdate<ItemRegistry>();
    }

    public void OnUpdate(ref SystemState state)
    {
        if (_requests.IsEmptyIgnoreFilter && _relocationDocks.IsEmptyIgnoreFilter) return;
        var owner = state.World.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>();
        if (!_relocationDocks.IsEmptyIgnoreFilter)
        {
            var dockRelease = new ReleaseLostRelocationDockJob
            {
                Entities = SystemAPI.GetEntityStorageInfoLookup(), ECB = owner.CreateCommandBuffer()
            }.Schedule(_relocationDocks, state.Dependency);
            state.Dependency = dockRelease;
            owner.AddJobHandleForProducer(dockRelease);
        }
        if (_requests.IsEmptyIgnoreFilter) return;
        Entity dronePrefab = _droneDatabase.GetSingleton<DronePrefab>().Prefab;
        var requests = _requests.ToEntityListAsync(Allocator.TempJob, state.Dependency, out var requestsReady);
        var pending = _pending.ToEntityListAsync(Allocator.TempJob, state.Dependency, out var pendingReady);
        var actions = _actions.ToEntityListAsync(Allocator.TempJob, state.Dependency, out var actionsReady);
        var dependency = JobHandle.CombineDependencies(requestsReady, pendingReady, actionsReady);
        _eligibility.Update(ref state);
        var handle = new DroneStationLifecycleApplyJob
        {
            Requests = requests.AsDeferredJobArray(), PendingOwners = pending.AsDeferredJobArray(), Actions = actions.AsDeferredJobArray(),
            RequestLookup = SystemAPI.GetComponentLookup<DroneStationRequest>(true),
            Decisions = SystemAPI.GetComponentLookup<DroneStationDecision>(true),
            PendingLookup = SystemAPI.GetBufferLookup<DroneTaskPendingPublicationElement>(true),
            TransportPendingLookup = SystemAPI.GetBufferLookup<DroneTransportPendingReservationElement>(true),
            ActionLookup = SystemAPI.GetComponentLookup<DroneActionReadyRequest>(true),
            Eligibility = _eligibility,
            Dedicated = SystemAPI.GetComponentLookup<DroneStationStorage>(true),
            Storage = SystemAPI.GetBufferLookup<StorageSlotElement>(true),
            Withdrawals = SystemAPI.GetBuffer<DroneInventoryWithdrawalElement>(_candidates),
            PostTransforms = SystemAPI.GetComponentLookup<PostTransformMatrix>(true),
            LocalToWorld = SystemAPI.GetComponentLookup<LocalToWorld>(true),
            MaximumCharge = SystemAPI.GetSingleton<DroneBatteryConfig>().MaximumCharge,
            MaximumStack = ItemRegistry.GetMaxStack(SystemAPI.GetSingletonBuffer<ItemConfigElement>(true), ItemTypeEnum.Drone),
            DronePrefab = dronePrefab, ECB = owner.CreateCommandBuffer()
        }.Schedule(JobHandle.CombineDependencies(state.Dependency, dependency));
        state.Dependency = JobHandle.CombineDependencies(requests.Dispose(handle), pending.Dispose(handle), actions.Dispose(handle));
        owner.AddJobHandleForProducer(handle);

    }

    /// <summary>소실된 정거장의 재배치 정박만 해제한다. 위치/배터리와 실제 이동의 소유자는 유지한다.</summary>
    [BurstCompile]
    private partial struct ReleaseLostRelocationDockJob : IJobEntity
    {
        [ReadOnly] public EntityStorageInfoLookup Entities;
        public EntityCommandBuffer ECB;

        private void Execute(Entity drone, in DroneDocked dock)
        {
            if (Entities.Exists(dock.Station)) return;
            ECB.RemoveComponent<DroneDocked>(drone);
            ECB.SetComponent(drone, new DroneMovementState());
            ECB.SetComponent(drone, new DroneActivityState { Activity = DroneActivityKind.Idle });
        }
    }
}

internal struct DroneStationAppliedChange
{
    public DroneStationRequest Request;
    public int SlotIndex;
}

[BurstCompile]
internal struct DroneStationLifecycleApplyJob : IJob
{
    [ReadOnly] public NativeArray<Entity> Requests;
    [ReadOnly] public NativeArray<Entity> PendingOwners;
    [ReadOnly] public NativeArray<Entity> Actions;
    [ReadOnly] public ComponentLookup<DroneStationRequest> RequestLookup;
    [ReadOnly] public ComponentLookup<DroneStationDecision> Decisions;
    [ReadOnly] public BufferLookup<DroneTaskPendingPublicationElement> PendingLookup;
    [ReadOnly] public BufferLookup<DroneTransportPendingReservationElement> TransportPendingLookup;
    [ReadOnly] public ComponentLookup<DroneActionReadyRequest> ActionLookup;
    [ReadOnly] public DroneStationEligibility Eligibility;
    [ReadOnly] public ComponentLookup<DroneStationStorage> Dedicated;
    [ReadOnly] public ComponentLookup<PostTransformMatrix> PostTransforms;
    [ReadOnly] public ComponentLookup<LocalToWorld> LocalToWorld;
    [ReadOnly] public BufferLookup<StorageSlotElement> Storage;
    public DynamicBuffer<DroneInventoryWithdrawalElement> Withdrawals;
    public Entity DronePrefab;
    public float MaximumCharge;
    public int MaximumStack;
    public EntityCommandBuffer ECB;

    public void Execute()
    {
        using var records = new NativeList<DroneStationRequestRecord>(Requests.Length, Allocator.Temp);
        DroneStationLifecycleUtility.ReadSortedRequests(Requests, RequestLookup, Decisions, records);
        using var busy = new NativeHashSet<Entity>(16, Allocator.Temp);
        DroneStationLifecycleUtility.GatherBusyWorkers(PendingOwners, PendingLookup, TransportPendingLookup, Actions, ActionLookup, busy);
        using var ranges = new NativeParallelHashMap<Entity, DroneStationSlotRange>(math.max(1, Requests.Length), Allocator.Temp);
        using var slots = new NativeList<DroneStationSlot>(Allocator.Temp);
        using var changes = new NativeList<DroneStationAppliedChange>(Allocator.Temp);
        using var stations = new NativeHashSet<Entity>(16, Allocator.Temp);
        using var removedDocks = new NativeHashSet<Entity>(16, Allocator.Temp);
        foreach (var record in records)
        {
            var request = record.Request;
            var decision = record.Decision;
            var status = decision.Status;
            Entity drone = request.Drone;
            if (decision.Approved)
            {
                bool relocationRequest = decision.IsRelocationRequest;
                status = Eligibility.Validate(request, MaximumCharge, decision.IsAutomaticRequest || relocationRequest, relocationRequest);
                if (status == DroneStationRequestStatus.Applied && busy.Contains(request.Drone))
                {
                    status = DroneStationRequestStatus.Busy;
                }
                if (status == DroneStationRequestStatus.Applied && request.Operation == DroneStationOperation.Store)
                {
                    if (DroneStationLifecycleUtility.ReserveSlot(request, decision.SlotIndex, MaximumStack,
                        Eligibility.Storages, Dedicated, Eligibility.FuelSlots, Storage, ranges, slots, Eligibility.Reserved) < 0)
                    {
                        status = DroneStationRequestStatus.NoSpace;
                    }
                }
                if (status == DroneStationRequestStatus.Applied)
                {
                    if (RecordTransition(request, out drone))
                    {
                        changes.Add(new DroneStationAppliedChange { Request = request, SlotIndex = decision.SlotIndex });
                        stations.Add(request.Station);
                        if (request.Operation is DroneStationOperation.Store or DroneStationOperation.Activate)
                        {
                            removedDocks.Add(request.Drone);
                        }
                    }
                    else
                    {
                        status = request.Operation == DroneStationOperation.ActivateStored
                            ? DroneStationRequestStatus.NoSpace : DroneStationRequestStatus.Rejected;
                    }
                }
            }
            ECB.RemoveComponent<DroneStationRequest>(record.Entity);
            ECB.RemoveComponent<DroneStationDecision>(record.Entity);
            ECB.AddComponent(record.Entity, new DroneStationResult
            {
                Drone = drone, Station = request.Station, SlotIndex = decision.SlotIndex,
                Operation = request.Operation, Status = status
            });
        }
        RecordInventories(stations, changes, removedDocks);
    }

    private bool RecordTransition(in DroneStationRequest request, out Entity drone)
    {
        drone = request.Drone;
        switch (request.Operation)
        {
            case DroneStationOperation.Dock:
                ECB.AddComponent(drone, new DroneDocked { Station = request.Station, SlotIndex = -1 });
                return true;
            case DroneStationOperation.Activate:
                ECB.RemoveComponent<DroneDocked>(drone);
                ECB.SetComponent(drone, new DroneMovementState());
                ECB.SetComponent(drone, new DroneActivityState { Activity = DroneActivityKind.Idle });
                return true;
            case DroneStationOperation.Store:
                ECB.DestroyEntity(drone);
                return true;
            case DroneStationOperation.ActivateStored:
                return TryActivateStored(request, out drone);
            default:
                return false;
        }
    }

    private bool TryActivateStored(in DroneStationRequest request, out Entity drone)
    {
        drone = Entity.Null;
        int slot = request.SourceSlotIndex;
        int budgetIndex = DroneItemTransferUtility.FindWithdrawal(Withdrawals, request.Station, false, slot, ItemTypeEnum.Drone);
        if (budgetIndex < 0 || !Eligibility.Transforms.TryGetComponent(DronePrefab, out var transform)) return false;
        var budget = Withdrawals[budgetIndex];
        if (budget.Remaining <= 0) return false;
        int2 anchor = Eligibility.Positions[request.Station].Value;
        var position = new float3(anchor.x, anchor.y, transform.Position.z);
        if (!DroneSpawnUtility.TryReadGridCell(position, out int2 cell)) return false;
        var current = Storage[request.Station][slot];
        if (current.ItemType != ItemTypeEnum.Drone || current.Quantity <= 0) return false;
        budget.Remaining--;
        Withdrawals[budgetIndex] = budget;

        drone = RuntimePrefabLifecycleUtility.Instantiate(ref ECB, DronePrefab);
        float4x4 post = PostTransforms.TryGetComponent(DronePrefab, out var matrix) ? matrix.Value : float4x4.identity;
        if (request.RelocationTask != Entity.Null)
            ECB.AddComponent(drone, new DroneStationRelocation { Task = request.RelocationTask, MemberIndex = request.RelocationMemberIndex });
        DroneSpawnUtility.RecordInitialState(ref ECB, drone, transform, position, cell, MaximumCharge,
            Eligibility.Positions.HasComponent(DronePrefab), LocalToWorld.HasComponent(DronePrefab), false, post);
        return true;
    }

    private void RecordInventories(NativeHashSet<Entity> stations, NativeList<DroneStationAppliedChange> changes,
        NativeHashSet<Entity> removedDocks)
    {
        foreach (Entity station in stations)
        {
            // 앞선 화물 인계가 반영한 최신 수량을 복사하고 드론 역할 전환과 함께 공개한다.
            var inventory = ECB.SetBuffer<StorageSlotElement>(station);
            inventory.CopyFrom(Storage[station]);
            DynamicBuffer<DroneStationReservedSlotElement> remainingReservations = default;
            foreach (var change in changes)
            {
                if (change.Request.Station != station) continue;
                if (change.Request.Operation == DroneStationOperation.Store)
                {
                    InventorySlotUtility.TryAdd(inventory, change.SlotIndex, ItemTypeEnum.Drone, 1, MaximumStack);
                    if (change.Request.RelocationTask != Entity.Null)
                    {
                        if (!remainingReservations.IsCreated)
                        {
                            // 먼저 정산된 이전 결과와 취소를 보존하고 이번 수납의 예약만 재고와 함께 해제한다.
                            remainingReservations = ECB.SetBuffer<DroneStationReservedSlotElement>(station);
                            remainingReservations.CopyFrom(Eligibility.Reserved[station]);
                        }
                        DroneStationRelocationUtility.ReleaseSlots(remainingReservations, change.Request.RelocationTask,
                            change.Request.RelocationMemberIndex);
                    }
                }
                else if (change.Request.Operation == DroneStationOperation.ActivateStored)
                    InventorySlotUtility.Take(inventory, change.SlotIndex, ItemTypeEnum.Drone, 1);
            }
            var docked = ECB.SetBuffer<DockedDroneElement>(station);
            foreach (var dock in Eligibility.Docked[station])
            {
                if (!removedDocks.Contains(dock.Drone))
                {
                    docked.Add(dock);
                }
            }
            foreach (var change in changes)
            {
                if (change.Request.Station == station && change.Request.Operation == DroneStationOperation.Dock)
                {
                    docked.Add(new DockedDroneElement { Drone = change.Request.Drone, SlotIndex = -1 });
                }
            }
        }
    }
}
