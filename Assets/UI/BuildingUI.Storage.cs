using System.Collections.Generic;
using Unity.Entities;

public partial class BuildingUI
{
    private void RefreshStorage(
        DynamicBuffer<ItemStorageLimitElement> storageLimits)
    {
        if (!_entityManager.HasBuffer<StoredItemElement>(_selectedBuilding))
        {
            Close();
            return;
        }

        SetStorageLayout();
        Storage storage = _entityManager.GetComponentData<Storage>(_selectedBuilding);
        DynamicBuffer<StoredItemElement> storedItems =
            _entityManager.GetBuffer<StoredItemElement>(_selectedBuilding, true);

        CountItems(storedItems, _storedCounts, static item => item.type);
        UpdateStorageInventory(
            _storedCounts,
            storageLimits,
            storage.capacity);
        int usedSlotCount = storedItems.GetUsedSlotCount(storageLimits);

        if (storage.capacity > 0 && usedSlotCount >= storage.capacity)
        {
            SetStatus(
                $"모든 창고 칸 사용 중 ({usedSlotCount} / {storage.capacity}칸)",
                "status-waiting");
        }
        else
        {
            SetStatus(
                $"창고 칸 사용량 ({usedSlotCount} / {storage.capacity}칸)",
                "status-normal");
        }
    }

    private void UpdateStorageInventory(
        Dictionary<ItemTypeEnum, int> counts,
        DynamicBuffer<ItemStorageLimitElement> storageLimits,
        int capacity)
    {
        _inputContainer.Clear();
        int createdSlotCount = 0;

        for (ItemTypeEnum itemType = ItemTypeEnum.Iron_Ore;
             itemType < ItemTypeEnum.Count && createdSlotCount < capacity;
             itemType++)
        {
            if (!counts.TryGetValue(itemType, out int count))
                continue;

            int stackLimit = storageLimits.GetStorageLimit(itemType);
            int effectiveStackLimit = stackLimit > 0 ? stackLimit : 1;
            int remainingCount = count;

            while (remainingCount > 0 && createdSlotCount < capacity)
            {
                int stackCount = remainingCount > effectiveStackLimit
                    ? effectiveStackLimit
                    : remainingCount;
                AddStorageSlot(
                    _inputContainer,
                    itemType,
                    stackCount,
                    effectiveStackLimit);
                remainingCount -= stackCount;
                createdSlotCount++;
            }
        }

        while (createdSlotCount < capacity)
        {
            AddEmptyStorageSlot(_inputContainer);
            createdSlotCount++;
        }
    }
}
