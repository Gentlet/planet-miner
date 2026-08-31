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

        int dedicatedDroneSlotCapacity = DroneStationStorageUtility
            .GetDedicatedDroneSlotCapacity(
                _entityManager,
                _selectedBuilding);
        SetStorageLayout(dedicatedDroneSlotCapacity > 0);
        Storage storage = _entityManager.GetComponentData<Storage>(_selectedBuilding);
        DynamicBuffer<StoredItemElement> storedItems =
            _entityManager.GetBuffer<StoredItemElement>(_selectedBuilding, true);

        CountItems(storedItems, _storedCounts, static item => item.type);
        int storedDroneCount = DroneStationStorageUtility.GetStoredDroneCount(
            _entityManager,
            _selectedBuilding);

        if (storedDroneCount > 0)
        {
            _storedCounts.TryGetValue(
                ItemTypeEnum.Drone,
                out int storedDroneItemCount);
            _storedCounts[ItemTypeEnum.Drone] =
                storedDroneItemCount + storedDroneCount;
        }

        if (dedicatedDroneSlotCapacity > 0)
        {
            int totalDroneSlotCount = GetUsedSlotCount(
                _storedCounts,
                storageLimits,
                ItemTypeEnum.Drone);
            int usedDedicatedDroneSlotCount = totalDroneSlotCount <
                                              dedicatedDroneSlotCapacity
                ? totalDroneSlotCount
                : dedicatedDroneSlotCapacity;
            UpdateStationStorageInventories(
                _storedCounts,
                storageLimits,
                storage.capacity,
                dedicatedDroneSlotCapacity);
            int usedSharedSlotCount = GetUsedSlotCount(
                _storedCounts,
                storageLimits);
            bool allSlotsUsed =
                usedSharedSlotCount >= storage.capacity &&
                usedDedicatedDroneSlotCount >= dedicatedDroneSlotCapacity;
            SetStatus(
                $"일반 {usedSharedSlotCount} / {storage.capacity}칸 · " +
                $"드론 전용 {usedDedicatedDroneSlotCount} / " +
                $"{dedicatedDroneSlotCapacity}칸",
                allSlotsUsed ? "status-waiting" : "status-normal");
            return;
        }

        UpdateStationStorageInventories(
            _storedCounts,
            storageLimits,
            storage.capacity,
            dedicatedDroneSlotCapacity);
        int usedSlotCount = GetUsedSlotCount(_storedCounts, storageLimits);

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
        int slotIndex = 0;

        for (ItemTypeEnum itemType = ItemTypeEnum.Iron_Ore;
             itemType < ItemTypeEnum.Count && slotIndex < capacity;
             itemType++)
        {
            if (!counts.TryGetValue(itemType, out int count))
                continue;

            int stackLimit = storageLimits.GetStorageLimit(itemType);
            int effectiveStackLimit = stackLimit > 0 ? stackLimit : 1;
            int remainingCount = count;

            while (remainingCount > 0 && slotIndex < capacity)
            {
                int stackCount = remainingCount > effectiveStackLimit
                    ? effectiveStackLimit
                    : remainingCount;
                SetStorageSlot(
                    _inputContainer,
                    slotIndex,
                    itemType,
                    stackCount,
                    effectiveStackLimit);
                remainingCount -= stackCount;
                slotIndex++;
            }
        }

        while (slotIndex < capacity)
        {
            SetEmptyStorageSlot(_inputContainer, slotIndex);
            slotIndex++;
        }

        TrimStorageSlots(_inputContainer, slotIndex);
    }

    private void UpdateStationStorageInventories(
        Dictionary<ItemTypeEnum, int> counts,
        DynamicBuffer<ItemStorageLimitElement> storageLimits,
        int sharedCapacity,
        int dedicatedDroneSlotCapacity)
    {
        if (dedicatedDroneSlotCapacity <= 0)
        {
            UpdateStorageInventory(counts, storageLimits, sharedCapacity);
            TrimStorageSlots(_dedicatedDroneStorageContainer, 0);
            return;
        }

        counts.TryGetValue(ItemTypeEnum.Drone, out int totalDroneCount);
        int droneStackLimit = storageLimits.GetStorageLimit(
            ItemTypeEnum.Drone);
        int effectiveDroneStackLimit = droneStackLimit > 0
            ? droneStackLimit
            : 1;
        int dedicatedDroneItemCapacity = dedicatedDroneSlotCapacity *
                                         effectiveDroneStackLimit;
        int dedicatedDroneCount = totalDroneCount < dedicatedDroneItemCapacity
            ? totalDroneCount
            : dedicatedDroneItemCapacity;
        int sharedDroneCount = totalDroneCount - dedicatedDroneCount;

        if (sharedDroneCount > 0)
        {
            counts[ItemTypeEnum.Drone] = sharedDroneCount;
        }
        else
        {
            counts.Remove(ItemTypeEnum.Drone);
        }

        UpdateStorageInventory(counts, storageLimits, sharedCapacity);
        UpdateDedicatedDroneStorageInventory(
            dedicatedDroneCount,
            effectiveDroneStackLimit,
            dedicatedDroneSlotCapacity);
    }

    private void UpdateDedicatedDroneStorageInventory(
        int droneCount,
        int stackLimit,
        int capacity)
    {
        int remainingDroneCount = droneCount;
        int slotIndex = 0;

        while (remainingDroneCount > 0 && slotIndex < capacity)
        {
            int stackCount = remainingDroneCount > stackLimit
                ? stackLimit
                : remainingDroneCount;
            SetStorageSlot(
                _dedicatedDroneStorageContainer,
                slotIndex,
                ItemTypeEnum.Drone,
                stackCount,
                stackLimit);
            remainingDroneCount -= stackCount;
            slotIndex++;
        }

        while (slotIndex < capacity)
        {
            SetEmptyStorageSlot(_dedicatedDroneStorageContainer, slotIndex);
            slotIndex++;
        }

        TrimStorageSlots(_dedicatedDroneStorageContainer, slotIndex);
    }

    private static int GetUsedSlotCount(
        Dictionary<ItemTypeEnum, int> counts,
        DynamicBuffer<ItemStorageLimitElement> storageLimits)
    {
        int usedSlotCount = 0;

        foreach (KeyValuePair<ItemTypeEnum, int> entry in counts)
        {
            int stackLimit = storageLimits.GetStorageLimit(entry.Key);
            int effectiveStackLimit = stackLimit > 0 ? stackLimit : 1;
            usedSlotCount +=
                (entry.Value + effectiveStackLimit - 1) /
                effectiveStackLimit;
        }

        return usedSlotCount;
    }

    private static int GetUsedSlotCount(
        Dictionary<ItemTypeEnum, int> counts,
        DynamicBuffer<ItemStorageLimitElement> storageLimits,
        ItemTypeEnum itemType)
    {
        if (!counts.TryGetValue(itemType, out int count))
            return 0;

        int stackLimit = storageLimits.GetStorageLimit(itemType);
        int effectiveStackLimit = stackLimit > 0 ? stackLimit : 1;
        return (count + effectiveStackLimit - 1) / effectiveStackLimit;
    }
}
