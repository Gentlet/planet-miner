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
        foreach (var entry in manager.GetBuffer<ResearchRecoveryElement>(source, true))
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
        int count = 0;
        foreach (var item in manager.GetBuffer<StoredItemElement>(source, true))
        {
            if ((type == ItemTypeEnum.None || item.ItemType == type) && PassesRecoveryFilter(manager, source, item.ItemEntity) &&
                ItemOwnershipApplySystem.CanTransferItem(manager, item.ItemEntity, item.ItemType, source))
            {
                count++;
            }
        }
        return count;
    }

    public static int CollectionQuantity(EntityManager manager, Entity source, ItemTypeEnum type)
    {
        return IsSource(manager, source) ? Count(manager, source, type) : DroneSchedulingUtility.CountInventory(manager, source, type);
    }

    public static bool HasTaskNeed(EntityManager manager, in DroneLogisticsTask task)
    {
        if (!IsSource(manager, task.Target))
        {
            return false;
        }
        if (Count(manager, task.Target, task.ItemType) > 0)
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
