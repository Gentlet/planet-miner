using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

/// <summary>
/// 생산 결과를 출력 슬롯에 더한 뒤 직접 생성 요청과 월드 아이템 삭제를 처리한다.
/// Storage/Product 요청은 지정 슬롯의 수량을 변경하고 World 요청의 실물은 EndBuilding에 공개한다.
/// </summary>
[UpdateInGroup(typeof(BuildingStateApplyGroup))]
[UpdateBefore(typeof(BuildingLifecycleApplySystem))]
public partial struct ItemLifecycleApplySystem : ISystem
{
    private EntityQuery _productResultQuery;
    private EntityQuery _spawnQuery;
    private EntityQuery _destroyQuery;
    private BufferLookup<StorageSlotElement> _storedBufferLookup;
    private BufferLookup<ProductSlotElement> _productBufferLookup;
    private BufferLookup<ItemPrefabElement> _itemPrefabBufferLookup;
    private EntityQuery _prefabDbQuery;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<ItemRegistry>();
        _storedBufferLookup = state.GetBufferLookup<StorageSlotElement>(false);
        _productBufferLookup = state.GetBufferLookup<ProductSlotElement>(false);
        _itemPrefabBufferLookup = state.GetBufferLookup<ItemPrefabElement>(true);

        _productResultQuery = SystemAPI.QueryBuilder()
            .WithAllRW<ProductResult>()
            .WithAll<ProductSlotElement>()
            .Build();

        _spawnQuery = SystemAPI.QueryBuilder()
            .WithAll<SpawnItemRequest>()
            .Build();

        _destroyQuery = SystemAPI.QueryBuilder()
            .WithAll<DestroyItemRequest>()
            .Build();

        _prefabDbQuery = SystemAPI.QueryBuilder()
            .WithAll<ItemPrefabDatabase, ItemPrefabElement>()
            .Build();

    }

    public void OnUpdate(ref SystemState state)
    {
        var ecbSystem = state.World.GetOrCreateSystemManaged<EndBuildingEntityCommandBufferSystem>();
        var ecb = ecbSystem.CreateCommandBuffer();

        _storedBufferLookup.Update(ref state);
        _productBufferLookup.Update(ref state);
        _itemPrefabBufferLookup.Update(ref state);

        // 생산 결과를 먼저 반영해 직접 요청이 남은 슬롯 공간을 사용하게 한다.
        var items = SystemAPI.GetSingletonBuffer<ItemConfigElement>(true);
        var productResultJob = new ProductResultApplyJob
        {
            ECB = ecb,
            Items = items
        };
        var productResultHandle = productResultJob.Schedule(_productResultQuery, state.Dependency);

        var spawnHandle = productResultHandle;
        var footprintsReleased = default(JobHandle);
        if (!_spawnQuery.IsEmptyIgnoreFilter)
        {
            // EndCommand 이후 현장 원본은 실제 스폰 요청이 있는 틱에만 복사한다.
            var constructionFootprints = ConstructionSiteWorldItemUtility.CaptureFootprints(state.EntityManager, Allocator.TempJob);
            Entity prefabDbEntity = _prefabDbQuery.IsEmptyIgnoreFilter ? Entity.Null : _prefabDbQuery.GetSingletonEntity();
            var spawnJob = new SpawnItemApplyJob
            {
                EntityHandle = SystemAPI.GetEntityTypeHandle(),
                SpawnItemRequestHandle = SystemAPI.GetComponentTypeHandle<SpawnItemRequest>(true),
                ECB = ecb,
                PrefabDbEntity = prefabDbEntity,
                ItemPrefabBufferLookup = _itemPrefabBufferLookup,
                Items = items,
                DedicatedSlots = SystemAPI.GetComponentLookup<DroneStationStorage>(true),
                InputSlots = SystemAPI.GetBufferLookup<BuildingInputSlotElement>(true),
                Docks = SystemAPI.GetBufferLookup<DockedDroneElement>(true),
                Reserved = SystemAPI.GetBufferLookup<DroneStationReservedSlotElement>(true),
                Demolition = SystemAPI.GetComponentLookup<PendingBuildingDemolition>(true),
                Preparing = SystemAPI.GetComponentLookup<PreparingStationDemolition>(true),
                StoredBufferLookup = _storedBufferLookup,
                ProductBufferLookup = _productBufferLookup,
                ConstructionFootprints = constructionFootprints
            };
            spawnHandle = spawnJob.Schedule(_spawnQuery, productResultHandle);
            footprintsReleased = constructionFootprints.Dispose(spawnHandle);
        }

        // 3. [파괴 Job] DestroyItemRequest 처리
        var destroyJob = new DestroyItemApplyJob
        {
            ECB = ecb
        };
        var destroyHandle = destroyJob.Schedule(_destroyQuery, spawnHandle);

        state.Dependency = JobHandle.CombineDependencies(destroyHandle, footprintsReleased);
        ecbSystem.AddJobHandleForProducer(destroyHandle);
    }
}

/// <summary>
/// 생산 건물의 ProductResult를 출력 슬롯 수량에 더하고 소비하는 단일 워커 Burst Job.
/// </summary>
[BurstCompile]
public partial struct ProductResultApplyJob : IJobEntity
{
    public EntityCommandBuffer ECB;
    [ReadOnly] public DynamicBuffer<ItemConfigElement> Items;

    public void Execute(ref DynamicBuffer<ProductResult> results, ref DynamicBuffer<ProductSlotElement> slots)
    {
        foreach (var result in results)
        {
            int maximum = ItemRegistry.GetMaxStack(Items, result.ItemType);
            if (!InventorySlotUtility.TryAddProduct(slots, result.SlotIndex, result.ItemType, result.Count, maximum))
            {
                SimulationFailureUtility.Record(ref ECB, "[ItemLifecycleApplySystem] Production result exceeds the configured output slot.");
            }
        }
        results.Clear();
    }
}

[BurstCompile]
public partial struct SpawnItemApplyJob : IJobChunk
{
    [ReadOnly] public EntityTypeHandle EntityHandle;
    [ReadOnly] public ComponentTypeHandle<SpawnItemRequest> SpawnItemRequestHandle;

    public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
    {
        var entities = chunk.GetNativeArray(EntityHandle);
        var spawnItemRequestValues = chunk.GetNativeArray(ref SpawnItemRequestHandle);
        var prefabBufferRead = new RegistryBufferReadCache<ItemPrefabElement>();
        var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
        while (enumerator.NextEntityIndex(out int index))
        {
            var requestValue = spawnItemRequestValues[index];
            ExecuteEntity(entities[index],
                in requestValue, ref prefabBufferRead);
        }
    }

    public EntityCommandBuffer ECB;
    public Entity PrefabDbEntity;
    [ReadOnly] public NativeArray<ConstructionSiteWorldItemUtility.Footprint> ConstructionFootprints;
    [ReadOnly] public DynamicBuffer<ItemConfigElement> Items;
    [ReadOnly] public ComponentLookup<DroneStationStorage> DedicatedSlots;
    [ReadOnly] public BufferLookup<BuildingInputSlotElement> InputSlots;
    [ReadOnly] public BufferLookup<DockedDroneElement> Docks;
    [ReadOnly] public BufferLookup<DroneStationReservedSlotElement> Reserved;
    [ReadOnly] public ComponentLookup<PendingBuildingDemolition> Demolition;
    [ReadOnly] public ComponentLookup<PreparingStationDemolition> Preparing;

    [ReadOnly]
    public BufferLookup<ItemPrefabElement> ItemPrefabBufferLookup;

    public BufferLookup<StorageSlotElement> StoredBufferLookup;

    public BufferLookup<ProductSlotElement> ProductBufferLookup;

    private void ExecuteEntity(Entity requestEntity, in SpawnItemRequest request,
        ref RegistryBufferReadCache<ItemPrefabElement> prefabBufferRead)
    {
        ECB.DestroyEntity(requestEntity);
        if (request.Destination == ItemSpawnDestination.World)
        {
            if (!ConstructionSiteWorldItemUtility.IsWorldPositionAllowed(ConstructionFootprints, request.Position)) return;
            if (PrefabDbEntity != Entity.Null && prefabBufferRead.TryRead(ItemPrefabBufferLookup, PrefabDbEntity, out var prefabs) &&
                PrefabLookupUtility.TryGetItemPrefab(prefabs, request.ItemType, out var prefab))
            {
                ItemLifecycleUtility.SpawnPrefabItem(ref ECB, prefab, request.ItemType, request.Position);
            }
            else
            {
                SimulationFailureUtility.Record(ref ECB, "[ItemLifecycleApplySystem] Missing prefab for world item.");
            }
            return;
        }

        Entity owner = request.TargetOwner;
        if (Demolition.HasComponent(owner)) return;
        int slot = request.TargetSlotIndex;
        var type = request.ItemType;
        int maximum = ItemRegistry.GetMaxStack(Items, type);
        if (maximum <= 0) return;
        if (request.Destination == ItemSpawnDestination.Product && ProductBufferLookup.TryGetBuffer(owner, out var output))
        {
            InventorySlotUtility.TryAddProduct(output, slot, type, 1, maximum);
            return;
        }

        if (request.Destination != ItemSpawnDestination.Storage ||
            !StoredBufferLookup.TryGetBuffer(owner, out var storage)) return;
        int dedicated = DedicatedSlots.TryGetComponent(owner, out var station) ? station.DedicatedSlotCount : 0;
        if (!StationStorageUtility.IsItemSlotAllowed(slot, storage.Length, dedicated, type)) return;
        if (type == ItemTypeEnum.Drone && Preparing.HasComponent(owner)) return;
        if (InputSlots.TryGetBuffer(owner, out var inputs) &&
            ((uint)slot >= (uint)inputs.Length || inputs[slot].ItemType != type)) return;
        if (Docks.TryGetBuffer(owner, out var docks) && StationStorageUtility.IsDockOccupied(docks, slot)) return;
        if (DroneStationRelocationUtility.IsReserved(Reserved, owner, slot)) return;
        InventorySlotUtility.TryAdd(storage, slot, type, 1, maximum);
    }
}

/// <summary>
/// DestroyItemRequest가 켜진 엔티티를 파괴하는 단일 워커 Burst Job.
/// </summary>
[BurstCompile]
public partial struct DestroyItemApplyJob : IJobEntity
{
    public EntityCommandBuffer ECB;

    public void Execute(Entity entity, in DestroyItemRequest request)
    {
        ItemLifecycleUtility.RecordDestroy(ref ECB, entity);
    }
}
