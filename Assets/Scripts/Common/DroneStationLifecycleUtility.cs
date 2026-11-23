using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Rendering;

internal struct DroneStationRequestRecord
{
    public Entity Entity;
    public DroneStationRequest Request;
    public DroneStationDecision Decision;
}

internal struct DroneStationSlotRange
{
    public int Start;
    public int Count;
    public int Dedicated;
}

internal struct DroneStationSlot
{
    public ItemTypeEnum Type;
    public int Count;
    public Entity Docked;
}

/// <summary>요청 순서를 정하고 작업 경합과 호출별 슬롯 예산을 중재한다.</summary>
internal static class DroneStationLifecycleUtility
{
    private struct RequestComparer : IComparer<DroneStationRequestRecord>
    {
        public int Compare(DroneStationRequestRecord left, DroneStationRequestRecord right)
        {
            return left.Request.Sequence.CompareTo(right.Request.Sequence);
        }
    }

    public static void ReadSortedRequests(NativeArray<Entity> entities, ComponentLookup<DroneStationRequest> requests,
        ComponentLookup<DroneStationDecision> decisions, NativeList<DroneStationRequestRecord> records)
    {
        foreach (Entity entity in entities)
        {
            records.Add(new DroneStationRequestRecord { Entity = entity, Request = requests[entity], Decision = decisions[entity] });
        }
        records.AsArray().Sort(new RequestComparer());
    }

    public static void GatherBusyWorkers(NativeArray<Entity> pendingOwners, BufferLookup<DroneTaskPendingPublicationElement> pending,
        BufferLookup<DroneTransportPendingReservationElement> transportPending,
        NativeArray<Entity> actionEntities, ComponentLookup<DroneActionReadyRequest> actions, NativeHashSet<Entity> busy)
    {
        foreach (Entity owner in pendingOwners)
        {
            foreach (var entry in pending[owner])
            {
                if (entry.Quantity > 0 || entry.PublicationQueued)
                {
                    busy.Add(entry.Candidate.Worker);
                }
            }
            if (!transportPending.TryGetBuffer(owner, out var transportEntries))
            {
                continue;
            }
            foreach (var entry in transportEntries)
            {
                // Take는 원본 확보 기록을 비운다. ECB 기록 뒤에도 수행자 연결이 공개될 때까지 Busy를 유지한다.
                if (entry.Reservation.RemainingQuantity > 0 || entry.PublicationQueued)
                {
                    busy.Add(entry.Candidate.Worker);
                }
            }
        }
        foreach (Entity entity in actionEntities)
        {
            busy.Add(actions[entity].Action.Worker);
        }
    }

    public static int ReserveSlot(in DroneStationRequest request, int preferred, int maxStack,
        ComponentLookup<Storage> storages, ComponentLookup<DroneStationStorage> dedicated, ComponentLookup<FuelStorageSlots> fuelSlots,
        BufferLookup<StorageSlotElement> items, BufferLookup<DockedDroneElement> docked,
        NativeParallelHashMap<Entity, DroneStationSlotRange> ranges, NativeList<DroneStationSlot> slots,
        BufferLookup<DroneStationReservedSlotElement> reserved)
    {
        Entity station = request.Station;
        Entity drone = request.Drone;
        bool docking = request.Operation == DroneStationOperation.Dock;
        if (!ranges.TryGetValue(station, out var range))
        {
            range = new DroneStationSlotRange
            {
                Start = slots.Length, Count = fuelSlots.TryGetComponent(station, out var fuel) ? fuel.Start : storages[station].SlotCount, Dedicated = dedicated[station].DedicatedSlotCount
            };
            for (int index = 0; index < range.Count; index++)
            {
                slots.Add(default);
            }
            var inventory = items[station];
            for (int index = 0; index < range.Count; index++)
            {
                var item = inventory[index];
                slots[range.Start + index] = new DroneStationSlot { Type = item.ItemType, Count = item.Quantity };
            }
            foreach (var dock in docked[station])
            {
                if (dock.SlotIndex >= 0 && dock.SlotIndex < range.Count)
                {
                    int index = range.Start + dock.SlotIndex;
                    var slot = slots[index];
                    slot.Docked = dock.Drone;
                    slots[index] = slot;
                }
            }
            ranges.Add(station, range);
        }
        bool hasReservations = reserved.TryGetBuffer(station, out var reservations);
        int promised = hasReservations ? DroneStationRelocationUtility.ReservedSlot(reservations, request.RelocationTask, request.RelocationMemberIndex) : -1;
        if (promised >= 0) preferred = promised;
        for (int index = 0; index < range.Count; index++)
        {
            int candidate = preferred >= 0 ? preferred : StationStorageUtility.SearchSlot(index, range.Count, range.Dedicated, ItemTypeEnum.Drone);
            if ((uint)candidate >= (uint)range.Count)
                return -1;
            var slot = slots[range.Start + candidate];
            if (hasReservations && DroneStationRelocationUtility.IsReserved(reservations, candidate, request.RelocationTask, request.RelocationMemberIndex))
            {
                continue;
            }
            if ((slot.Docked == Entity.Null || slot.Docked == drone) &&
                (slot.Count == 0 || (!docking && slot.Type == ItemTypeEnum.Drone && slot.Count < maxStack)))
            {
                slot.Docked = docking ? drone : Entity.Null;
                slot.Type = ItemTypeEnum.Drone;
                slot.Count += docking ? 0 : 1;
                slots[range.Start + candidate] = slot;
                return candidate;
            }
            if (preferred >= 0)
            {
                break;
            }
        }
        return -1;
    }

}
