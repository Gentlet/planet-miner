using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>철거 화물의 슬롯별 확보량을 기존 정거장 슬롯 경합과 함께 계산한다.</summary>
internal static class DroneDemolitionStorageUtility
{
    internal static int FreeQuantity(DynamicBuffer<DroneStationReservedSlotElement> slots, int index,
        ItemTypeEnum type, int free, Entity worker)
    {
        bool hasClaim = false;
        int own = 0;
        foreach (var slot in slots)
        {
            bool owned = worker != Entity.Null && slot.TransportWorker == worker;
            hasClaim |= owned;
            if (slot.SlotIndex != index) continue;
            if (slot.TransportWorker == Entity.Null || slot.ItemType != type) return 0;
            if (owned) own += slot.Quantity;
            else free -= slot.Quantity;
        }
        return math.max(0, hasClaim ? math.min(free, own) : free);
    }

    private struct RefundNeed
    {
        public ItemTypeEnum Type;
        public int Remaining;
        public int MaxStack;
    }

    // 점유된 스택과 품목이 확정된 예약은 해당 품목만 받는다. 일반 빈 슬롯은 한 품목에만 배정한다.
    internal static bool CanFitRefund(EntityManager manager, Entity registry,
        NativeHashMap<Entity, FixedBitSet> destinations, NativeList<DroneDemolitionRefundElement> costs,
        int firstItem, int firstQuantity)
    {
        if (firstItem == costs.Length) return true;
        if (destinations.IsEmpty || !manager.HasBuffer<ItemConfigElement>(registry)) return false;
        if (firstItem + 1 == costs.Length)
        {
            var type = costs[firstItem].ItemType;
            int remaining = firstQuantity;
            foreach (var destination in destinations)
            {
                if (!destination.Value.IsSet((byte)type)) continue;
                remaining -= DroneSchedulingUtility.CountStorageSpace(manager, destination.Key, type, registry, remaining,
                    generalOnly: true);
                if (remaining == 0) return true;
            }
            return false;
        }

        using var needs = new NativeList<RefundNeed>(costs.Length - firstItem, Allocator.Temp);
        var registeredItems = manager.GetBuffer<ItemConfigElement>(registry, true);
        FixedBitSet allTypes = default;
        for (int i = firstItem; i < costs.Length; i++)
        {
            var type = costs[i].ItemType;
            int maxStack = ItemRegistry.GetMaxStack(registeredItems, type);
            if (maxStack <= 0) return false;
            allTypes.Set((byte)type, true);
            needs.Add(new RefundNeed
            {
                Type = type, Remaining = i == firstItem ? firstQuantity : costs[i].Quantity, MaxStack = maxStack
            });
        }

        using var emptySlots = new NativeList<FixedBitSet>(Allocator.Temp);
        int unrestricted = 0;
        foreach (var destination in destinations)
        {
            Entity storage = destination.Key;
            if (!DroneSchedulingUtility.IsStorage(manager, storage) || manager.HasComponent<PreparingStationDemolition>(storage))
                continue;

            var read = StationStorageUtility.StorageReadContext.Create(manager, storage);
            var reservations = manager.HasBuffer<DroneStationReservedSlotElement>(storage)
                ? manager.GetBuffer<DroneStationReservedSlotElement>(storage, true) : default;
            var filter = manager.HasComponent<StorageFilter>(storage) ? manager.GetComponentData<StorageFilter>(storage) : default;
            int total = read.SlotCount;
            int dedicated = read.DedicatedSlotCount;
            int general = math.min(read.StoredItems.Length, math.min(total, GameConstants.MaxStorageSlots) - dedicated);
            var remaining = needs.AsArray();
            for (int slot = 0; slot < general; slot++)
            {
                bool typed = HasAssignedType(ref read, reservations, slot);
                FixedBitSet accepts = default;
                for (int n = 0; n < remaining.Length; n++)
                {
                    var need = remaining[n];
                    if (!destination.Value.IsSet((byte)need.Type) || !filter.IsItemAllowed(need.Type)) continue;
                    int free = DroneSchedulingUtility.ReadSlotFreeQuantity(ref read, need.Type, slot, need.MaxStack, total, dedicated);
                    if (free <= 0) continue;
                    if (typed)
                    {
                        need.Remaining = math.max(0, need.Remaining - free);
                        remaining[n] = need;
                    }
                    else
                    {
                        accepts.Set((byte)need.Type, true);
                    }
                }
                if (accepts.Part0 != 0 || accepts.Part1 != 0)
                {
                    emptySlots.Add(accepts);
                    if (accepts.Part0 == allTypes.Part0 && accepts.Part1 == allTypes.Part1) unrestricted++;
                }
            }
            if (SlotsNeeded(needs) <= unrestricted) return true;
        }

        if (SlotsNeeded(needs) > emptySlots.Length) return false;
        var choices = emptySlots.AsArray();
        using var owners = new NativeArray<ItemTypeEnum>(choices.Length, Allocator.Temp);
        foreach (var need in needs)
        {
            long slots = ((long)need.Remaining + need.MaxStack - 1) / need.MaxStack;
            for (long n = 0; n < slots; n++)
            {
                FixedBitSet visited = default;
                if (!DroneDemolitionUtility.TryMatchRefundUnit(need.Type, choices, owners, ref visited)) return false;
            }
        }
        return true;
    }

    private static long SlotsNeeded(NativeList<RefundNeed> needs)
    {
        long slots = 0;
        foreach (var need in needs)
        {
            slots += ((long)need.Remaining + need.MaxStack - 1) / need.MaxStack;
        }
        return slots;
    }

    private static bool HasAssignedType(ref StationStorageUtility.StorageReadContext read,
        DynamicBuffer<DroneStationReservedSlotElement> reservations, int slot)
    {
        if (read.StoredItems[slot].Quantity > 0 || read.HasInputSlots) return true;
        if (!reservations.IsCreated) return false;
        foreach (var row in reservations)
        {
            if (row.SlotIndex == slot && row.TransportWorker != Entity.Null) return true;
        }
        return false;
    }

    internal static bool TryReserveRefund(EntityManager manager, Entity storage, Entity registry, Entity task,
        Entity worker, ItemTypeEnum type, int quantity, out int quantityWithoutLastEmptySlot) =>
        TryReserve(manager, storage, registry, task, worker, type, quantity, true, out quantityWithoutLastEmptySlot);

    private static bool TryReserve(EntityManager manager, Entity storage, Entity registry, Entity task,
        Entity worker, ItemTypeEnum type, int quantity, bool matchingStacksFirst, out int quantityWithoutLastEmptySlot)
    {
        quantityWithoutLastEmptySlot = 0;
        if (!manager.HasBuffer<DroneStationReservedSlotElement>(storage) || !manager.HasBuffer<ItemConfigElement>(registry)) return false;
        if (quantity <= 0 || !DroneSchedulingUtility.IsStorage(manager, storage)) return false;
        if (manager.HasComponent<PreparingStationDemolition>(storage) ||
            (manager.HasComponent<StorageFilter>(storage) && !manager.GetComponentData<StorageFilter>(storage).IsItemAllowed(type)))
            return false;

        var reservations = manager.GetBuffer<DroneStationReservedSlotElement>(storage);
        var read = StationStorageUtility.StorageReadContext.Create(manager, storage);
        int max = ItemRegistry.GetMaxStack(manager.GetBuffer<ItemConfigElement>(registry, true), type);
        int total = read.SlotCount;
        int dedicated = read.DedicatedSlotCount;
        int general = math.min(read.StoredItems.Length, math.min(total, GameConstants.MaxStorageSlots) - dedicated);
        int initialRows = reservations.Length;
        int remaining = quantity;
        int passes = matchingStacksFirst ? 2 : 1;
        for (int pass = 0; pass < passes && remaining > 0; pass++)
        {
            for (int slot = 0; slot < general && remaining > 0; slot++)
            {
                if (matchingStacksFirst && HasAssignedType(ref read, reservations, slot) != (pass == 0)) continue;
                int amount = math.min(remaining, DroneSchedulingUtility.ReadSlotFreeQuantity(ref read, type, slot, max, total, dedicated));
                if (amount <= 0) continue;
                if (pass == 1) quantityWithoutLastEmptySlot = quantity - remaining;
                reservations.Add(new DroneStationReservedSlotElement
                {
                    Task = task, SlotIndex = slot, TransportWorker = worker, ItemType = type, Quantity = amount,
                    MemberIndex = -1
                });
                remaining -= amount;
            }
        }
        if (remaining == 0) return true;
        reservations.ResizeUninitialized(initialRows);
        return false;
    }

    internal static bool TryReserveTransport(EntityManager manager, Entity registry,
        in DroneTaskCandidateDecisionElement candidate, int quantity)
    {
        Entity destination = candidate.Destination;
        if (!TryReserve(manager, destination, registry, candidate.Task, candidate.Worker, candidate.ItemType, quantity, false, out _))
            return false;

        bool reachable = !manager.HasComponent<DroneStation>(destination) ||
            DroneDemolitionUtility.CanReachRefundStorage(manager, candidate.Worker, candidate.Source, destination);
        if (!reachable) ResizeClaim(manager, destination, candidate.Worker, 0);
        return reachable;
    }

    internal static bool TestPickupRoute(EntityManager manager, Entity registry, in DroneTaskCandidateDecisionElement candidate,
        int quantity, ref DroneDemolitionUtility.RefundFlightReadContext flightReads)
    {
        Entity destination = candidate.Destination;
        if (!TryReserve(manager, destination, registry, candidate.Task, candidate.Worker, candidate.ItemType, quantity, false, out _))
            return false;

        // 임시 확보 중의 정박 자격으로 경로를 검사하고, 반환한 슬롯은 다음 후보의 검사에 반영한다.
        try
        {
            flightReads.RefreshDockAvailability(manager, destination);
            return DroneDemolitionUtility.CanReachRefundStorage(manager, candidate.Worker, candidate.Source, destination, ref flightReads);
        }
        finally
        {
            ResizeClaim(manager, destination, candidate.Worker, 0);
            flightReads.RefreshDockAvailability(manager, destination);
        }
    }

    internal static int AvailableTransportSpace(EntityManager manager, Entity storage, ItemTypeEnum type,
        Entity registry, int wanted, Entity assignment = default) =>
        manager.HasComponent<PreparingStationDemolition>(storage) ? 0 :
            DroneSchedulingUtility.CountStorageSpace(manager, storage, type, registry, wanted,
                generalOnly: true, deliveryAssignment: assignment);

    internal static void ResizeClaim(EntityManager manager, Entity storage, Entity worker, int quantity)
    {
        if (worker == Entity.Null || !manager.HasBuffer<DroneStationReservedSlotElement>(storage)) return;
        var reservations = manager.GetBuffer<DroneStationReservedSlotElement>(storage);
        for (int i = 0; i < reservations.Length; i++)
        {
            var row = reservations[i];
            if (row.TransportWorker != worker) continue;
            row.Quantity = math.min(quantity, row.Quantity);
            quantity -= row.Quantity;
            if (row.Quantity == 0) reservations.RemoveAt(i--);
            else reservations[i] = row;
        }
    }

    internal static void Consume(EntityManager manager, Entity storage, Entity worker, int slot, int quantity)
    {
        if (worker == Entity.Null || !manager.HasBuffer<DroneStationReservedSlotElement>(storage)) return;
        var reservations = manager.GetBuffer<DroneStationReservedSlotElement>(storage);
        for (int i = 0; i < reservations.Length; i++)
        {
            var row = reservations[i];
            if (row.TransportWorker != worker || row.SlotIndex != slot) continue;
            row.Quantity -= quantity;
            if (row.Quantity <= 0) reservations.RemoveAt(i);
            else reservations[i] = row;
            return;
        }
    }

    internal static int Quantity(EntityManager manager, Entity storage, Entity worker)
    {
        if (worker == Entity.Null || !manager.HasBuffer<DroneStationReservedSlotElement>(storage)) return 0;
        int quantity = 0;
        foreach (var row in manager.GetBuffer<DroneStationReservedSlotElement>(storage, true))
            if (row.TransportWorker == worker) quantity += row.Quantity;
        return quantity;
    }
}
