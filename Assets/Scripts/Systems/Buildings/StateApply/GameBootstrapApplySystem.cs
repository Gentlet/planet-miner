using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

/// <summary>
/// 완공 건물/시작 실물의 공통 생성 API를 사용하며, 검증이 끝난 전체 지급만 같은 EndBuilding에 기록한다.
/// 건물/현장과 아직 공개되지 않은 직접 Spawn도 검사한다. 자원과 기존 실물은 변경하지 않는다.
/// </summary>
[UpdateInGroup(typeof(BuildingStateApplyGroup))]
[UpdateAfter(typeof(BuildingLifecycleApplySystem))]
[UpdateAfter(typeof(ItemOwnershipApplySystem))]
public partial struct GameBootstrapApplySystem : ISystem
{
    private EntityQuery _requests;
    private EntityQuery _records;
    private EntityQuery _buildings;
    private EntityQuery _spawns;
    private EntityQuery _itemsWithPositions;

    public void OnCreate(ref SystemState state)
    {
        _requests = state.GetEntityQuery(typeof(GameBootstrapRequest));
        _records = SystemAPI.QueryBuilder().WithAll<GameBootstrapState>()
            .WithOptions(EntityQueryOptions.IncludeDisabledEntities).Build();
        _buildings = SystemAPI.QueryBuilder().WithAll<BuildingType>()
            .WithOptions(EntityQueryOptions.IncludeDisabledEntities).Build();
        _spawns = state.GetEntityQuery(typeof(SpawnBuildingRequest));
        _itemsWithPositions = state.GetEntityQuery(ComponentType.ReadOnly<ItemIdentity>(),
            ComponentType.ReadOnly<ItemOwnership>(), ComponentType.ReadOnly<GridPosition>());
        state.RequireForUpdate<GameBootstrapRequest>();
        state.RequireForUpdate<PrefabDatabaseReady>();
        state.RequireForUpdate<StartingItemConfig>();
        state.RequireForUpdate<BuildingConfig>();
        state.RequireForUpdate<ItemRegistry>();
        state.RequireForUpdate<BuildingPrefabDatabase>();
        state.RequireForUpdate<ItemPrefabDatabase>();
    }

    public void OnUpdate(ref SystemState state)
    {
        var requests = _requests.ToEntityListAsync(Allocator.TempJob, state.Dependency, out var requestsReady);
        var records = _records.ToEntityListAsync(Allocator.TempJob, state.Dependency, out var recordsReady);
        var buildings = _buildings.ToEntityListAsync(Allocator.TempJob, state.Dependency, out var buildingsReady);
        var spawns = _spawns.ToEntityListAsync(Allocator.TempJob, state.Dependency, out var spawnsReady);
        var itemEntities = _itemsWithPositions.ToEntityListAsync(Allocator.TempJob, state.Dependency, out var itemsReady);
        var ready = JobHandle.CombineDependencies(JobHandle.CombineDependencies(requestsReady, recordsReady),
            JobHandle.CombineDependencies(buildingsReady, spawnsReady));
        ready = JobHandle.CombineDependencies(ready, itemsReady);
        var end = state.World.GetOrCreateSystemManaged<EndBuildingEntityCommandBufferSystem>();
        var apply = new ApplyGameBootstrapJob
        {
            Requests = requests.AsDeferredJobArray(), Records = records.AsDeferredJobArray(),
            Buildings = buildings.AsDeferredJobArray(), Spawns = spawns.AsDeferredJobArray(),
            ItemEntities = itemEntities.AsDeferredJobArray(),
            RequestData = SystemAPI.GetComponentLookup<GameBootstrapRequest>(true),
            RecordData = SystemAPI.GetComponentLookup<GameBootstrapState>(true),
            BuildingTypes = SystemAPI.GetComponentLookup<BuildingType>(true),
            Positions = SystemAPI.GetComponentLookup<GridPosition>(true),
            Footprints = SystemAPI.GetComponentLookup<BuildingFootprint>(true),
            Directions = SystemAPI.GetComponentLookup<Direction>(true),
            SpawnData = SystemAPI.GetComponentLookup<SpawnBuildingRequest>(true),
            Sites = SystemAPI.GetComponentLookup<ConstructionSite>(true),
            DisabledEntities = SystemAPI.GetComponentLookup<Disabled>(true),
            ItemOwners = SystemAPI.GetComponentLookup<ItemOwnership>(true),
            DestroyRequests = SystemAPI.GetComponentLookup<DestroyItemRequest>(true),
            Requirements = SystemAPI.GetBufferLookup<ConstructionMaterialRequirementElement>(true),
            BuildingConfigs = SystemAPI.GetBufferLookup<BuildingConfigElement>(true),
            ItemConfigs = SystemAPI.GetBufferLookup<ItemConfigElement>(true),
            Definitions = SystemAPI.GetBufferLookup<StartingItemConfigElement>(true),
            BuildingPrefabs = SystemAPI.GetBufferLookup<BuildingPrefabElement>(true),
            ItemPrefabs = SystemAPI.GetBufferLookup<ItemPrefabElement>(true),
            ConfigEntity = SystemAPI.GetSingletonEntity<BuildingConfig>(),
            ItemConfigEntity = SystemAPI.GetSingletonEntity<ItemRegistry>(),
            DefinitionEntity = SystemAPI.GetSingletonEntity<StartingItemConfig>(),
            BuildingDatabase = SystemAPI.GetSingletonEntity<BuildingPrefabDatabase>(),
            ItemDatabase = SystemAPI.GetSingletonEntity<ItemPrefabDatabase>(),
            ECB = end.CreateCommandBuffer()
        }.Schedule(JobHandle.CombineDependencies(state.Dependency, ready));
        end.AddJobHandleForProducer(apply);
        state.Dependency = JobHandle.CombineDependencies(
            JobHandle.CombineDependencies(requests.Dispose(apply), records.Dispose(apply)),
            JobHandle.CombineDependencies(buildings.Dispose(apply), spawns.Dispose(apply)));
        state.Dependency = JobHandle.CombineDependencies(state.Dependency, itemEntities.Dispose(apply));
    }
}

[BurstCompile]
public struct ApplyGameBootstrapJob : IJob
{
    [ReadOnly] public NativeArray<Entity> Requests;
    [ReadOnly] public NativeArray<Entity> Records;
    [ReadOnly] public NativeArray<Entity> Buildings;
    [ReadOnly] public NativeArray<Entity> Spawns;
    [ReadOnly] public NativeArray<Entity> ItemEntities;
    [ReadOnly] public ComponentLookup<GameBootstrapRequest> RequestData;
    [ReadOnly] public ComponentLookup<GameBootstrapState> RecordData;
    [ReadOnly] public ComponentLookup<BuildingType> BuildingTypes;
    [ReadOnly] public ComponentLookup<GridPosition> Positions;
    [ReadOnly] public ComponentLookup<BuildingFootprint> Footprints;
    [ReadOnly] public ComponentLookup<Direction> Directions;
    [ReadOnly] public ComponentLookup<SpawnBuildingRequest> SpawnData;
    [ReadOnly] public ComponentLookup<ConstructionSite> Sites;
    [ReadOnly] public ComponentLookup<Disabled> DisabledEntities;
    [ReadOnly] public ComponentLookup<ItemOwnership> ItemOwners;
    [ReadOnly] public ComponentLookup<DestroyItemRequest> DestroyRequests;
    [ReadOnly] public BufferLookup<ConstructionMaterialRequirementElement> Requirements;
    [ReadOnly] public BufferLookup<BuildingConfigElement> BuildingConfigs;
    [ReadOnly] public BufferLookup<ItemConfigElement> ItemConfigs;
    [ReadOnly] public BufferLookup<StartingItemConfigElement> Definitions;
    [ReadOnly] public BufferLookup<BuildingPrefabElement> BuildingPrefabs;
    [ReadOnly] public BufferLookup<ItemPrefabElement> ItemPrefabs;
    public Entity ConfigEntity;
    public Entity ItemConfigEntity;
    public Entity DefinitionEntity;
    public Entity BuildingDatabase;
    public Entity ItemDatabase;
    public EntityCommandBuffer ECB;

    private struct ItemSlot
    {
        public ItemTypeEnum Type;
        public Entity Prefab;
        public int Slot;
        public int Quantity;
    }

    public void Execute()
    {
        if (Requests.Length != 1 || Records.Length > 1)
        {
            Fail("[GameBootstrapApplySystem] Duplicate bootstrap request or record.");
            return;
        }

        Entity request = Requests[0];
        var configs = BuildingConfigs[ConfigEntity];
        bool hasMainFacilityConfig = TryGetMainFacilityConfig(configs, out var config);
        if (Records.Length == 1)
        {
            if (!hasMainFacilityConfig || !IsValidRecord(RecordData[Records[0]].MainFacility, config.BuildingId))
            {
                Fail("[GameBootstrapApplySystem] Bootstrap record has an invalid main facility.");
            }
            ECB.DestroyEntity(request);
            return;
        }

        if (!hasMainFacilityConfig)
        {
            Fail("[GameBootstrapApplySystem] Main facility configuration is missing.");
            ECB.DestroyEntity(request);
            return;
        }

        using var worldItemPositions = new NativeList<int2>(Allocator.Temp);
        CollectWorldItemPositions(worldItemPositions);
        if (!ValidateBootstrapConflictsAndRecordFailure(config.Footprint, configs, worldItemPositions.AsArray()))
        {
            ECB.DestroyEntity(request);
            return;
        }

        using var slots = new NativeList<ItemSlot>(Allocator.Temp);
        if (!PrepareItemSlots(config.StorageCapacity, config.DroneStationStorageCapacity, slots))
        {
            ECB.DestroyEntity(request);
            return;
        }

        Entity main = BuildingLifecycleUtility.SpawnConfiguredBuilding(ref ECB, int2.zero,
            DirectionEnum.Up, RequestData[request].Stamp,
            BuildingDatabase, config, BuildingPrefabs);
        if (main == Entity.Null)
        {
            ECB.DestroyEntity(request);
            return;
        }

        for (int i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];
            for (int count = 0; count < slot.Quantity; count++)
            {
                Entity item = ItemLifecycleUtility.SpawnPrefabItem(ref ECB, slot.Prefab, slot.Type,
                    int2.zero, ItemOwnership.Stored(main));
                ECB.AppendToBuffer(main, new StoredItemElement(item, slot.Type, slot.Slot));
            }
        }

        // 생성된 deferred Entity 참조와 지급 완료 기록은 같은 ECB에서 함께 remap/공개한다.
        Entity record = ECB.CreateEntity();
        ECB.AddComponent(record, new GameBootstrapState { MainFacility = main });
        ECB.DestroyEntity(request);
    }

    // 시작 정책의 주 시설 1티어를 선택한다. 이후 생성과 완료 기록 검증은 선택한 ID를 사용한다.
    private static bool TryGetMainFacilityConfig(in DynamicBuffer<BuildingConfigElement> configs, out BuildingConfigElement config)
    {
        foreach (var definition in configs)
        {
            if (definition.BuildingType == BuildingTypeEnum.MainFacility && definition.Tier == 1)
            {
                config = definition;
                return true;
            }
        }
        config = default;
        return false;
    }

    private bool IsValidRecord(Entity main, int expectedBuildingId)
    {
        if (!BuildingTypes.TryGetComponent(main, out var type) ||
            !Positions.TryGetComponent(main, out var position) ||
            !Directions.TryGetComponent(main, out var direction))
        {
            return false;
        }

        if (type.BuildingId != expectedBuildingId || type.Type != BuildingTypeEnum.MainFacility || type.Tier != 1 ||
            !math.all(position.Value == int2.zero) || direction.dir != DirectionEnum.Up)
        {
            return false;
        }

        int mainCount = 0;
        for (int i = 0; i < Buildings.Length; i++)
        {
            if (BuildingTypes[Buildings[i]].Type == BuildingTypeEnum.MainFacility)
            {
                mainCount++;
            }
        }
        return mainCount == 1;
    }

    private void CollectWorldItemPositions(NativeList<int2> positions)
    {
        for (int i = 0; i < ItemEntities.Length; i++)
        {
            Entity item = ItemEntities[i];
            if (DestroyRequests.HasComponent(item) && DestroyRequests.IsComponentEnabled(item))
            {
                continue;
            }
            if (!ItemOwners[item].IsWorldItem)
            {
                continue;
            }
            positions.Add(Positions[item].Value);
        }
    }

    private bool ValidateBootstrapConflictsAndRecordFailure(int2 size, DynamicBuffer<BuildingConfigElement> configs,
        NativeArray<int2> worldItemPositions)
    {
        for (int i = 0; i < Buildings.Length; i++)
        {
            Entity building = Buildings[i];
            if (BuildingTypes[building].Type == BuildingTypeEnum.MainFacility)
            {
                Fail("[GameBootstrapApplySystem] Existing main facility has no bootstrap record.");
                return false;
            }
            if (!Positions.TryGetComponent(building, out var position) ||
                !Footprints.TryGetComponent(building, out var footprint) ||
                !Directions.TryGetComponent(building, out var direction))
            {
                Fail("[GameBootstrapApplySystem] Existing building has incomplete placement data.");
                return false;
            }
            int2 occupiedSize = BuildingFootprintUtility.GetEffectiveSize(footprint.Size, direction.dir);
            int2 buildingPosition = position.Value;
            if (OverlapsStart(buildingPosition, occupiedSize, size))
            {
                Fail("[GameBootstrapApplySystem] Starting footprint is occupied by a building or site.");
                return false;
            }
            // 다른 위치여도 같은 종료 경계에 실제 주 시설을 완공하면 부트스트랩과 중복 생성된다.
            // 미충족/바닥 차단/비활성/취소 현장은 완공 후보가 아니며 기존 공사 수명을 유지한다.
            if (Sites.TryGetComponent(building, out var site) &&
                Requirements.TryGetBuffer(building, out var requirements) && !DisabledEntities.HasComponent(building))
            {
                if (BuildingConfigLookupUtility.TryGetConfig(configs, site.TargetBuildingId, out var target) &&
                    target.BuildingType == BuildingTypeEnum.MainFacility &&
                    (site.Flags & ConstructionSiteFlags.Cancelled) == 0 &&
                    ConstructionCompletionUtility.AreMaterialsSatisfied(requirements) &&
                    !ConstructionCompletionUtility.HasBlockingWorldItem(buildingPosition, occupiedSize, worldItemPositions))
                {
                    Fail("[GameBootstrapApplySystem] Another main facility will complete at this building boundary.");
                    return false;
                }
            }
        }

        // 기존 수명주기가 같은 EndBuilding에 기록한 직접 Spawn도 원본 요청이 소비되기 전에 확인한다.
        for (int i = 0; i < Spawns.Length; i++)
        {
            var spawn = SpawnData[Spawns[i]];
            // 공통 생성 소유자가 생성 없이 소비하는 요청은 미공개 점유가 아니다.
            if (spawn.BuildingId == 0)
            {
                continue;
            }
            if (!BuildingConfigLookupUtility.TryGetConfig(configs, spawn.BuildingId, out var target))
            {
                Fail("[GameBootstrapApplySystem] Pending building spawn has invalid configuration.");
                return false;
            }
            if (!math.all(spawn.FootprintSize == int2.zero) && !math.all(spawn.FootprintSize == target.Footprint))
            {
                continue;
            }
            if (target.BuildingType == BuildingTypeEnum.MainFacility)
            {
                Fail("[GameBootstrapApplySystem] Another main facility spawn is already pending.");
                return false;
            }
            int2 occupiedSize = BuildingFootprintUtility.GetEffectiveSize(target.Footprint, spawn.Direction);
            if (OverlapsStart(spawn.Position, occupiedSize, size))
            {
                Fail("[GameBootstrapApplySystem] Starting footprint conflicts with a pending spawn.");
                return false;
            }
        }
        return true;
    }

    private bool PrepareItemSlots(int generalSlots, int dedicatedSlots, NativeList<ItemSlot> slots)
    {
        int totalSlots = generalSlots + dedicatedSlots;
        var used = new NativeArray<byte>(totalSlots, Allocator.Temp);
        try
        {
            var definitions = Definitions[DefinitionEntity];
            var items = ItemConfigs[ItemConfigEntity];
            var prefabs = ItemPrefabs[ItemDatabase];
            // 일반 품목의 공간을 먼저 정하고 드론은 기존 전용 우선/일반 공유 규칙으로 배치한다.
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < definitions.Length; i++)
                {
                    var definition = definitions[i];
                    if ((definition.ItemType == ItemTypeEnum.Drone) != (pass == 1))
                    {
                        continue;
                    }
                    int maximum = ItemRegistry.GetMaxStack(items, definition.ItemType);
                    if (maximum <= 0 || !PrefabLookupUtility.TryGetItemPrefab(prefabs, definition.ItemType, out Entity prefab))
                    {
                        Fail("[GameBootstrapApplySystem] Starting item configuration or prefab is missing.");
                        return false;
                    }
                    int remaining = definition.Quantity;
                    for (int index = 0; index < totalSlots && remaining > 0; index++)
                    {
                        int slot = StationStorageUtility.SearchSlot(index, totalSlots, dedicatedSlots, definition.ItemType);
                        if (used[slot] != 0 || !StationStorageUtility.IsItemSlotAllowed(slot, totalSlots, dedicatedSlots, definition.ItemType))
                        {
                            continue;
                        }
                        int amount = math.min(maximum, remaining);
                        slots.Add(new ItemSlot { Type = definition.ItemType, Prefab = prefab, Slot = slot, Quantity = amount });
                        used[slot] = 1;
                        remaining -= amount;
                    }
                    if (remaining > 0)
                    {
                        Fail("[GameBootstrapApplySystem] Starting items exceed main facility storage capacity.");
                        return false;
                    }
                }
            }
            return true;
        }
        finally
        {
            used.Dispose();
        }
    }

    private static bool OverlapsStart(int2 position, int2 size, int2 startSize)
    {
        return (long)position.x < startSize.x && (long)position.x + size.x > 0 &&
            (long)position.y < startSize.y && (long)position.y + size.y > 0;
    }

    private void Fail(in FixedString128Bytes message)
    {
        SimulationFailureUtility.Record(ref ECB, message);
    }
}
