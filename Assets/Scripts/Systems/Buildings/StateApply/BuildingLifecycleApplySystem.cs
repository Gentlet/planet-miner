using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Rendering;

/// <summary>Building StateApply에서 완공 건물의 생성과 검증된 철거를 EndBuilding에 공개한다.</summary>
[UpdateInGroup(typeof(BuildingStateApplyGroup))]
[UpdateAfter(typeof(RoutingApplySystem))]
[UpdateAfter(typeof(BuildingItemStorageApplySystem))]
public partial struct BuildingLifecycleApplySystem : ISystem
{
    private EntityQuery _demolitionRequests;
    private EntityQuery _assignments;
    private EntityQuery _spawnQuery;
    private EntityQuery _beltItems;
    private EntityQuery _buildingPrefabDbQuery;
    private EntityQuery _buildingConfigQuery;
    private EntityQuery _registry;
    private EntityQuery _capacity;

    public void OnCreate(ref SystemState state)
    {
        _demolitionRequests = state.GetEntityQuery(typeof(BuildingDemolitionExecutionRequest), typeof(DroneLogisticsTask));
        _assignments = state.GetEntityQuery(ComponentType.ReadOnly<DroneTaskAssignment>());
        _spawnQuery = SystemAPI.QueryBuilder().WithAll<SpawnBuildingRequest>().Build();
        _beltItems = SystemAPI.QueryBuilder().WithAll<BeltMovementState, ItemIdentity, GridPosition>()
            .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).Build();
        _buildingPrefabDbQuery = SystemAPI.QueryBuilder().WithAll<BuildingPrefabElement>().Build();
        _buildingConfigQuery = SystemAPI.QueryBuilder().WithAll<BuildingConfig, BuildingConfigElement>().Build();
        _registry = state.GetEntityQuery(ComponentType.ReadOnly<ItemRegistry>(), ComponentType.ReadOnly<ItemConfigElement>());
        _capacity = state.GetEntityQuery(ComponentType.ReadOnly<DroneCapacityState>());
    }

    public void OnUpdate(ref SystemState state)
    {
        bool demolish = !_demolitionRequests.IsEmptyIgnoreFilter;
        bool spawn = !_spawnQuery.IsEmptyIgnoreFilter;
        if (!demolish && !spawn) return;

        state.CompleteDependency();
        var ecbSystem = state.World.GetOrCreateSystemManaged<EndBuildingEntityCommandBufferSystem>();
        var ecb = ecbSystem.CreateCommandBuffer();
        Entity config = _buildingConfigQuery.IsEmptyIgnoreFilter ? Entity.Null : _buildingConfigQuery.GetSingletonEntity();
        if (demolish) RecordDemolitions(state.EntityManager, config, ecb);
        if (!spawn) return;

        BuildingPlacementRequestUtility.PrepareSpawnStamps(state.EntityManager, _spawnQuery);
        var spawnJob = new SpawnBuildingApplyJob
        {
            EntityHandle = SystemAPI.GetEntityTypeHandle(),
            SpawnBuildingRequestHandle = SystemAPI.GetComponentTypeHandle<SpawnBuildingRequest>(true),
            ECB = ecb,
            PrefabDbEntity = _buildingPrefabDbQuery.IsEmptyIgnoreFilter ? Entity.Null : _buildingPrefabDbQuery.GetSingletonEntity(),
            ConfigEntity = config,
            PrefabBufferLookup = SystemAPI.GetBufferLookup<BuildingPrefabElement>(true),
            ConfigBufferLookup = SystemAPI.GetBufferLookup<BuildingConfigElement>(true),
            PrefabPostTransforms = SystemAPI.GetComponentLookup<PostTransformMatrix>(true)
        };
        state.Dependency = spawnJob.Schedule(_spawnQuery, state.Dependency);
        ecbSystem.AddJobHandleForProducer(state.Dependency);
    }

    private void RecordDemolitions(EntityManager manager, Entity config, EntityCommandBuffer ecb)
    {
        using var requests = _demolitionRequests.ToEntityArray(Allocator.Temp);
        using var assignments = _assignments.ToEntityArray(Allocator.Temp);
        Entity registry = _registry.IsEmptyIgnoreFilter ? Entity.Null : _registry.GetSingletonEntity();
        int capacity = DroneSchedulingUtility.ReadCarryingCapacity(_capacity);
        foreach (Entity root in requests)
        {
            var task = manager.GetComponentData<DroneLogisticsTask>(root);
            Entity target = task.Target;
            BuildingDemolitionResultStatus result;
            if (!DroneDemolitionUtility.IsTarget(manager, target) ||
                !manager.HasComponent<PendingBuildingDemolition>(target) ||
                manager.GetComponentData<PendingBuildingDemolition>(target).Task != root)
                result = BuildingDemolitionResultStatus.Failed;
            else if (task.State != DroneLogisticsTaskStateEnum.ExecutionConfirmed ||
                !DroneDemolitionUtility.CanDrain(manager, task) ||
                DroneDemolitionUtility.CountInventory(manager, target) != 0 || HasBeltItems(manager, target) ||
                !DroneDemolitionUtility.CanConfirmDemolitionCrew(manager, root, task, assignments, config, registry, capacity))
                result = BuildingDemolitionResultStatus.Retry;
            else
            {
                // 삭제된 건물 대신 root가 수량과 인계 위치를 보유한다. 드론 Lifecycle이 같은 틱에 화물로 정산한다.
                using var costs = DroneDemolitionUtility.ReadRefunds(manager, root, task, config);
                var refund = ecb.SetBuffer<DroneDemolitionRefundElement>(root);
                foreach (var material in costs) refund.Add(material);
                ecb.AddComponent(root, manager.GetComponentData<GridPosition>(target));
                ecb.AddComponent(root, manager.GetComponentData<BuildingFootprint>(target));
                ecb.AddComponent(root, manager.GetComponentData<Direction>(target));
                ecb.DestroyEntity(target);
                result = BuildingDemolitionResultStatus.Completed;
            }
            ecb.AddComponent(root, new BuildingDemolitionResult { Status = result });
            ecb.RemoveComponent<BuildingDemolitionExecutionRequest>(root);
        }
    }

    private bool HasBeltItems(EntityManager manager, Entity building)
    {
        if (!manager.HasComponent<BeltComponent>(building)) return false;
        using var items = _beltItems.ToEntityArray(Allocator.Temp);
        foreach (Entity item in items)
            if (DroneDemolitionUtility.IsStoppedBeltItem(manager, building, item)) return true;
        return false;
    }
}

/// <summary>
/// SpawnBuildingRequest를 소비하여 완공 건물 엔티티를 생성하고 컴포넌트를 초기화하는 단일 워커 Burst Job.
/// </summary>
[BurstCompile]
public partial struct SpawnBuildingApplyJob : IJobChunk
{
    [ReadOnly] public EntityTypeHandle EntityHandle;
    [ReadOnly] public ComponentTypeHandle<SpawnBuildingRequest> SpawnBuildingRequestHandle;

    private struct RegistryReadContext
    {
        public BuildingLifecycleUtility.SpawnRegistryReadContext Spawn;
    }

    public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
    {
        var entities = chunk.GetNativeArray(EntityHandle);
        var spawnBuildingRequestValues = chunk.GetNativeArray(ref SpawnBuildingRequestHandle);
        var registryRead = new RegistryReadContext();
        var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
        while (enumerator.NextEntityIndex(out int index))
        {
            var requestValue = spawnBuildingRequestValues[index];
            BuildingLifecycleUtility.SpawnBuilding(
                ref ECB,
                requestValue.BuildingId,
                requestValue.Position,
                requestValue.Direction,
                requestValue.FootprintSize,
                requestValue.Stamp,
                PrefabDbEntity,
                ConfigEntity,
                in PrefabBufferLookup,
                in ConfigBufferLookup,
                in PrefabPostTransforms,
                ref registryRead.Spawn
            );

            // 요청 엔티티 소비 (Consume-on-Apply)
            ECB.DestroyEntity(entities[index]);
        }
    }

    public EntityCommandBuffer ECB;
    public Entity PrefabDbEntity;
    public Entity ConfigEntity;
    [ReadOnly] public ComponentLookup<PostTransformMatrix> PrefabPostTransforms;

    [ReadOnly]
    public BufferLookup<BuildingPrefabElement> PrefabBufferLookup;

    [ReadOnly]
    public BufferLookup<BuildingConfigElement> ConfigBufferLookup;
}

/// <summary>확인 뒤 대상이 소실된 root에 실패를 반환해 결과 대기가 남지 않게 한다.</summary>
