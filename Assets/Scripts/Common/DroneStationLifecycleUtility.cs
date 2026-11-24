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
        BufferLookup<StorageSlotElement> items,
        NativeParallelHashMap<Entity, DroneStationSlotRange> ranges, NativeList<DroneStationSlot> slots,
        BufferLookup<DroneStationReservedSlotElement> reserved)
    {
        Entity station = request.Station;
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
            ranges.Add(station, range);
        }
        bool hasReservations = reserved.TryGetBuffer(station, out var reservations);
        int promised = hasReservations ? DroneStationRelocationUtility.ReservedSlot(reservations, request.RelocationTask, request.RelocationMemberIndex) : -1;
        if (request.RelocationTask != Entity.Null && promised < 0) return -1;
        if (promised >= 0) preferred = promised;
        int first = preferred >= 0 ? preferred : range.Count - range.Dedicated;
        int end = preferred >= 0 ? preferred + 1 : range.Count;
        for (int candidate = first; candidate < end; candidate++)
        {
            if (!StationStorageUtility.IsDroneStorageSlot(candidate, range.Count, range.Dedicated))
                return -1;
            var slot = slots[range.Start + candidate];
            int free = StationStorageUtility.FreeDroneQuantity(new StorageSlotElement(slot.Type, slot.Count),
                reservations, candidate, maxStack, request.RelocationTask, request.RelocationMemberIndex);
            if (free <= 0) continue;
            // 이관 Store는 이미 차감한 예약 한 개분을 사용한다. 일반 Store만 임시 점유량을 늘린다.
            if (promised < 0)
            {
                slot.Type = ItemTypeEnum.Drone;
                slot.Count++;
                slots[range.Start + candidate] = slot;
            }
            return candidate;
        }
        return -1;
    }

}
