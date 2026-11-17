using Unity.Collections;
using Unity.Entities;

/// <summary>연구건물의 회수 대기 실물만 일반 운반의 출발 재고로 공개한다. 정상 저장소/납품 대상/공사 공급원 자격은 확장하지 않는다.</summary>
public static class ResearchRecoveryUtility
{
    public static bool IsSource(EntityManager manager, Entity entity)
    {
        if (!DroneSchedulingUtility.IsActiveEntity(manager, entity))
        {
            return false;
        }
        return manager.HasComponent<ResearchCycleState>(entity) &&
            manager.HasComponent<GridPosition>(entity) && manager.HasBuffer<StorageSlotElement>(entity) &&
            manager.HasBuffer<ResearchRecoveryElement>(entity) && !manager.HasComponent<PendingBuildingDemolition>(entity);
    }

    internal static int ReadSlotQuantity(DynamicBuffer<ResearchRecoveryElement> recovery, int slotIndex, ItemTypeEnum type)
    {
        foreach (var entry in recovery)
        {
            if (entry.SlotIndex == slotIndex && entry.ItemType == type) return entry.Quantity;
        }
        return 0;
    }

    internal static void ConsumeSlot(DynamicBuffer<ResearchRecoveryElement> recovery, int slotIndex, ItemTypeEnum type, int quantity)
    {
        for (int index = 0; index < recovery.Length; index++)
        {
            var entry = recovery[index];
            if (entry.SlotIndex != slotIndex || entry.ItemType != type) continue;
            entry.Quantity -= quantity;
            if (entry.Quantity <= 0) recovery.RemoveAt(index);
            else recovery[index] = entry;
            return;
        }
    }

    public static int Count(EntityManager manager, Entity source, ItemTypeEnum type)
    {
        if (!IsSource(manager, source))
        {
            return 0;
        }
        return CountValidatedSource(manager, source, type);
    }

    internal static int CountValidatedSource(EntityManager manager, Entity source, ItemTypeEnum type)
    {
        var read = new ValidatedSourceReadContext(manager, source);
        return read.Count(type);
    }

    // IsSource 검증을 마친 출발지 하나에서 버퍼를 지연 취득해 품목별 재고 검사에 공유한다.
    internal ref struct ValidatedSourceReadContext
    {
        private readonly EntityManager _manager;
        private readonly Entity _source;
        private DynamicBuffer<StorageSlotElement> _storedItems;
        private DynamicBuffer<ResearchRecoveryElement> _recovery;

        internal ValidatedSourceReadContext(EntityManager manager, Entity source)
        {
            _manager = manager;
            _source = source;
            _storedItems = default;
            _recovery = default;
        }

        internal DynamicBuffer<StorageSlotElement> StoredItems
        {
            get
            {
                if (!_storedItems.IsCreated)
                {
                    _storedItems = _manager.GetBuffer<StorageSlotElement>(_source, true);
                }
                return _storedItems;
            }
        }

        internal int Count(ItemTypeEnum type)
        {
            int count = 0;
            var storage = StoredItems;
            if (!_recovery.IsCreated) _recovery = _manager.GetBuffer<ResearchRecoveryElement>(_source, true);
            foreach (var entry in _recovery)
            {
                if ((type == ItemTypeEnum.None || entry.ItemType == type) &&
                    (uint)entry.SlotIndex < (uint)storage.Length && storage[entry.SlotIndex].ItemType == entry.ItemType)
                    count += Unity.Mathematics.math.min(entry.Quantity, storage[entry.SlotIndex].Quantity);
            }
            return count;
        }
    }

    public static bool HasTaskNeed(EntityManager manager, in DroneLogisticsTask task)
    {
        if (!IsSource(manager, task.Target))
        {
            return false;
        }
        if (CountValidatedSource(manager, task.Target, task.ItemType) > 0)
        {
            return true;
        }
        // 마지막 회수품이 이미 이동 중이면 실제 납품/수량 정산까지 기존 root를 유지한다.
        using var query = manager.CreateEntityQuery(typeof(DroneTaskAssignment));
        using var assignments = query.ToEntityArray(Allocator.Temp);
        foreach (Entity entity in assignments)
        {
            var assignment = manager.GetComponentData<DroneTaskAssignment>(entity);
            if (!manager.HasComponent<DroneLogisticsTask>(assignment.Task))
            {
                continue;
            }
            if (manager.GetComponentData<DroneLogisticsTask>(assignment.Task).CreationSequence == task.CreationSequence &&
                DroneSchedulingUtility.ReadAssignmentCargoQuantity(manager, assignment) > 0)
            {
                return true;
            }
        }
        return false;
    }
}
