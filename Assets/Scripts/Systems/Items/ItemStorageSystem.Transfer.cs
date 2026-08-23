using Unity.Entities;

public partial class ItemStorageSystem
{
    public bool TryConsumeOwnedItemImmediate<TSourceElement>(
        Entity owner,
        int sourceIndex)
        where TSourceElement : unmanaged, IBufferElementData, IItemStorageElement
    {
        if (owner == Entity.Null)
            return false;

        if (!EntityManager.Exists(owner))
            return false;

        if (!EntityManager.HasBuffer<TSourceElement>(owner))
            return false;

        DynamicBuffer<TSourceElement> items =
            EntityManager.GetBuffer<TSourceElement>(owner);

        if (sourceIndex < 0 || sourceIndex >= items.Length)
            return false;

        Entity itemEntity = items[sourceIndex].ItemEntity;

        if (!ValidateStoredItemOwnership(itemEntity, owner))
            return false;

        if (EntityManager.HasComponent<DroneItemReservation>(itemEntity))
            return false;

        items.RemoveAt(sourceIndex);
        EntityManager.DestroyEntity(itemEntity);
        return true;
    }

    public bool TryTransferOwnedItemImmediate<TSourceElement>(
        Entity sourceOwner,
        int sourceIndex,
        Entity destinationOwner)
        where TSourceElement : unmanaged, IBufferElementData, IItemStorageElement
    {
        return TryTransferOwnedItemImmediate<TSourceElement>(
            sourceOwner,
            sourceIndex,
            destinationOwner,
            Entity.Null);
    }

    public bool TryTransferReservedItemImmediate<TSourceElement>(
        Entity sourceOwner,
        int sourceIndex,
        Entity destinationOwner,
        Entity reservationEntity)
        where TSourceElement : unmanaged, IBufferElementData, IItemStorageElement
    {
        if (reservationEntity == Entity.Null)
            return false;

        if (!EntityManager.Exists(reservationEntity))
            return false;

        return TryTransferOwnedItemImmediate<TSourceElement>(
            sourceOwner,
            sourceIndex,
            destinationOwner,
            reservationEntity);
    }

    private bool TryTransferOwnedItemImmediate<TSourceElement>(
        Entity sourceOwner,
        int sourceIndex,
        Entity destinationOwner,
        Entity reservationEntity)
        where TSourceElement : unmanaged, IBufferElementData, IItemStorageElement
    {
        if (sourceOwner == Entity.Null)
            return false;

        if (destinationOwner == Entity.Null)
            return false;

        if (sourceOwner == destinationOwner)
            return false;

        if (!EntityManager.Exists(sourceOwner))
            return false;

        if (!EntityManager.Exists(destinationOwner))
            return false;

        if (!EntityManager.HasBuffer<TSourceElement>(sourceOwner))
            return false;

        if (!EntityManager.HasBuffer<StoredItemElement>(destinationOwner))
            return false;

        DynamicBuffer<TSourceElement> sourceItems =
            EntityManager.GetBuffer<TSourceElement>(sourceOwner);

        if (sourceIndex < 0 || sourceIndex >= sourceItems.Length)
            return false;

        Entity itemEntity = sourceItems[sourceIndex].ItemEntity;

        if (!ValidateStoredItemOwnership(itemEntity, sourceOwner))
            return false;

        if (!EntityManager.HasComponent<Item>(itemEntity))
            return false;

        if (!EntityManager.HasComponent<Disabled>(itemEntity))
            return false;

        Item itemData = EntityManager.GetComponentData<Item>(itemEntity);

        bool hasReservation = EntityManager.HasComponent<DroneItemReservation>(
            itemEntity);

        if (reservationEntity == Entity.Null)
        {
            if (hasReservation)
                return false;
        }
        else
        {
            if (!hasReservation)
                return false;

            DroneItemReservation itemReservation = EntityManager
                .GetComponentData<DroneItemReservation>(itemEntity);

            if (itemReservation.reservationEntity != reservationEntity)
                return false;
        }

        DynamicBuffer<StoredItemElement> destinationItems =
            EntityManager.GetBuffer<StoredItemElement>(destinationOwner);

        if (ContainsItem(destinationItems, itemEntity))
            return false;

        destinationItems.Add(new StoredItemElement
        {
            itemEntity = itemEntity,
            type = itemData.type
        });
        EntityManager.SetComponentData(
            itemEntity,
            new StoredItem { owner = destinationOwner });

        sourceItems = EntityManager.GetBuffer<TSourceElement>(sourceOwner);
        int currentSourceIndex = FindItemIndex(sourceItems, itemEntity);

        if (currentSourceIndex >= 0)
        {
            sourceItems.RemoveAt(currentSourceIndex);
            return true;
        }

        destinationItems = EntityManager.GetBuffer<StoredItemElement>(
            destinationOwner);
        int destinationIndex = FindItemIndex(
            destinationItems,
            itemEntity);

        if (destinationIndex >= 0)
            destinationItems.RemoveAt(destinationIndex);

        EntityManager.SetComponentData(
            itemEntity,
            new StoredItem { owner = sourceOwner });
        return false;
    }

    private bool ValidateStoredItemOwnership(
        Entity itemEntity,
        Entity expectedOwner)
    {
        if (itemEntity == Entity.Null)
            return false;

        if (!EntityManager.Exists(itemEntity))
            return false;

        if (!EntityManager.HasComponent<StoredItem>(itemEntity))
            return false;

        StoredItem storedItem =
            EntityManager.GetComponentData<StoredItem>(itemEntity);
        return storedItem.owner == expectedOwner;
    }

    private static bool ContainsItem(
        DynamicBuffer<StoredItemElement> items,
        Entity itemEntity)
    {
        return FindItemIndex(items, itemEntity) >= 0;
    }

    private static int FindItemIndex<TElement>(
        DynamicBuffer<TElement> items,
        Entity itemEntity)
        where TElement : unmanaged, IBufferElementData, IItemStorageElement
    {
        for (int i = 0; i < items.Length; i++)
        {
            if (items[i].ItemEntity == itemEntity)
                return i;
        }

        return -1;
    }
}
