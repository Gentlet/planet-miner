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

internal struct DroneStationVisualChange
{
    public Entity Actor;
    public Entity Prefab;
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

/// <summary>요청 순서/작업 경합/한 호출의 슬롯 예산을 처리한다. 관리형 표시 접근은 마지막 메서드에만 둔다.</summary>
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
            if (!transportPending.HasBuffer(owner))
            {
                continue;
            }
            foreach (var entry in transportPending[owner])
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

    public static int ReserveSlot(Entity station, Entity drone, DroneStationOperation operation, int preferred, int maxStack,
        ComponentLookup<Storage> storages, ComponentLookup<DroneStationStorage> dedicated,
        BufferLookup<StoredItemElement> items, BufferLookup<DockedDroneElement> docked,
        NativeParallelHashMap<Entity, DroneStationSlotRange> ranges, NativeList<DroneStationSlot> slots,
        BufferLookup<DroneStationReservedSlotElement> reserved)
    {
        if (!ranges.TryGetValue(station, out var range))
        {
            range = new DroneStationSlotRange
            {
                Start = slots.Length, Count = storages[station].SlotCount, Dedicated = dedicated[station].DedicatedSlotCount
            };
            for (int index = 0; index < range.Count; index++)
            {
                slots.Add(default);
            }
            foreach (var item in items[station])
            {
                if (item.SlotIndex < 0 || item.SlotIndex >= range.Count)
                {
                    continue;
                }
                int index = range.Start + item.SlotIndex;
                var slot = slots[index];
                slot.Type = item.ItemType;
                slot.Count++;
                slots[index] = slot;
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
        int promised = DroneStationRelocationUtility.ReservedSlot(reserved, station, drone);
        if (promised >= 0) preferred = promised;
        for (int index = 0; index < range.Count; index++)
        {
            int candidate = preferred >= 0 ? preferred : StationStorageUtility.SearchSlot(index, range.Count, range.Dedicated, ItemTypeEnum.Drone);
            var slot = slots[range.Start + candidate];
            if (DroneStationRelocationUtility.IsReserved(reserved, station, candidate, drone)) continue;
            bool docking = operation == DroneStationOperation.Dock;
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

    /// <summary>데이터 Job의 승인된 표시 목록만 읽는다. 역할/소유권/결과와 같은 EndSimulation에 기록한다.</summary>
    public static void RecordManagedAppearance(EntityManager manager, EntityCommandBuffer ecb, NativeArray<DroneStationVisualChange> changes)
    {
        foreach (var change in changes)
        {
            if (manager.HasComponent<RenderMeshArray>(change.Prefab))
            {
                ecb.AddSharedComponentManaged(change.Actor, manager.GetSharedComponentManaged<RenderMeshArray>(change.Prefab));
            }
        }
    }
}
