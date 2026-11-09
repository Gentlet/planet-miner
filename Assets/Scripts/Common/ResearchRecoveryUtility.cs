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
            manager.HasComponent<GridPosition>(entity) && manager.HasBuffer<StoredItemElement>(entity) &&
            manager.HasBuffer<ResearchRecoveryElement>(entity) && !manager.HasComponent<PendingBuildingDemolition>(entity);
    }

    public static bool IsCollectionSource(EntityManager manager, Entity entity)
    {
        return DroneSchedulingUtility.IsStorage(manager, entity) || IsSource(manager, entity);
    }

    /// <summary>회수 부분집합이 있는 소유자만 목록으로 제한한다. 일반 보관/수행자는 이 필터를 통과한다.</summary>
    public static bool PassesRecoveryFilter(EntityManager manager, Entity source, Entity item)
    {
        if (!manager.HasBuffer<ResearchRecoveryElement>(source))
        {
            return true;
        }
        return ContainsRecoveryItem(manager.GetBuffer<ResearchRecoveryElement>(source, true), item);
    }

    private static bool ContainsRecoveryItem(DynamicBuffer<ResearchRecoveryElement> recovery, Entity item)
    {
        foreach (var entry in recovery)
        {
            if (entry.Item == item)
            {
                return true;
            }
        }
        return false;
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
        private DynamicBuffer<StoredItemElement> _storedItems;
        private DynamicBuffer<ResearchRecoveryElement> _recovery;

        internal ValidatedSourceReadContext(EntityManager manager, Entity source)
        {
            _manager = manager;
            _source = source;
            _storedItems = default;
            _recovery = default;
        }

        internal DynamicBuffer<StoredItemElement> StoredItems
        {
            get
            {
                if (!_storedItems.IsCreated)
                {
                    _storedItems = _manager.GetBuffer<StoredItemElement>(_source, true);
                }
                return _storedItems;
            }
        }

        internal int Count(ItemTypeEnum type)
        {
            int count = 0;
            foreach (var item in StoredItems)
            {
                if (type != ItemTypeEnum.None && item.ItemType != type)
                {
                    continue;
                }
                if (!_recovery.IsCreated)
                {
                    _recovery = _manager.GetBuffer<ResearchRecoveryElement>(_source, true);
                }
                if (ContainsRecoveryItem(_recovery, item.ItemEntity) &&
                    ItemOwnershipApplySystem.CanTransferItem(_manager, item.ItemEntity, item.ItemType, _source))
                {
                    count++;
                }
            }
            return count;
        }
    }

    public static int CollectionQuantity(EntityManager manager, Entity source, ItemTypeEnum type)
    {
        return IsSource(manager, source) ? CountValidatedSource(manager, source, type) : DroneSchedulingUtility.CountInventory(manager, source, type);
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
