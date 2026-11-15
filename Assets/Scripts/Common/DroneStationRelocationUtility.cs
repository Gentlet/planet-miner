using Unity.Collections;
using Unity.Entities;

/// <summary>정거장 이주의 자격/예약 조회와 지정 드론의 슬롯 반환을 공유한다. 반환 시점과 완료 정산은 각 호출자가 소유한다.</summary>
public static class DroneStationRelocationUtility
{
    /// <summary>재계획 또는 종료 소유자가 지정한 드론의 예약 행을 모두 제거한다.</summary>
    internal static void ReleaseSlots(DynamicBuffer<DroneStationReservedSlotElement> reservations, Entity drone)
    {
        for (int i = reservations.Length - 1; i >= 0; i--)
        {
            if (reservations[i].Drone == drone)
            {
                reservations.RemoveAt(i);
            }
        }
    }

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
        if (!reservations.TryGetBuffer(station, out var buffer))
        {
            return false;
        }
        return IsReserved(buffer, slot, except);
    }

    public static int ReservedSlot(BufferLookup<DroneStationReservedSlotElement> reservations, Entity station, Entity drone)
    {
        if (!reservations.TryGetBuffer(station, out var buffer))
        {
            return -1;
        }
        return ReservedSlot(buffer, drone);
    }

    internal static bool IsReserved(DynamicBuffer<DroneStationReservedSlotElement> reservations, int slot, Entity except = default)
    {
        foreach (var row in reservations)
        {
            if (row.SlotIndex == slot && row.Drone != except) return true;
        }
        return false;
    }

    internal static int ReservedSlot(DynamicBuffer<DroneStationReservedSlotElement> reservations, Entity drone)
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
