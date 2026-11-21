using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: DroneExecution 시작에 건물 종료 확정 상태와 Command 차단으로 드론 인계 계획을 준비한다.
/// 입력: 행동 요청/Decision, 현재 배정·현장 예약, 월드 실물 ID 또는 출발 슬롯 수량, 목적지 슬롯 상태.
/// 출력·소유권: 요청별 계획과 이번 드론 단계 공용 출발 예산을 작성한다.
/// 이용: Lifecycle Apply가 접수 순서대로 계획과 최신 수량을 확인하고 성공분을 정산한다.
/// 공급원/공간 예약은 만들지 않는다. 건물 단계 입고·출고는 포함하고 드론 단계 새 수집품·새 공간은 계획에 더하지 않는다.
/// 정리: 같은 요청은 한 번만 준비하며 계획은 행동 반영 뒤 EndSimulation에 제거된다.
/// </summary>
[UpdateInGroup(typeof(DroneExecutionGroup), OrderFirst = true)]
public partial struct DroneItemTransferExecutionSystem : ISystem
{
    private EntityQuery _requests;
    private EntityQuery _stationRequests;
    private Entity _candidates;
    private EntityQuery _registry;
    private EntityQuery _sites;

    public void OnCreate(ref SystemState state)
    {
        _candidates = DroneSchedulingUtility.GetOrCreateCandidates(state.EntityManager);
        _stationRequests = state.GetEntityQuery(typeof(DroneStationRequest), typeof(DroneStationDecision));
        _requests = state.GetEntityQuery(ComponentType.ReadOnly<DroneActionReadyRequest>(),
            ComponentType.ReadWrite<DroneItemTransferDecision>(),
            ComponentType.ReadWrite<DroneItemTransferItemDecisionElement>(),
            ComponentType.ReadWrite<DroneItemTransferSlotDecisionElement>());
        _registry = state.GetEntityQuery(ComponentType.ReadOnly<ItemRegistry>(), ComponentType.ReadOnly<ItemConfigElement>());
        _sites = state.GetEntityQuery(ComponentType.ReadOnly<ConstructionSite>(), ComponentType.ReadOnly<GridPosition>());
    }

    public void OnUpdate(ref SystemState state)
    {
        state.CompleteDependency();
        var manager = state.EntityManager;
        var withdrawals = manager.GetBuffer<DroneInventoryWithdrawalElement>(_candidates);
        withdrawals.Clear();
        using var stationRequests = _stationRequests.ToEntityArray(Allocator.Temp);
        foreach (var requestEntity in stationRequests)
        {
            var request = manager.GetComponentData<DroneStationRequest>(requestEntity);
            if (request.Operation != DroneStationOperation.ActivateStored ||
                !manager.GetComponentData<DroneStationDecision>(requestEntity).Approved ||
                !manager.HasBuffer<StorageSlotElement>(request.Station)) continue;
            var slots = manager.GetBuffer<StorageSlotElement>(request.Station, true);
            if ((uint)request.SourceSlotIndex >= (uint)slots.Length) continue;
            var slot = slots[request.SourceSlotIndex];
            if (slot.ItemType == ItemTypeEnum.Drone)
                DroneItemTransferUtility.CaptureWithdrawal(withdrawals, request.Station, false,
                    request.SourceSlotIndex, slot.ItemType, slot.Quantity);
        }
        using var sites = _sites.ToEntityArray(Allocator.Temp);
        Entity registry = _registry.IsEmptyIgnoreFilter ? Entity.Null : _registry.GetSingletonEntity();
        // 실제 행동은 작업 생성 우선순위와 별개인 World 접수 순서를 따른다.
        using var ordered = DroneActionRequestUtility.ReadInReceiptOrder(manager, _requests, Allocator.Temp);
        for (int i = 0; i < ordered.Length; i++)
        {
            PrepareRequest(manager, ordered[i].Entity, ordered[i].Request, registry, sites, withdrawals);
        }
    }

    private static void PrepareRequest(EntityManager manager, Entity entity, in DroneActionReadyRequest request,
        Entity registry, NativeArray<Entity> sites, DynamicBuffer<DroneInventoryWithdrawalElement> withdrawals)
    {
        var decision = manager.GetComponentData<DroneItemTransferDecision>(entity);
        // 같은 실행 단계 재진입으로 이번 틱에 새로 생긴 재고·공간을 계획에 추가하지 않는다.
        if (decision.Prepared) return;
        var items = manager.GetBuffer<DroneItemTransferItemDecisionElement>(entity);
        var slots = manager.GetBuffer<DroneItemTransferSlotDecisionElement>(entity);
        items.Clear();
        slots.Clear();
        decision.Prepared = true;
        decision.WantedQuantity = 0;
        decision.MaximumQuantity = 0;
        decision.InputQuantityLimit = int.MaxValue;
        if (!decision.CanExecute || manager.HasComponent<DroneItemTransferResult>(entity) ||
            !DroneItemTransferValidationUtility.TryValidate(manager, request, out var assignment))
        {
            decision.CanExecute = false;
            manager.SetComponentData(entity, decision);
            return;
        }

        // 작업이 원한 수량과 실제 계획 가능한 상한을 구분한다.
        // 수집/공급은 기존 현장 예약, 보관은 현재 슬롯 공간, 방출은 기존 적재 실물로 상한을 제한한다.
        switch (request.Action.Kind)
        {
            case DroneActionKindEnum.CollectFromStorage:
                decision.WantedQuantity = assignment.AssignedQuantity;
                decision.MaximumQuantity = PrepareCollection(manager, request.Action.Assignment, assignment, items, withdrawals);
                break;
            case DroneActionKindEnum.RecoverWorldItem:
                decision.WantedQuantity = 1;
                if (DroneWorldItemRecoveryUtility.CanRecoverAssignedItem(manager, assignment, sites))
                {
                    items.Add(new DroneItemTransferItemDecisionElement { ItemEntity = assignment.Source });
                }
                decision.MaximumQuantity = items.Length;
                break;
            case DroneActionKindEnum.SupplyConstructionSite:
            {
                var cargo = manager.GetComponentData<DroneCargoState>(assignment.Worker);
                decision.WantedQuantity = cargo.Quantity;
                decision.MaximumQuantity = PrepareSupply(manager, request.Action.Assignment, assignment, cargo.Quantity);
                break;
            }
            case DroneActionKindEnum.StoreCargo:
            {
                var cargo = manager.GetComponentData<DroneCargoState>(assignment.Worker);
                decision.WantedQuantity = cargo.Quantity;
                int capacity = CaptureStorageSlots(manager, assignment, registry, slots,
                    decision.WantedQuantity, request.Action.Assignment);
                decision.MaximumQuantity = math.min(capacity, cargo.Quantity);
                if (assignment.DestinationArea == DroneTransportAreaEnum.Input)
                {
                    decision.InputQuantityLimit = DroneTransportEndpointUtility.InputRemaining(manager, assignment.Destination, assignment.ItemType);
                    decision.MaximumQuantity = math.min(decision.MaximumQuantity, decision.InputQuantityLimit);
                }
                if (manager.HasComponent<DroneTransportReservation>(request.Action.Assignment))
                {
                    var task = manager.GetComponentData<DroneLogisticsTask>(assignment.Task);
                    decision.MaximumQuantity = math.min(decision.MaximumQuantity,
                        DroneTransportEndpointUtility.DestinationSpace(manager, task, assignment.Destination, registry,
                            decision.WantedQuantity, request.Action.Assignment));
                    decision.MaximumQuantity = math.min(decision.MaximumQuantity,
                        manager.GetComponentData<DroneTransportReservation>(request.Action.Assignment).RemainingQuantity);
                }
                break;
            }
            case DroneActionKindEnum.DropCargo:
            {
                var cargo = manager.GetComponentData<DroneCargoState>(assignment.Worker);
                decision.WantedQuantity = cargo.Quantity;
                decision.MaximumQuantity = cargo.Quantity;
                break;
            }
        }
        manager.SetComponentData(entity, decision);
    }

    private static int PrepareCollection(EntityManager manager, Entity entity, in DroneTaskAssignment assignment,
        DynamicBuffer<DroneItemTransferItemDecisionElement> candidates, DynamicBuffer<DroneInventoryWithdrawalElement> withdrawals)
    {
        int limit = DroneItemTransferUtility.CollectionLimit(manager, entity, assignment);
        if (limit <= 0) return 0;
        var task = manager.GetComponentData<DroneLogisticsTask>(assignment.Task);
        Entity source = assignment.Source;
        ItemTypeEnum type = assignment.ItemType;
        if (type == ItemTypeEnum.Drone && manager.HasComponent<PreparingStationDemolition>(source)) return 0;
        bool product = assignment.SourceArea == DroneTransportAreaEnum.Product;
        int planned = 0;
        if (product)
        {
            foreach (var slot in manager.GetBuffer<ProductSlotElement>(source, true))
            {
                if (slot.ItemType != type) continue;
                candidates.Add(new DroneItemTransferItemDecisionElement { SlotIndex = slot.SlotIndex, Quantity = slot.Quantity });
                DroneItemTransferUtility.CaptureWithdrawal(withdrawals, source, true, slot.SlotIndex, type, slot.Quantity);
                planned += math.min(limit - planned, slot.Quantity);
            }
            return planned;
        }

        var storage = manager.GetBuffer<StorageSlotElement>(source, true);
        int count = DroneSchedulingUtility.IsTransportKind(task.Kind) && assignment.SourceArea == DroneTransportAreaEnum.GeneralStorage
            ? storage.Length - StationStorageUtility.ReadDedicatedCount(manager, source)
            : storage.Length;
        bool recoveryOnly = task.Purpose == DroneTransportPurposeEnum.ResearchRecovery;
        var recovery = recoveryOnly ? manager.GetBuffer<ResearchRecoveryElement>(source, true) : default;
        for (int index = 0; index < count; index++)
        {
            var slot = storage[index];
            if (slot.ItemType != type || slot.Quantity <= 0) continue;
            int eligible = recoveryOnly
                ? math.min(slot.Quantity, ResearchRecoveryUtility.ReadSlotQuantity(recovery, index, type))
                : slot.Quantity;
            if (eligible <= 0) continue;
            candidates.Add(new DroneItemTransferItemDecisionElement { SlotIndex = index, Quantity = eligible });
            DroneItemTransferUtility.CaptureWithdrawal(withdrawals, source, false, index, type, slot.Quantity);
            planned += math.min(limit - planned, eligible);
        }
        return planned;
    }

    private static int PrepareSupply(EntityManager manager, Entity entity, in DroneTaskAssignment assignment, int cargo)
    {
        if (!DroneSchedulingUtility.IsSite(manager, assignment.Destination)) return 0;
        var reservation = manager.GetComponentData<ConstructionSupplyReservation>(entity);
        int limit = math.min(reservation.RemainingQuantity, ConstructionSupplyReservationUtility.RemainingIncludingOwn(
            manager, assignment.Destination, assignment.ItemType, reservation.RemainingQuantity));
        return math.min(limit, cargo);
    }

    private static int CaptureStorageSlots(EntityManager manager, in DroneTaskAssignment assignment, Entity registry,
        DynamicBuffer<DroneItemTransferSlotDecisionElement> snapshot, int wanted, Entity deliveryAssignment)
    {
        Entity storage = assignment.Destination;
        ItemTypeEnum type = assignment.ItemType;
        if (type == ItemTypeEnum.Drone && manager.HasComponent<PreparingStationDemolition>(storage) &&
            !DroneStationRelocationUtility.CanReceiveCommittedDroneCargo(manager, storage, deliveryAssignment)) return 0;
        // 이 슬롯 기록은 런타임 Storage의 복제 원본이 아니라 이번 행동에 허용할 공간 상한이다.
        // 뒤의 출고로 공간이 늘어도 상한을 확장하지 않고, 실제 성공분만 Lifecycle의 슬롯 예산에서 차감한다.
        if (DroneSchedulingUtility.FindStorageSlot(manager, storage, type, registry, assignment.DestinationArea, assignment.Worker) < 0)
        {
            return 0;
        }
        int maxStack = ItemRegistry.GetMaxStack(manager.GetBuffer<ItemConfigElement>(registry, true), type);
        var storageRead = StationStorageUtility.StorageReadContext.Create(manager, storage);
        int slotCount = math.min(storageRead.SlotCount, GameConstants.MaxStorageSlots);
        var stored = storageRead.StoredItems;
        bool hasInputSlots = storageRead.HasInputSlots;
        int available = 0;
        for (int slot = 0; slot < slotCount; slot++)
        {
            var original = stored[slot];
            var currentType = original.ItemType;
            int count = original.Quantity;
            snapshot.Add(new DroneItemTransferSlotDecisionElement
            {
                SlotIndex = slot,
                ItemType = currentType,
                ItemCount = count
            });
            bool allowed = StationStorageUtility.IsItemSlotAllowed(slot, slotCount,
                storageRead.DedicatedSlotCount, type) &&
                !storageRead.IsDockOccupied(slot);
            if (hasInputSlots)
            {
                var inputs = storageRead.InputSlots;
                allowed &= slot < inputs.Length && inputs[slot].ItemType == type;
            }
            if (!allowed || (currentType != ItemTypeEnum.None && currentType != type)) continue;
            int free = storageRead.FreeAfterReservations(slot, type, math.max(0, maxStack - count), assignment.Worker);
            available += math.min(math.max(0, wanted - available), free);
        }
        return available;
    }
}
