using Unity.Collections;
using Unity.Entities;

/// <summary>예약 조회/기존 운송의 입고 자격/철거 확인 완료 조건을 공유한다. 원본은 변경하지 않는다.</summary>
public static class DroneStationRelocationUtility
{
    /// <summary>준비 전에 이미 수집한 운송만 마무리한다. 이관 대상 확정 뒤 새 드론을 추가하지 않는다.</summary>
    public static bool CanReceiveCommittedDroneCargo(EntityManager manager, Entity station, Entity assignmentEntity,
        Entity worker = default)
    {
        if (!manager.HasComponent<PreparingStationDemolition>(station)) return false;
        if (!manager.HasComponent<DroneTaskAssignment>(assignmentEntity)) return false;
        var preparation = manager.GetComponentData<PreparingStationDemolition>(station);
        if (preparation.Started) return false;
        var assignment = manager.GetComponentData<DroneTaskAssignment>(assignmentEntity);
        if (assignment.Destination != station || assignment.ItemType != ItemTypeEnum.Drone ||
            assignment.NextAction != DroneActionKindEnum.StoreCargo || DroneSchedulingUtility.IsTerminal(assignment.State)) return false;
        if (worker != Entity.Null && assignment.Worker != worker) return false;
        if (DroneSchedulingUtility.ReadAssignmentCargoQuantity(manager, assignment) <= 0) return false;
        if (!manager.HasComponent<DroneLogisticsTask>(assignment.Task)) return false;
        var task = manager.GetComponentData<DroneLogisticsTask>(assignment.Task);
        if (task.State == DroneLogisticsTaskStateEnum.Cancelled) return false;
        if (preparation.Task == Entity.Null) return true;
        if (!manager.HasComponent<DroneLogisticsTask>(preparation.Task)) return false;
        return task.CreationSequence < manager.GetComponentData<DroneLogisticsTask>(preparation.Task).CreationSequence;
    }

    public static bool IsReserved(EntityManager manager, Entity station, int slot, Entity except = default)
    {
        if (!manager.HasBuffer<DroneStationReservedSlotElement>(station)) return false;
        return IsReserved(manager.GetBuffer<DroneStationReservedSlotElement>(station, true), slot, except);
    }

    public static int ReservedSlot(EntityManager manager, Entity station, Entity drone)
    {
        if (!manager.HasBuffer<DroneStationReservedSlotElement>(station)) return -1;
        return ReservedSlot(manager.GetBuffer<DroneStationReservedSlotElement>(station, true), drone);
    }

    public static bool IsReserved(BufferLookup<DroneStationReservedSlotElement> reservations, Entity station, int slot, Entity except = default)
    {
        if (!reservations.HasBuffer(station)) return false;
        return IsReserved(reservations[station], slot, except);
    }

    public static int ReservedSlot(BufferLookup<DroneStationReservedSlotElement> reservations, Entity station, Entity drone)
    {
        if (!reservations.HasBuffer(station)) return -1;
        return ReservedSlot(reservations[station], drone);
    }

    private static bool IsReserved(DynamicBuffer<DroneStationReservedSlotElement> reservations, int slot, Entity except)
    {
        foreach (var row in reservations)
        {
            if (row.SlotIndex == slot && row.Drone != except) return true;
        }
        return false;
    }

    private static int ReservedSlot(DynamicBuffer<DroneStationReservedSlotElement> reservations, Entity drone)
    {
        foreach (var row in reservations)
        {
            if (row.Drone == drone) return row.SlotIndex;
        }
        return -1;
    }

    public static bool IsComplete(EntityManager manager, Entity station)
    {
        if (!manager.HasComponent<PreparingStationDemolition>(station)) return false;
        var preparation = manager.GetComponentData<PreparingStationDemolition>(station);
        if (!preparation.Started || preparation.CompletedCount != preparation.TotalCount) return false;
        if (manager.HasBuffer<DockedDroneElement>(station) && !manager.GetBuffer<DockedDroneElement>(station, true).IsEmpty) return false;
        if (manager.HasBuffer<DroneStationReservedSlotElement>(station) && !manager.GetBuffer<DroneStationReservedSlotElement>(station, true).IsEmpty) return false;
        foreach (var item in manager.GetBuffer<StoredItemElement>(station, true))
        {
            if (item.ItemType == ItemTypeEnum.Drone) return false;
        }
        using var query = manager.CreateEntityQuery(new EntityQueryDesc
        {
            All = new[] { ComponentType.ReadOnly<DroneStationRelocation>() },
            Options = EntityQueryOptions.IncludeDisabledEntities
        });
        using var actors = query.ToComponentDataArray<DroneStationRelocation>(Allocator.Temp);
        foreach (var actor in actors)
        {
            if (actor.Source == station) return false;
        }
        return true;
    }

    public static bool IsStationDemolition(EntityManager manager, in DroneLogisticsTask task) =>
        task.Kind == DroneLogisticsTaskKindEnum.BuildingDemolition &&
        manager.HasComponent<PreparingStationDemolition>(task.Target);

    public static bool ShouldStopUnstartedRelocation(EntityManager manager, in DroneStationRelocation relocation, Entity candidates)
    {
        if (!manager.HasComponent<DroneLogisticsTask>(relocation.Task)) return true;
        var task = manager.GetComponentData<DroneLogisticsTask>(relocation.Task);
        return task.State == DroneLogisticsTaskStateEnum.Cancelled || task.State == DroneLogisticsTaskStateEnum.Closed ||
            DroneTaskRequestDecisionUtility.IsCancellationPlanned(manager, candidates, relocation.Task);
    }
}
