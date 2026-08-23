using Unity.Collections;
using Unity.Entities;
using UnityEngine;

public partial class DroneTaskReservationSystem
{
    private bool TrySelectSourceItems(DroneTaskReservationRequest request)
    {
        _selectedItems.Clear();

        if (request.sourceOwner != Entity.Null)
        {
            if (!ValidateSourceOwner(request.sourceOwner))
                return false;

            CollectAvailableItemsFromOwner(
                request.sourceOwner,
                request.destinationOwner,
                request.itemType,
                request.quantity);
        }
        else
        {
            CollectAvailableItemsFromOwners(request);
        }

        if (_selectedItems.Count == request.quantity)
            return true;

        Debug.LogWarning(
            $"Not enough unreserved items for drone task reservation. " +
            $"Task : {request.taskEntity}, Item : {request.itemType}, " +
            $"Requested : {request.quantity}, Found : {_selectedItems.Count}");
        _selectedItems.Clear();
        return false;
    }

    private void CollectAvailableItemsFromOwners(
        DroneTaskReservationRequest request)
    {
        _sourceInventories.Clear();

        AddSourceInventories(
            _storedItemOwnerQuery,
            DroneTaskItemSourceKind.Stored);
        AddSourceInventories(
            _producedItemOwnerQuery,
            DroneTaskItemSourceKind.Produced);
        _sourceInventories.Sort(CompareSourceInventories);

        for (int i = 0; i < _sourceInventories.Count; i++)
        {
            SourceInventoryCandidate sourceInventory =
                _sourceInventories[i];

            if (sourceInventory.Owner == request.destinationOwner)
                continue;

            if (sourceInventory.SourceKind == DroneTaskItemSourceKind.Stored)
            {
                CollectAvailableItems<StoredItemElement>(
                    sourceInventory.Owner,
                    request.destinationOwner,
                    request.itemType,
                    request.quantity,
                    sourceInventory.SourceKind);
            }
            else
            {
                CollectAvailableItems<ProducedItemElement>(
                    sourceInventory.Owner,
                    request.destinationOwner,
                    request.itemType,
                    request.quantity,
                    sourceInventory.SourceKind);
            }

            if (_selectedItems.Count == request.quantity)
                return;
        }
    }

    private void AddSourceInventories(
        EntityQuery query,
        DroneTaskItemSourceKind sourceKind)
    {
        using NativeArray<Entity> sourceOwners =
            query.ToEntityArray(Allocator.Temp);

        for (int i = 0; i < sourceOwners.Length; i++)
        {
            _sourceInventories.Add(new SourceInventoryCandidate
            {
                Owner = sourceOwners[i],
                SourceKind = sourceKind
            });
        }
    }

    private void CollectAvailableItemsFromOwner(
        Entity sourceOwner,
        Entity destinationOwner,
        ItemTypeEnum itemType,
        int requestedQuantity)
    {
        if (EntityManager.HasBuffer<StoredItemElement>(sourceOwner))
        {
            CollectAvailableItems<StoredItemElement>(
                sourceOwner,
                destinationOwner,
                itemType,
                requestedQuantity,
                DroneTaskItemSourceKind.Stored);
        }

        if (_selectedItems.Count == requestedQuantity)
            return;

        if (EntityManager.HasBuffer<ProducedItemElement>(sourceOwner))
        {
            CollectAvailableItems<ProducedItemElement>(
                sourceOwner,
                destinationOwner,
                itemType,
                requestedQuantity,
                DroneTaskItemSourceKind.Produced);
        }
    }

    private void CollectAvailableItems<TElement>(
        Entity sourceOwner,
        Entity destinationOwner,
        ItemTypeEnum itemType,
        int requestedQuantity,
        DroneTaskItemSourceKind sourceKind)
        where TElement : unmanaged, IBufferElementData, IItemStorageElement
    {
        if (sourceOwner == destinationOwner)
            return;

        DynamicBuffer<TElement> ownedItems =
            EntityManager.GetBuffer<TElement>(sourceOwner, true);

        for (int i = 0; i < ownedItems.Length; i++)
        {
            TElement ownedItem = ownedItems[i];

            if (!IsAvailableOwnedItem(
                    ownedItem.ItemEntity,
                    sourceOwner,
                    itemType))
                continue;

            if (IsAlreadySelected(ownedItem.ItemEntity))
                continue;

            _selectedItems.Add(new ReservedItemCandidate
            {
                ItemEntity = ownedItem.ItemEntity,
                SourceOwner = sourceOwner,
                SourceKind = sourceKind
            });

            if (_selectedItems.Count == requestedQuantity)
                return;
        }
    }

    private bool ValidateSourceOwner(Entity sourceOwner)
    {
        if (!EntityManager.Exists(sourceOwner))
        {
            Debug.LogError(
                $"Drone reservation source does not exist. Source : {sourceOwner}");
            return false;
        }

        bool hasStoredItems =
            EntityManager.HasBuffer<StoredItemElement>(sourceOwner);
        bool hasProducedItems =
            EntityManager.HasBuffer<ProducedItemElement>(sourceOwner);

        if (!hasStoredItems && !hasProducedItems)
        {
            Debug.LogError(
                $"Drone reservation source has no item ownership buffer. Source : {sourceOwner}");
            return false;
        }

        return true;
    }

    private bool IsAvailableOwnedItem(
        Entity itemEntity,
        Entity sourceOwner,
        ItemTypeEnum expectedItemType)
    {
        if (itemEntity == Entity.Null)
            return false;

        if (!EntityManager.Exists(itemEntity))
            return false;

        if (!EntityManager.HasComponent<Item>(itemEntity))
            return false;

        if (!EntityManager.HasComponent<StoredItem>(itemEntity))
            return false;

        if (EntityManager.HasComponent<DroneItemReservation>(itemEntity))
            return false;

        Item item = EntityManager.GetComponentData<Item>(itemEntity);

        if (item.type != expectedItemType)
            return false;

        StoredItem storedItem =
            EntityManager.GetComponentData<StoredItem>(itemEntity);
        return storedItem.owner == sourceOwner;
    }

    private bool IsAlreadySelected(Entity itemEntity)
    {
        for (int i = 0; i < _selectedItems.Count; i++)
        {
            if (_selectedItems[i].ItemEntity == itemEntity)
                return true;
        }

        return false;
    }

    private static int CompareSourceInventories(
        SourceInventoryCandidate left,
        SourceInventoryCandidate right)
    {
        int indexComparison = left.Owner.Index.CompareTo(right.Owner.Index);

        if (indexComparison != 0)
            return indexComparison;

        int versionComparison =
            left.Owner.Version.CompareTo(right.Owner.Version);

        if (versionComparison != 0)
            return versionComparison;

        return left.SourceKind.CompareTo(right.SourceKind);
    }

    private struct ReservedItemCandidate
    {
        public Entity ItemEntity;
        public Entity SourceOwner;
        public DroneTaskItemSourceKind SourceKind;
    }

    private struct SourceInventoryCandidate
    {
        public Entity Owner;
        public DroneTaskItemSourceKind SourceKind;
    }
}
