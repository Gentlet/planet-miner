using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>승인된 벨트 실물과 건물 슬롯 수량을 EndBuilding 경계에서 인계한다.</summary>
[UpdateInGroup(typeof(BuildingStateApplyGroup))]
[UpdateBefore(typeof(ItemLifecycleApplySystem))]
[BurstCompile]
public partial struct BuildingItemStorageApplySystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<ItemRegistry>();
        state.RequireForUpdate<BeltSpatialIndex>();
        state.RequireForUpdate<BeltSpatialIndexFence>();
    }

    public void OnUpdate(ref SystemState state)
    {
        var boundary = state.World.GetOrCreateSystemManaged<EndBuildingEntityCommandBufferSystem>();
        var beltIndex = SystemAPI.GetSingleton<BeltSpatialIndex>();
        ref var beltFence = ref SystemAPI.GetSingletonRW<BeltSpatialIndexFence>().ValueRW;
        var storage = SystemAPI.GetBufferLookup<StorageSlotElement>();
        var items = SystemAPI.GetSingletonBuffer<ItemConfigElement>(true);
        var input = new BuildingItemInputApplyJob
        {
            Storage = storage,
            Items = items,
            Movement = SystemAPI.GetComponentLookup<BeltMovementState>(),
            Destroy = SystemAPI.GetComponentLookup<DestroyItemRequest>()
        }.Schedule(state.Dependency);

        state.Dependency = new BuildingItemOutputApplyJob
        {
            BeltMap = beltIndex.Map,
            Storage = storage,
            Products = SystemAPI.GetBufferLookup<ProductSlotElement>(),
            Prefabs = SystemAPI.GetBufferLookup<ItemPrefabElement>(true),
            PrefabDatabase = SystemAPI.TryGetSingletonEntity<ItemPrefabDatabase>(out var database) ? database : Entity.Null,
            ECB = boundary.CreateCommandBuffer()
        }.Schedule(JobHandle.CombineDependencies(input, beltFence.State.GetReaderDependency()));
        beltFence.State.AddReader(state.Dependency);
        boundary.AddJobHandleForProducer(state.Dependency);
    }
}

[BurstCompile]
public partial struct BuildingItemInputApplyJob : IJobEntity
{
    public BufferLookup<StorageSlotElement> Storage;
    [ReadOnly] public DynamicBuffer<ItemConfigElement> Items;
    public ComponentLookup<BeltMovementState> Movement;
    public ComponentLookup<DestroyItemRequest> Destroy;

    public void Execute(Entity item, in ItemIdentity identity, ref BuildingItemInputDecision decision,
        EnabledRefRW<BuildingItemInputDecision> enabled)
    {
        bool approved = decision.CanDeposit && decision.TargetSlotIndex >= 0;
        var target = decision.TargetBuilding;
        int slot = decision.TargetSlotIndex;
        decision = default;
        enabled.ValueRW = false;

        if (!approved) return;
        if (!Destroy.HasComponent(item)) return;
        if (Destroy.IsComponentEnabled(item)) return;
        if (!Storage.TryGetBuffer(target, out var inventory)) return;
        int maximum = ItemRegistry.GetMaxStack(Items, identity.Type);
        if (!InventorySlotUtility.TryAdd(inventory, slot, identity.Type, 1, maximum)) return;

        if (Movement.HasComponent(item)) Movement.SetComponentEnabled(item, false);
        Destroy.SetComponentEnabled(item, true);
    }
}

[BurstCompile]
public partial struct BuildingItemOutputApplyJob : IJobEntity
{
    [ReadOnly] public NativeParallelHashMap<int2, BeltInfo> BeltMap;
    public BufferLookup<StorageSlotElement> Storage;
    public BufferLookup<ProductSlotElement> Products;
    [ReadOnly] public BufferLookup<ItemPrefabElement> Prefabs;
    public Entity PrefabDatabase;
    public EntityCommandBuffer ECB;

    public void Execute(Entity building, ref BuildingItemOutputDecision decision,
        EnabledRefRW<BuildingItemOutputDecision> enabled)
    {
        var approved = decision;
        decision = default;
        enabled.ValueRW = false;
        if (!approved.CanOutput) return;
        if (!BeltMap.TryGetValue(approved.TargetBeltPosition, out var belt)) return;
        if (!TryReadOutputPrefab(approved.ItemType, out var prefab))
        {
            SimulationFailureUtility.Record(ref ECB, "[BuildingItemStorageApplySystem] Missing output item prefab.");
            return;
        }

        int taken = 0;
        if (approved.FromProduct && Products.TryGetBuffer(building, out var products))
            taken = InventorySlotUtility.TakeProduct(products, approved.SourceSlotIndex, approved.ItemType, 1);
        if (!approved.FromProduct && Storage.TryGetBuffer(building, out var inventory))
            taken = InventorySlotUtility.Take(inventory, approved.SourceSlotIndex, approved.ItemType, 1);
        if (taken == 0) return;

        int2 position = approved.TargetBeltPosition;
        Entity item = ItemLifecycleUtility.SpawnPrefabItem(ref ECB, prefab, approved.ItemType, position);
        ECB.AddComponent(item, new Direction(belt.Direction));
        ECB.SetComponent(item, new BeltMovementState(0f));
        ECB.SetComponentEnabled<BeltMovementState>(item, true);
        float2 visualPosition = (float2)position - (float2)belt.Direction.ToInt2() * 0.5f;
        ECB.SetComponent(item, LocalTransform.FromPosition(new float3(visualPosition, 0f)));
    }

    private bool TryReadOutputPrefab(ItemTypeEnum type, out Entity prefab)
    {
        prefab = Entity.Null;
        if (PrefabDatabase == Entity.Null) return false;
        if (!Prefabs.TryGetBuffer(PrefabDatabase, out var prefabs)) return false;
        return PrefabLookupUtility.TryGetItemPrefab(prefabs, type, out prefab);
    }
}
