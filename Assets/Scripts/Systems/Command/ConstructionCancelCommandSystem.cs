using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

/// <summary>현장에 도착한 수량을 월드에 반환하고 EndCommand에서 현장을 제거한다.</summary>
[UpdateInGroup(typeof(CommandGroup))]
public partial struct ConstructionCancelCommandSystem : ISystem
{
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<CancelConstructionRequest>();

    public void OnUpdate(ref SystemState state)
    {
        var boundary = state.World.GetOrCreateSystemManaged<EndCommandEntityCommandBufferSystem>();
        state.Dependency = new CancelConstructionCommandJob
        {
            ECB = boundary.CreateCommandBuffer(),
            Sites = SystemAPI.GetComponentLookup<ConstructionSite>(),
            Materials = SystemAPI.GetBufferLookup<ConstructionMaterialRequirementElement>(true),
            Positions = SystemAPI.GetComponentLookup<GridPosition>(true),
            Prefabs = SystemAPI.GetBufferLookup<ItemPrefabElement>(true),
            PrefabDatabase = SystemAPI.TryGetSingletonEntity<ItemPrefabDatabase>(out var database) ? database : Entity.Null
        }.Schedule(state.Dependency);
        boundary.AddJobHandleForProducer(state.Dependency);
    }
}

[BurstCompile]
public partial struct CancelConstructionCommandJob : IJobEntity
{
    public EntityCommandBuffer ECB;
    public ComponentLookup<ConstructionSite> Sites;
    [ReadOnly] public BufferLookup<ConstructionMaterialRequirementElement> Materials;
    [ReadOnly] public ComponentLookup<GridPosition> Positions;
    [ReadOnly] public BufferLookup<ItemPrefabElement> Prefabs;
    public Entity PrefabDatabase;

    public void Execute(Entity requestEntity, in CancelConstructionRequest request)
    {
        ECB.DestroyEntity(requestEntity);
        Entity target = request.TargetSite;
        if (!Sites.TryGetComponent(target, out var site) || (site.Flags & ConstructionSiteFlags.Cancelled) != 0) return;
        if (!Materials.TryGetBuffer(target, out var materials) || !Positions.TryGetComponent(target, out var position)) return;

        using var returns = new NativeList<InventoryReturn>(Allocator.Temp);
        foreach (var material in materials)
        {
            if (material.DeliveredQuantity > 0)
                returns.Add(new InventoryReturn(material.ItemType, material.DeliveredQuantity));
        }
        if (!ItemLifecycleUtility.TryReturnInventory(ref ECB, Prefabs, PrefabDatabase, returns, position.Value, false)) return;

        site.Flags |= ConstructionSiteFlags.Cancelled;
        Sites[target] = site;
        ECB.DestroyEntity(target);
    }
}
