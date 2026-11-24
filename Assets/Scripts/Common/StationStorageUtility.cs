using Unity.Entities;

/// <summary>일반 슬롯 뒤의 드론 전용 구간과 보관 자격을 공통 해석한다.</summary>
public static class StationStorageUtility
{
    public static bool IsItemSlotAllowed(int slot, int total, int dedicated, ItemTypeEnum type)
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
        return type == ItemTypeEnum.Drone ? slot >= general : slot < general;
    }

    internal static bool IsDroneStorageSlot(int slot, int nonFuelCount, int dedicated) =>
        (uint)slot < (uint)nonFuelCount && slot >= nonFuelCount - dedicated;

    internal static bool CanStoreDrone(in DynamicBuffer<StorageSlotElement> items,
        in DynamicBuffer<DroneStationReservedSlotElement> reservations, int nonFuelCount, int dedicated, int maxStack)
    {
        for (int index = nonFuelCount - dedicated; index < nonFuelCount; index++)
        {
            if (FreeDroneQuantity(items[index], reservations, index, maxStack) > 0)
            {
                return true;
            }
        }
        return false;
    }

    internal static int FreeDroneQuantity(in StorageSlotElement item,
        in DynamicBuffer<DroneStationReservedSlotElement> reservations, int slot, int maxStack,
        Entity relocationTask = default, int memberIndex = -1)
    {
        int free = InventorySlotUtility.FreeQuantity(item, ItemTypeEnum.Drone, maxStack);
        return reservations.IsCreated && free > 0
            ? DroneDemolitionStorageUtility.FreeQuantity(reservations, slot, ItemTypeEnum.Drone, free,
                Entity.Null, relocationTask, memberIndex)
            : free;
    }

    public static int NonFuelSlotCount(EntityManager manager, Entity owner, int total)
    {
        return manager.HasComponent<FuelStorageSlots>(owner)
            ? manager.GetComponentData<FuelStorageSlots>(owner).Start : total;
    }

    public static bool IsTransportSlotAllowed(EntityManager manager, Entity owner, DroneTransportAreaEnum area,
        ItemTypeEnum type, int slot, int total, int dedicated)
    {
        if (manager.HasComponent<FuelStorageSlots>(owner))
        {
            var fuel = manager.GetComponentData<FuelStorageSlots>(owner);
            if (area == DroneTransportAreaEnum.Input)
                return slot >= fuel.Start && slot < fuel.Start + fuel.Count && fuel.Allows(type);
            total = fuel.Start;
        }
        return IsItemSlotAllowed(slot, total, dedicated, type);
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

    internal static bool MatchesDockedWorker(EntityManager manager, Entity station, Entity worker)
    {
        return manager.HasComponent<DroneWorker>(worker) && !manager.HasComponent<ItemIdentity>(worker) &&
            manager.HasComponent<DroneDocked>(worker) && manager.GetComponentData<DroneDocked>(worker).Station == station;
    }

    // 읽기 전용 탐색 한 번의 고정 메타데이터/버퍼만 공유한다. 슬롯 점유 판단은 매번 수행한다.
    internal ref struct StorageReadContext
    {
        private EntityManager _manager;
        private Entity _storage;
        private bool _readSlots, _readDedicated, _readStored, _readReservations, _readInputPresence, _readInputs;
        private int _slots, _dedicated;
        private bool _hasReservations, _hasInputs;
        private DynamicBuffer<StorageSlotElement> _stored;
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

        internal int NonFuelSlotCount => StationStorageUtility.NonFuelSlotCount(_manager, _storage, SlotCount);

        internal bool AllowsTransport(DroneTransportAreaEnum area, ItemTypeEnum type, int slot)
        {
            return IsTransportSlotAllowed(_manager, _storage, area, type, slot, SlotCount, DedicatedSlotCount);
        }

        internal DynamicBuffer<StorageSlotElement> StoredItems
        {
            get
            {
                if (!_readStored)
                {
                    _stored = _manager.GetBuffer<StorageSlotElement>(_storage, true);
                    _readStored = true;
                }
                return _stored;
            }
        }

        internal int FreeAfterReservations(int slot, ItemTypeEnum type, int free, Entity worker = default)
        {
            ReadReservations();
            return _hasReservations ? DroneDemolitionStorageUtility.FreeQuantity(_reservations, slot, type, free, worker) : free;
        }

        private void ReadReservations()
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
