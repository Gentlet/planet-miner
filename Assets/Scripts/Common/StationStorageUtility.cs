using Unity.Entities;

/// <summary>일반 슬롯 뒤의 드론 전용 구간과 활성 정박 점유를 공통 해석한다. 소유권/예약을 변경하지 않는다.</summary>
public static class StationStorageUtility
{
    public static bool IsItemSlotAllowed(int slot, int total, int dedicated, ItemTypeEnum type, bool beltInput = false)
    {
        if (slot < 0 || slot >= total)
        {
            return false;
        }
        int general = total - dedicated;
        if (dedicated == 0)
        {
            return true;
        }
        if (type != ItemTypeEnum.Drone)
        {
            return slot < general;
        }
        return !beltInput || slot >= general;
    }

    public static int ReadDedicatedCount(EntityManager manager, Entity storage)
    {
        return manager.HasComponent<DroneStationStorage>(storage)
            ? manager.GetComponentData<DroneStationStorage>(storage).DedicatedSlotCount : 0;
    }

    public static int SearchSlot(int index, int total, int dedicated, ItemTypeEnum type)
    {
        if (type != ItemTypeEnum.Drone || dedicated == 0)
        {
            return index;
        }
        return index < dedicated ? total - dedicated + index : index - dedicated;
    }

    public static bool IsDockOccupied(EntityManager manager, Entity station, int slot, Entity except = default)
    {
        if (!manager.HasBuffer<DockedDroneElement>(station))
        {
            return false;
        }
        foreach (var entry in manager.GetBuffer<DockedDroneElement>(station, true))
        {
            if (entry.SlotIndex == slot && entry.Drone != except)
            {
                return true;
            }
        }
        return false;
    }
}
