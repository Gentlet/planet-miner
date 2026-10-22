using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

/// <summary>최종 데이터 처리는 Burst Job, RenderMeshArray 기록은 완료된 승인 목록을 읽는 관리형 경계다.</summary>
[UpdateInGroup(typeof(DroneStateApplyGroup), OrderLast = true)]
[UpdateAfter(typeof(DroneTaskAssignmentPublishSystem))]
public partial struct DroneStationLifecycleApplySystem : ISystem
{
    private EntityQuery _requests;
    private EntityQuery _pending;
    private EntityQuery _actions;
    private EntityQuery _droneDatabase;
    private EntityQuery _itemDatabase;
    private EntityQueryMask _stationMask;
    private EntityQueryMask _itemMask;
    private EntityQueryMask _workerMask;

    public void OnCreate(ref SystemState state)
    {
        _requests = state.GetEntityQuery(ComponentType.ReadOnly<DroneStationRequest>(), ComponentType.ReadOnly<DroneStationDecision>());
        _pending = state.GetEntityQuery(ComponentType.ReadOnly<DroneTaskPendingPublicationElement>());
        _actions = state.GetEntityQuery(ComponentType.ReadOnly<DroneActionReadyRequest>());
        _droneDatabase = state.GetEntityQuery(ComponentType.ReadOnly<DronePrefabDatabase>(), ComponentType.ReadOnly<DronePrefab>());
        _itemDatabase = state.GetEntityQuery(ComponentType.ReadOnly<ItemPrefabDatabase>(), ComponentType.ReadOnly<ItemPrefabElement>());
        DroneStationEligibility.CreateMasks(ref state, out _stationMask, out _itemMask, out _workerMask);
        state.RequireForUpdate(_requests);
        state.RequireForUpdate<PrefabDatabaseReady>();
        state.RequireForUpdate<DroneBatteryConfig>();
        state.RequireForUpdate<ItemRegistry>();
    }

    public void OnUpdate(ref SystemState state)
    {
        var owner = state.World.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>();
        Entity dronePrefab = _droneDatabase.GetSingleton<DronePrefab>().Prefab;
        var items = SystemAPI.GetBufferLookup<ItemPrefabElement>(true)[_itemDatabase.GetSingletonEntity()];
        PrefabLookupUtility.TryGetItemPrefab(items, ItemTypeEnum.Drone, out Entity itemPrefab);
        using var requests = _requests.ToEntityListAsync(Allocator.TempJob, state.Dependency, out var requestsReady);
        using var pending = _pending.ToEntityListAsync(Allocator.TempJob, state.Dependency, out var pendingReady);
        using var actions = _actions.ToEntityListAsync(Allocator.TempJob, state.Dependency, out var actionsReady);
        using var visuals = new NativeList<DroneStationVisualChange>(Allocator.TempJob);
        var dependency = JobHandle.CombineDependencies(requestsReady, pendingReady, actionsReady);
        var handle = new DroneStationLifecycleApplyJob
        {
            Requests = requests.AsDeferredJobArray(), PendingOwners = pending.AsDeferredJobArray(), Actions = actions.AsDeferredJobArray(),
            RequestLookup = state.GetComponentLookup<DroneStationRequest>(true),
            Decisions = state.GetComponentLookup<DroneStationDecision>(true),
            PendingLookup = state.GetBufferLookup<DroneTaskPendingPublicationElement>(true),
            ActionLookup = state.GetComponentLookup<DroneActionReadyRequest>(true),
            Eligibility = DroneStationEligibility.Create(ref state, _stationMask, _itemMask, _workerMask),
            Dedicated = state.GetComponentLookup<DroneStationStorage>(true),
            PostTransforms = state.GetComponentLookup<PostTransformMatrix>(true),
            Meshes = state.GetComponentLookup<MaterialMeshInfo>(true), Bounds = state.GetComponentLookup<RenderBounds>(true),
            MaximumCharge = SystemAPI.GetSingleton<DroneBatteryConfig>().MaximumCharge,
            MaximumStack = ItemRegistry.GetMaxStack(SystemAPI.GetSingletonBuffer<ItemConfigElement>(true), ItemTypeEnum.Drone),
            DronePrefab = dronePrefab, ItemPrefab = itemPrefab, ECB = owner.CreateCommandBuffer(), Visuals = visuals
        }.Schedule(JobHandle.CombineDependencies(state.Dependency, dependency));
        state.Dependency = handle;
        owner.AddJobHandleForProducer(handle);

        // 관리형 shared component는 Job에서 읽거나 기록하지 않는다. 승인 목록을 읽는 이 지점에서만 완료를 기다린다.
        handle.Complete();
        DroneStationLifecycleUtility.RecordManagedAppearance(state.EntityManager, owner.CreateCommandBuffer(), visuals.AsArray());
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
    [ReadOnly] public ComponentLookup<DroneActionReadyRequest> ActionLookup;
    [ReadOnly] public DroneStationEligibility Eligibility;
    [ReadOnly] public ComponentLookup<DroneStationStorage> Dedicated;
    [ReadOnly] public ComponentLookup<PostTransformMatrix> PostTransforms;
    [ReadOnly] public ComponentLookup<MaterialMeshInfo> Meshes;
    [ReadOnly] public ComponentLookup<RenderBounds> Bounds;
    public Entity DronePrefab;
    public Entity ItemPrefab;
    public float MaximumCharge;
    public int MaximumStack;
    public EntityCommandBuffer ECB;
    public NativeList<DroneStationVisualChange> Visuals;

    public void Execute()
    {
        using var records = new NativeList<DroneStationRequestRecord>(Requests.Length, Allocator.Temp);
        DroneStationLifecycleUtility.ReadSortedRequests(Requests, RequestLookup, Decisions, records);
        using var busy = new NativeHashSet<Entity>(16, Allocator.Temp);
        DroneStationLifecycleUtility.GatherBusyWorkers(PendingOwners, PendingLookup, Actions, ActionLookup, busy);
        using var ranges = new NativeParallelHashMap<Entity, DroneStationSlotRange>(math.max(1, Requests.Length), Allocator.Temp);
        using var slots = new NativeList<DroneStationSlot>(Allocator.Temp);
        using var changes = new NativeList<DroneStationAppliedChange>(Allocator.Temp);
        using var stations = new NativeHashSet<Entity>(16, Allocator.Temp);
        using var removedItems = new NativeHashSet<Entity>(16, Allocator.Temp);
        using var removedDocks = new NativeHashSet<Entity>(16, Allocator.Temp);
        foreach (var record in records)
        {
            var request = record.Request;
            var decision = record.Decision;
            var status = decision.Status;
            if (decision.Approved)
            {
                status = Eligibility.Validate(request, MaximumCharge);
                if (status == DroneStationRequestStatus.Applied && busy.Contains(request.Drone))
                {
                    status = DroneStationRequestStatus.Busy;
                }
                if (status == DroneStationRequestStatus.Applied && request.Operation != DroneStationOperation.Activate)
                {
                    if (DroneStationLifecycleUtility.ReserveSlot(request.Station, request.Drone, request.Operation,
                        decision.SlotIndex, MaximumStack, Eligibility.Storages, Dedicated, Eligibility.Items, Eligibility.Docked, ranges, slots) < 0)
                    {
                        status = DroneStationRequestStatus.NoSpace;
                    }
                }
                if (status == DroneStationRequestStatus.Applied)
                {
                    if (RecordTransition(request, decision.SlotIndex, out bool activatedItem))
                    {
                        changes.Add(new DroneStationAppliedChange { Request = request, SlotIndex = decision.SlotIndex });
                        stations.Add(request.Station);
                        if (activatedItem)
                        {
                            removedItems.Add(request.Drone);
                        }
                        if (request.Operation != DroneStationOperation.Dock)
                        {
                            removedDocks.Add(request.Drone);
                        }
                    }
                    else
                    {
                        status = DroneStationRequestStatus.Rejected;
                    }
                }
            }
            ECB.RemoveComponent<DroneStationRequest>(record.Entity);
            ECB.RemoveComponent<DroneStationDecision>(record.Entity);
            ECB.AddComponent(record.Entity, new DroneStationResult { Drone = request.Drone, Operation = request.Operation, Status = status });
        }
        RecordInventories(stations, changes, removedItems, removedDocks);
    }

    private bool RecordTransition(in DroneStationRequest request, int slot, out bool activatedItem)
    {
        activatedItem = false;
        Entity drone = request.Drone;
        if (request.Operation == DroneStationOperation.Dock)
        {
            ECB.AddComponent(drone, new DroneDocked { Station = request.Station, SlotIndex = slot });
            return true;
        }
        if (request.Operation == DroneStationOperation.Activate)
        {
            if (Eligibility.ItemTypes.HasComponent(drone))
            {
                int2 anchor = Eligibility.Positions[request.Station].Value;
                float3 position = new float3(anchor.x, anchor.y, Eligibility.Transforms[DronePrefab].Position.z);
                if (!DroneSpawnUtility.TryReadGridCell(position, out int2 cell))
                {
                    return false;
                }
                bool hasBattery = Eligibility.Batteries.HasComponent(drone);
                float charge = hasBattery ? Eligibility.Batteries[drone].CurrentCharge : MaximumCharge;
                DroneSpawnUtility.RecordWorkerRole(ref ECB, drone, position, charge, hasBattery);
                ECB.AddComponent(drone, new GridPosition(cell));
                RecordNativeAppearance(drone, DronePrefab, position);
                activatedItem = true;
                ECB.RemoveComponent<ItemIdentity>(drone);
                ECB.RemoveComponent<ItemOwnership>(drone);
                ECB.RemoveComponent<DestroyItemRequest>(drone);
                ECB.RemoveComponent<TransferOwnershipRequest>(drone);
                ECB.RemoveComponent<BeltMovementState>(drone);
                ECB.RemoveComponent<BeltMovementDecision>(drone);
                ECB.RemoveComponent<BuildingItemInputDecision>(drone);
                ECB.RemoveComponent<DroneRecoveryPending>(drone);
            }
            ECB.RemoveComponent<DroneDocked>(drone);
            ECB.RemoveComponent<DisableRendering>(drone);
            return true;
        }
        int2 stationPosition = Eligibility.Positions[request.Station].Value;
        float3 itemPosition = new float3(stationPosition.x, stationPosition.y, Eligibility.Transforms[ItemPrefab].Position.z);
        ECB.RemoveComponent<DroneDocked>(drone);
        ECB.RemoveComponent<DroneWorker>(drone);
        ECB.RemoveComponent<DroneWorkerObservation>(drone);
        ECB.RemoveComponent<DroneWorkerAssignment>(drone);
        ECB.RemoveComponent<DroneCargoState>(drone);
        ECB.RemoveComponent<DroneActivityState>(drone);
        ECB.RemoveComponent<StoredItemElement>(drone);
        ItemLifecycleUtility.InitializeItemRole(ref ECB, drone, ItemTypeEnum.Drone, stationPosition, itemPosition, ItemOwnership.Stored(request.Station));
        RecordNativeAppearance(drone, ItemPrefab, itemPosition);
        return true;
    }

    private void RecordNativeAppearance(Entity actor, Entity prefab, float3 position)
    {
        var transform = Eligibility.Transforms[prefab];
        transform.Position = position;
        ECB.AddComponent(actor, transform);
        float4x4 post = float4x4.identity;
        if (PostTransforms.HasComponent(prefab))
        {
            var value = PostTransforms[prefab];
            post = value.Value;
            ECB.AddComponent(actor, value);
        }
        else
        {
            ECB.RemoveComponent<PostTransformMatrix>(actor);
        }
        ECB.AddComponent(actor, new LocalToWorld { Value = math.mul(transform.ToMatrix(), post) });
        if (Meshes.HasComponent(prefab))
        {
            ECB.AddComponent(actor, Meshes[prefab]);
            ECB.SetComponentEnabled<MaterialMeshInfo>(actor, true);
        }
        if (Bounds.HasComponent(prefab))
        {
            ECB.AddComponent(actor, Bounds[prefab]);
        }
        Visuals.Add(new DroneStationVisualChange { Actor = actor, Prefab = prefab });
    }

    private void RecordInventories(NativeHashSet<Entity> stations, NativeList<DroneStationAppliedChange> changes,
        NativeHashSet<Entity> removedItems, NativeHashSet<Entity> removedDocks)
    {
        foreach (Entity station in stations)
        {
            var items = ECB.SetBuffer<StoredItemElement>(station);
            foreach (var item in Eligibility.Items[station])
            {
                if (!removedItems.Contains(item.ItemEntity))
                {
                    items.Add(item);
                }
            }
            foreach (var change in changes)
            {
                if (change.Request.Station == station && change.Request.Operation == DroneStationOperation.Store)
                {
                    items.Add(new StoredItemElement(change.Request.Drone, ItemTypeEnum.Drone, change.SlotIndex));
                }
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
                    docked.Add(new DockedDroneElement { Drone = change.Request.Drone, SlotIndex = change.SlotIndex });
                }
            }
        }
    }
}