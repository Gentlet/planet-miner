using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

/// <summary>
/// Command가 완공 건물 철거를 승인한다. 일반 건물은 즉시 중단하고 정거장은 충전을 유지하는 준비 상태로 시작한다.
/// 드론 확인 전에는 건물을 보존하며 취소된 root의 중단 상태도 이 소유자가 다음 EndCommand에 해제한다.
/// 외부 DemolishBuildingRequest는 여기서 수명을 끝내고 기존 Task 접수 API로 드론 작업을 요청한다.
/// </summary>
[UpdateInGroup(typeof(CommandGroup))]
public partial struct BuildingDemolitionCommandSystem : ISystem
{
    private EntityQuery _requests;
    private EntityQuery _pending;
    private EntityQuery _preparing;
    private EntityQuery _ownedRequests;
    private EntityQuery _targetsWithGeometry;
    private EntityQuery _stationTargets;

    public void OnCreate(ref SystemState state)
    {
        _requests = SystemAPI.QueryBuilder().WithAny<DemolishBuildingRequest, DroneTaskRequest>().Build();
        _pending = SystemAPI.QueryBuilder().WithAll<PendingBuildingDemolition>()
            .WithOptions(EntityQueryOptions.IncludeDisabledEntities).Build();
        _ownedRequests = state.GetEntityQuery(typeof(BuildingDemolitionAutomaticTaskRequest));
        _preparing = state.GetEntityQuery(new EntityQueryDesc
        {
            All = new[] { ComponentType.ReadOnly<PreparingStationDemolition>() },
            Options = EntityQueryOptions.IncludeDisabledEntities
        });
        _stationTargets = state.GetEntityQuery(new EntityQueryDesc
        {
            All = new[] { ComponentType.ReadOnly<DroneStation>(), ComponentType.ReadOnly<DroneStationStorage>(),
                ComponentType.ReadOnly<Storage>(), ComponentType.ReadOnly<StoredItemElement>(),
                ComponentType.ReadOnly<DockedDroneElement>(), ComponentType.ReadOnly<DroneStationNetworkDecision>(),
                ComponentType.ReadOnly<DroneStationReservedSlotElement>() },
            Options = EntityQueryOptions.IncludeDisabledEntities
        });
        _targetsWithGeometry = state.GetEntityQuery(new EntityQueryDesc
        {
            All = new[] { ComponentType.ReadOnly<GridPosition>(), ComponentType.ReadOnly<BuildingFootprint>(),
                ComponentType.ReadOnly<Direction>() },
            Options = EntityQueryOptions.IncludeDisabledEntities
        });
    }

    public void OnUpdate(ref SystemState state)
    {
        using (var owned = _ownedRequests.ToEntityArray(Allocator.Temp))
            foreach (Entity request in owned) DroneTaskRequestUtility.TryConsumeResult(state.EntityManager, request, out _);
        if (_requests.IsEmptyIgnoreFilter && _pending.IsEmptyIgnoreFilter && _preparing.IsEmptyIgnoreFilter) return;
        var end = state.World.GetOrCreateSystemManaged<EndCommandEntityCommandBufferSystem>();
        var ecb = end.CreateCommandBuffer();
        using var released = new NativeParallelHashSet<Entity>(System.Math.Max(1, _pending.CalculateEntityCount() + _preparing.CalculateEntityCount()), Allocator.TempJob);
        using var accepted = new NativeParallelHashSet<Entity>(System.Math.Max(1, _requests.CalculateEntityCount()), Allocator.TempJob);
        using var legacyTargets = new NativeQueue<Entity>(Allocator.TempJob);
        using var legacySequenceBudget = new NativeReference<ulong>(
            DroneTaskRequestUtility.ReadAvailableRequestSequenceCount(state.EntityManager), Allocator.TempJob);
        var release = new ReleaseEndedBuildingDemolitionJob
        {
            Tasks = state.GetComponentLookup<DroneLogisticsTask>(true),
            Confirmations = state.GetComponentLookup<BuildingDemolitionExecutionRequest>(true),
            Released = released, ECB = ecb
        }.Schedule(_pending, state.Dependency);
        release = new ReleaseEndedStationDemolitionJob
        {
            Tasks = state.GetComponentLookup<DroneLogisticsTask>(true),
            Confirmations = state.GetComponentLookup<BuildingDemolitionExecutionRequest>(true),
            Released = released, ECB = ecb
        }.Schedule(_preparing, release);
        var handle = new ValidateBuildingDemolitionJob
        {
            ECB = ecb,
            Legacy = state.GetComponentLookup<DemolishBuildingRequest>(true),
            Requests = state.GetComponentLookup<DroneTaskRequest>(true),
            Buildings = state.GetComponentLookup<BuildingType>(true),
            Indestructible = state.GetComponentLookup<IndestructibleBuilding>(true),
            Prefabs = state.GetComponentLookup<Prefab>(true),
            Pending = state.GetComponentLookup<PendingBuildingDemolition>(true),
            Preparing = state.GetComponentLookup<PreparingStationDemolition>(true),
            Transfers = state.GetComponentLookup<TransferOwnershipRequest>(),
            Stored = state.GetBufferLookup<StoredItemElement>(true),
            Products = state.GetBufferLookup<ProductItemElement>(true),
            Results = state.GetBufferLookup<ProductResult>(),
            TargetShape = _targetsWithGeometry.GetEntityQueryMask(),
            StationShape = _stationTargets.GetEntityQueryMask(),
            Released = released, Accepted = accepted, LegacyTargets = legacyTargets, LegacySequenceBudget = legacySequenceBudget
        }.Schedule(_requests, release);
        // Burst가 승인한 레거시 입력만 기존 메인 스레드 접수 API에 넘겨 World 순번의 단일 발급자를 유지한다.
        handle.Complete();
        while (legacyTargets.TryDequeue(out Entity target))
        {
            Entity request = DroneTaskRequestUtility.SubmitCreate(state.EntityManager, DroneLogisticsTaskKindEnum.BuildingDemolition,
                target, ItemTypeEnum.None);
            state.EntityManager.AddComponent<BuildingDemolitionAutomaticTaskRequest>(request);
        }
        state.Dependency = handle;
        end.AddJobHandleForProducer(handle);
    }
}

[BurstCompile]
public partial struct ReleaseEndedBuildingDemolitionJob : IJobEntity
{
    [ReadOnly] public ComponentLookup<DroneLogisticsTask> Tasks;
    [ReadOnly] public ComponentLookup<BuildingDemolitionExecutionRequest> Confirmations;
    public NativeParallelHashSet<Entity> Released;
    public EntityCommandBuffer ECB;

    public void Execute(Entity entity, in PendingBuildingDemolition pending)
    {
        if (pending.Task == Entity.Null || Confirmations.HasComponent(pending.Task)) return;
        if (Tasks.HasComponent(pending.Task))
        {
            var state = Tasks[pending.Task].State;
            if (state != DroneLogisticsTaskStateEnum.Cancelled && state != DroneLogisticsTaskStateEnum.Closed) return;
        }
        Released.Add(entity);
        ECB.RemoveComponent<PendingBuildingDemolition>(entity);
    }
}

[BurstCompile]
public partial struct ValidateBuildingDemolitionJob : IJobEntity
{
    public EntityQueryMask TargetShape;
    public EntityQueryMask StationShape;
    [ReadOnly] public ComponentLookup<DemolishBuildingRequest> Legacy;
    [ReadOnly] public ComponentLookup<DroneTaskRequest> Requests;
    [ReadOnly] public ComponentLookup<BuildingType> Buildings;
    [ReadOnly] public ComponentLookup<IndestructibleBuilding> Indestructible;
    [ReadOnly] public ComponentLookup<Prefab> Prefabs;
    [ReadOnly] public ComponentLookup<PendingBuildingDemolition> Pending;
    [ReadOnly] public ComponentLookup<PreparingStationDemolition> Preparing;
    public ComponentLookup<TransferOwnershipRequest> Transfers;
    [ReadOnly] public BufferLookup<StoredItemElement> Stored;
    [ReadOnly] public BufferLookup<ProductItemElement> Products;
    public BufferLookup<ProductResult> Results;
    [ReadOnly] public NativeParallelHashSet<Entity> Released;
    public NativeParallelHashSet<Entity> Accepted;
    public NativeQueue<Entity> LegacyTargets;
    public NativeReference<ulong> LegacySequenceBudget;
    public EntityCommandBuffer ECB;

    public void Execute(Entity entity)
    {
        bool legacy = Legacy.HasComponent(entity);
        Entity target;
        if (legacy)
        {
            target = Legacy[entity].TargetBuilding;
            ECB.DestroyEntity(entity);
        }
        else
        {
            var request = Requests[entity];
            if (request.Kind != DroneLogisticsTaskKindEnum.BuildingDemolition ||
                request.Operation != DroneTaskRequestOperationEnum.Create || request.ItemType != ItemTypeEnum.None ||
                request.Quantity != 0) return;
            target = request.Target;
        }
        if (target == Entity.Null || !Buildings.HasComponent(target)) return;
        if (!TargetShape.MatchesIgnoreFilter(target)) return;
        if (Prefabs.HasComponent(target) || Indestructible.HasComponent(target)) return;
        var type = Buildings[target].Type;
        if (type == BuildingTypeEnum.None || type == BuildingTypeEnum.ConstructionSite || type == BuildingTypeEnum.MainFacility) return;
        if (type == BuildingTypeEnum.DroneStation && !StationShape.MatchesIgnoreFilter(target)) return;
        if (Pending.HasComponent(target) && !Released.Contains(target)) return;
        if (Preparing.HasComponent(target) && !Released.Contains(target)) return;
        // 이 단일 Command에서 접수하기 전까지 다른 Submit은 실행되지 않는다. 발급 불가한 레거시 입력은 중단 전에 거부한다.
        if (legacy && LegacySequenceBudget.Value == 0) return;
        if (!Accepted.Add(target)) return;
        if (legacy) LegacySequenceBudget.Value--;
        if (type == BuildingTypeEnum.DroneStation)
        {
            ECB.AddComponent<PreparingStationDemolition>(target);
            if (legacy) LegacyTargets.Enqueue(target);
            return;
        }
        if (Stored.HasBuffer(target))
            foreach (var item in Stored[target]) CancelTransfer(item.ItemEntity);
        if (Products.HasBuffer(target))
            foreach (var item in Products[target]) CancelTransfer(item.ItemEntity);
        if (Results.HasBuffer(target)) Results[target].Clear();
        ECB.AddComponent<PendingBuildingDemolition>(target);
        if (legacy) LegacyTargets.Enqueue(target);
    }

    private void CancelTransfer(Entity item)
    {
        if (Transfers.HasComponent(item) && Transfers.IsComponentEnabled(item))
            Transfers.SetComponentEnabled(item, false);
    }
}

[BurstCompile]
public partial struct ReleaseEndedStationDemolitionJob : IJobEntity
{
    [ReadOnly] public ComponentLookup<DroneLogisticsTask> Tasks;
    [ReadOnly] public ComponentLookup<BuildingDemolitionExecutionRequest> Confirmations;
    public NativeParallelHashSet<Entity> Released;
    public EntityCommandBuffer ECB;

    public void Execute(Entity entity, in PreparingStationDemolition preparing)
    {
        if (preparing.Task == Entity.Null || Confirmations.HasComponent(preparing.Task)) return;
        if (Tasks.HasComponent(preparing.Task))
        {
            var state = Tasks[preparing.Task].State;
            if (state != DroneLogisticsTaskStateEnum.Cancelled && state != DroneLogisticsTaskStateEnum.Closed) return;
        }
        Released.Add(entity);
        ECB.RemoveComponent<PreparingStationDemolition>(entity);
    }
}
