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
        return IsDockOccupied(manager.GetBuffer<DockedDroneElement>(station, true), slot, except);
    }

    internal static bool IsDockOccupied(DynamicBuffer<DockedDroneElement> docks, int slot, Entity except = default)
    {
        foreach (var entry in docks)
        {
            if (entry.SlotIndex == slot && entry.Drone != except)
            {
                return true;
            }
        }
        return false;
    }

    // 읽기 전용 탐색 한 번의 고정 메타데이터/버퍼만 공유한다. 슬롯 점유 판단은 매번 수행한다.
    internal ref struct StorageReadContext
    {
        private EntityManager _manager;
        private Entity _storage;
        private bool _readSlots, _readDedicated, _readStored, _readDocks, _readReservations, _readInputPresence, _readInputs;
        private int _slots, _dedicated;
        private bool _hasDocks, _hasReservations, _hasInputs;
        private DynamicBuffer<StoredItemElement> _stored;
        private DynamicBuffer<DockedDroneElement> _docks;
        private DynamicBuffer<DroneStationReservedSlotElement> _reservations;
        private DynamicBuffer<BuildingInputSlotElement> _inputs;

        internal static StorageReadContext Create(EntityManager manager, Entity storage) =>
            new StorageReadContext { _manager = manager, _storage = storage };

        internal int SlotCount
        {
            get
            {
                if (!_readSlots)
                {
                    _slots = _manager.GetComponentData<Storage>(_storage).SlotCount;
                    _readSlots = true;
                }
                return _slots;
            }
        }

        internal int DedicatedSlotCount
        {
            get
            {
                if (!_readDedicated)
                {
                    _dedicated = ReadDedicatedCount(_manager, _storage);
                    _readDedicated = true;
                }
                return _dedicated;
            }
        }

        internal DynamicBuffer<StoredItemElement> StoredItems
        {
            get
            {
                if (!_readStored)
                {
                    _stored = _manager.GetBuffer<StoredItemElement>(_storage, true);
                    _readStored = true;
                }
                return _stored;
            }
        }

        internal bool IsDockOccupied(int slot)
        {
            if (!_readDocks)
            {
                _hasDocks = _manager.HasBuffer<DockedDroneElement>(_storage);
                if (_hasDocks)
                {
                    _docks = _manager.GetBuffer<DockedDroneElement>(_storage, true);
                }
                _readDocks = true;
            }
            return _hasDocks && StationStorageUtility.IsDockOccupied(_docks, slot);
        }

        internal bool IsReserved(int slot)
        {
            if (!_readReservations)
            {
                _hasReservations = _manager.HasBuffer<DroneStationReservedSlotElement>(_storage);
                if (_hasReservations)
                {
                    _reservations = _manager.GetBuffer<DroneStationReservedSlotElement>(_storage, true);
                }
                _readReservations = true;
            }
            return _hasReservations && DroneStationRelocationUtility.IsReserved(_reservations, slot);
        }

        internal bool HasInputSlots
        {
            get
            {
                if (!_readInputPresence)
                {
                    _hasInputs = _manager.HasBuffer<BuildingInputSlotElement>(_storage);
                    _readInputPresence = true;
                }
                return _hasInputs;
            }
        }

        internal DynamicBuffer<BuildingInputSlotElement> InputSlots
        {
            get
            {
                if (!_readInputs)
                {
                    _inputs = _manager.GetBuffer<BuildingInputSlotElement>(_storage, true);
                    _readInputs = true;
                }
                return _inputs;
            }
        }
    }
}
