using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[UpdateAfter(typeof(ConstructionCancelSystem))]
[UpdateAfter(typeof(DroneCargoTransferSystem))]
[UpdateBefore(typeof(BuildingSpawnSystem))]
public partial class ConstructionCompletionSystem : SystemBase
{
    private ChunkMapSystem _chunkMap;
    private ItemStorageSystem _itemStorage;
    private EntityQuery _siteQuery;

    protected override void OnCreate()
    {
        _chunkMap = World.GetOrCreateSystemManaged<ChunkMapSystem>();
        _itemStorage = World.GetOrCreateSystemManaged<ItemStorageSystem>();
        _siteQuery = GetEntityQuery(
            ComponentType.ReadOnly<ConstructionSite>(),
            ComponentType.ReadOnly<GridPosition>(),
            ComponentType.ReadOnly<ConstructionSiteReservedCellElement>(),
            ComponentType.ReadOnly<ConstructionMaterialRequirementElement>(),
            ComponentType.ReadWrite<StoredItemElement>());
    }

    protected override void OnUpdate()
    {
        using NativeArray<Entity> sites =
            _siteQuery.ToEntityArray(Allocator.Temp);

        for (int i = 0; i < sites.Length; i++)
        {
            Entity siteEntity = sites[i];

            if (!HasAllMaterials(siteEntity))
                continue;

            if (!CanConsumeAllMaterials(siteEntity))
                continue;

            CompleteConstruction(siteEntity);
        }
    }

    private bool HasAllMaterials(Entity siteEntity)
    {
        DynamicBuffer<ConstructionMaterialRequirementElement> requirements =
            EntityManager.GetBuffer<ConstructionMaterialRequirementElement>(
                siteEntity,
                true);
        DynamicBuffer<StoredItemElement> storedItems =
            EntityManager.GetBuffer<StoredItemElement>(siteEntity, true);

        if (requirements.Length == 0)
            return false;

        for (int i = 0; i < requirements.Length; i++)
        {
            ConstructionMaterialRequirementElement requirement = requirements[i];

            if (storedItems.CountItems(requirement.itemType) < requirement.quantity)
                return false;
        }

        return true;
    }

    private bool CanConsumeAllMaterials(Entity siteEntity)
    {
        DynamicBuffer<StoredItemElement> storedItems =
            EntityManager.GetBuffer<StoredItemElement>(siteEntity, true);

        for (int i = 0; i < storedItems.Length; i++)
        {
            Entity itemEntity = storedItems[i].itemEntity;

            if (itemEntity == Entity.Null)
                return false;

            if (!EntityManager.Exists(itemEntity))
                return false;

            if (EntityManager.HasComponent<DroneItemReservation>(itemEntity))
                return false;
        }

        return true;
    }

    private void CompleteConstruction(Entity siteEntity)
    {
        ConstructionSite site = EntityManager
            .GetComponentData<ConstructionSite>(siteEntity);
        int2 anchor = EntityManager.GetComponentData<GridPosition>(siteEntity)
            .gridPosition;

        while (EntityManager.GetBuffer<StoredItemElement>(siteEntity).Length > 0)
        {
            if (_itemStorage.TryConsumeOwnedItemImmediate<StoredItemElement>(
                    siteEntity,
                    0))
                continue;

            Debug.LogError(
                $"Construction completion could not consume an owned material. Site : {siteEntity}");
            return;
        }

        Entity spawnRequest = EntityManager.CreateEntity();
        EntityManager.AddComponentData(spawnRequest, new BuildingSpawnRequest
        {
            type = site.type,
            gridPosition = anchor,
            dir = site.direction,
            selectedItemType = site.selectedItemType
        });

        DynamicBuffer<ConstructionSiteReservedCellElement> reservedCells =
            EntityManager.GetBuffer<ConstructionSiteReservedCellElement>(
                siteEntity,
                true);
        _chunkMap.UnregisterConstructionSite(
            siteEntity,
            reservedCells,
            false);
        EntityManager.DestroyEntity(siteEntity);
    }
}
