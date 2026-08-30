using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;

public partial class DroneDirectTaskSystem
{
    private static readonly ProfilerMarker FindRecoveryDestinationMarker =
        new("DroneDirectTask.FindRecoveryDestination");
    private static readonly ProfilerMarker ScanRecoveryStoragesMarker =
        new("DroneDirectTask.ScanRecoveryStorages");

    public bool HasAvailableRecoveryDestination(
        Entity itemEntity,
        int networkId)
    {
        if (!TryGetWorldItem(itemEntity, out Item item, out GridPosition position))
            return false;

        return TryFindRecoveryDestination(
            position.gridPosition,
            networkId,
            item.type,
            out _);
    }

    public bool TryGetRecoveryDestination(
        Entity itemEntity,
        int networkId,
        out Entity destinationOwner)
    {
        destinationOwner = Entity.Null;

        if (!TryGetWorldItem(itemEntity, out Item item, out GridPosition position))
            return false;

        return TryFindRecoveryDestination(
            position.gridPosition,
            networkId,
            item.type,
            out destinationOwner);
    }

    public bool CanUseRecoveryDestination(
        Entity itemEntity,
        int networkId,
        Entity destinationOwner)
    {
        if (!TryGetWorldItem(itemEntity, out Item item, out _))
            return false;

        if (destinationOwner == Entity.Null)
            return false;

        if (!EntityManager.Exists(destinationOwner))
            return false;

        if (!EntityManager.HasComponent<Storage>(destinationOwner))
            return false;

        if (!EntityManager.HasComponent<GridPosition>(destinationOwner))
            return false;

        if (!EntityManager.HasBuffer<StoredItemElement>(destinationOwner))
            return false;

        GridPosition destinationPosition = EntityManager
            .GetComponentData<GridPosition>(destinationOwner);

        if (!_networkSystem.TryGetNetworkIdAtCell(
                destinationPosition.gridPosition,
                out int destinationNetworkId))
            return false;

        if (destinationNetworkId != networkId)
            return false;

        if (_storageLimitQuery.IsEmptyIgnoreFilter)
            return false;

        Entity storageLimitEntity = _storageLimitQuery.GetSingletonEntity();
        using NativeArray<ItemStorageLimitElement> storageLimits =
            DynamicBufferCopyUtility.CreateNativeCopy(
                EntityManager.GetBuffer<ItemStorageLimitElement>(
                    storageLimitEntity,
                    true),
                Allocator.Temp);
        return CanStorageAcceptItem(
            destinationOwner,
            item.type,
            storageLimits);
    }

    private bool TryFindRecoveryDestination(
        int2 sourceCell,
        int networkId,
        ItemTypeEnum itemType,
        out Entity destinationOwner)
    {
        using ProfilerMarker.AutoScope findDestinationScope =
            FindRecoveryDestinationMarker.Auto();
        destinationOwner = Entity.Null;

        if (_storageLimitQuery.IsEmptyIgnoreFilter)
            return false;

        Entity storageLimitEntity = _storageLimitQuery.GetSingletonEntity();
        using NativeArray<ItemStorageLimitElement> storageLimits =
            DynamicBufferCopyUtility.CreateNativeCopy(
                EntityManager.GetBuffer<ItemStorageLimitElement>(
                    storageLimitEntity,
                    true),
                Allocator.Temp);
        using NativeArray<Entity> storageEntities =
            _storageQuery.ToEntityArray(Allocator.Temp);
        float nearestDistanceSquared = float.MaxValue;

        using (ScanRecoveryStoragesMarker.Auto())
        {
            for (int i = 0; i < storageEntities.Length; i++)
            {
                Entity candidate = storageEntities[i];
                int2 candidateCell = EntityManager
                    .GetComponentData<GridPosition>(candidate)
                    .gridPosition;

                if (!_networkSystem.TryGetNetworkIdAtCell(
                        candidateCell,
                        out int candidateNetwork))
                    continue;

                if (candidateNetwork != networkId)
                    continue;

                if (!CanStorageAcceptItem(candidate, itemType, storageLimits))
                    continue;

                float distanceSquared = math.distancesq(
                    new float2(sourceCell),
                    new float2(candidateCell));

                if (distanceSquared >= nearestDistanceSquared)
                    continue;

                destinationOwner = candidate;
                nearestDistanceSquared = distanceSquared;
            }
        }

        return destinationOwner != Entity.Null;
    }

    private bool CanStorageAcceptItem(
        Entity storageEntity,
        ItemTypeEnum itemType,
        NativeArray<ItemStorageLimitElement> storageLimits)
    {
        Storage storage = EntityManager.GetComponentData<Storage>(storageEntity);
        DynamicBuffer<StoredItemElement> storedItems = EntityManager
            .GetBuffer<StoredItemElement>(storageEntity, true);
        bool hasReservedCapacity = EntityManager.HasBuffer<
            DroneReservedStorageCapacityElement>(storageEntity);
        DynamicBuffer<DroneReservedStorageCapacityElement> reservedCapacity =
            hasReservedCapacity
                ? EntityManager.GetBuffer<DroneReservedStorageCapacityElement>(
                    storageEntity,
                    true)
                : default;
        return StorageCapacityUtility.CanStoreAdditionalItems(
            storedItems,
            reservedCapacity,
            hasReservedCapacity,
            storage.capacity,
            storageLimits,
            itemType,
            1,
            DroneStationStorageUtility.GetStoredDroneCount(
                EntityManager,
                storageEntity),
            DroneStationStorageUtility.GetDedicatedDroneSlotCapacity(
                EntityManager,
                storageEntity));
    }

    private bool TryGetWorldItem(
        Entity itemEntity,
        out Item item,
        out GridPosition position)
    {
        item = default;
        position = default;

        if (itemEntity == Entity.Null)
            return false;

        if (!EntityManager.Exists(itemEntity))
            return false;

        if (!EntityManager.HasComponent<Item>(itemEntity))
            return false;

        if (!EntityManager.HasComponent<GridPosition>(itemEntity))
            return false;

        if (EntityManager.HasComponent<StoredItem>(itemEntity))
            return false;

        if (EntityManager.HasComponent<Disabled>(itemEntity))
            return false;

        item = EntityManager.GetComponentData<Item>(itemEntity);
        position = EntityManager.GetComponentData<GridPosition>(itemEntity);
        return true;
    }

    private bool IsValidDemolitionTarget(Entity targetEntity)
    {
        if (targetEntity == Entity.Null)
            return false;

        if (!EntityManager.Exists(targetEntity))
            return false;

        if (!EntityManager.HasComponent<BuildingOccupant>(targetEntity))
            return false;

        if (!EntityManager.HasComponent<GridPosition>(targetEntity))
            return false;

        if (EntityManager.HasComponent<IndestructibleBuilding>(targetEntity))
            return false;

        return true;
    }
}
