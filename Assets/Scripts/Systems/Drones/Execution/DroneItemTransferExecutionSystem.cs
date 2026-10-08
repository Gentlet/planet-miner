using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: DroneExecution 시작에 건물 종료 확정 상태와 Command 차단으로 드론 인계 계획을 준비한다.
/// 입력: 행동 요청/Decision, 현재 배정·현장 예약, 이미 존재하는 실물 ID와 목적지 저장 슬롯 상태.
/// 출력·소유권: 요청 엔티티의 수량 상한과 실물/슬롯 Decision 버퍼만 작성한다. 실물·Owner·예약·배정은 쓰지 않는다.
/// 이용: Lifecycle Apply가 접수 순서대로 계획 안의 실물·대상 상태를 재검사하고 공통 Ownership API에 인계를 위임한다.
/// 공급원/공간 예약은 만들지 않는다. 건물 단계 입고·출고는 포함하고 드론 단계 새 수집품·새 공간은 계획에 더하지 않는다.
/// 정리: 같은 요청은 한 번만 준비하며 계획은 행동 반영 뒤 EndSimulation에 제거된다.
/// </summary>
[UpdateInGroup(typeof(DroneExecutionGroup), OrderFirst = true)]
public partial struct DroneItemTransferExecutionSystem : ISystem
{
    private EntityQuery _requests;
    private EntityQuery _registry;
    private EntityQuery _sites;

    private struct PendingAction
    {
        public Entity Entity;
        public DroneActionReadyRequest Request;
    }

    private struct ReceiptComparer : IComparer<PendingAction>
    {
        public int Compare(PendingAction first, PendingAction second)
        {
            int order = first.Request.ReceiptSequence.CompareTo(second.Request.ReceiptSequence);
            if (order != 0) return order;
            order = first.Entity.Index.CompareTo(second.Entity.Index);
            return order != 0 ? order : first.Entity.Version.CompareTo(second.Entity.Version);
        }
    }

    public void OnCreate(ref SystemState state)
    {
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
        using var entities = _requests.ToEntityArray(Allocator.Temp);
        using var sites = _sites.ToEntityArray(Allocator.Temp);
        using var ordered = new NativeList<PendingAction>(entities.Length, Allocator.Temp);
        Entity registry = _registry.IsEmptyIgnoreFilter ? Entity.Null : _registry.GetSingletonEntity();
        for (int i = 0; i < entities.Length; i++)
        {
            ordered.Add(new PendingAction
            {
                Entity = entities[i], Request = manager.GetComponentData<DroneActionReadyRequest>(entities[i])
            });
        }
        // 작업 생성 우선순위와 별개로 실제 행동은 World 접수 순번을 따른다.
        // 여기서는 각 계획만 준비하고 실물/공간 경합의 성공분은 Lifecycle Apply가 정산한다.
        ordered.AsArray().Sort(new ReceiptComparer());
        for (int i = 0; i < ordered.Length; i++)
        {
            PrepareRequest(manager, ordered[i].Entity, ordered[i].Request, registry, sites);
        }
    }

    private static void PrepareRequest(EntityManager manager, Entity entity, in DroneActionReadyRequest request,
        Entity registry, NativeArray<Entity> sites)
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
                decision.MaximumQuantity = PrepareCollection(manager, request.Action.Assignment, assignment, items);
                break;
            case DroneActionKindEnum.RecoverWorldItem:
                decision.WantedQuantity = 1;
                PrepareRecovery(manager, assignment, items, sites);
                decision.MaximumQuantity = items.Length;
                break;
            case DroneActionKindEnum.SupplyConstructionSite:
                decision.WantedQuantity = manager.GetBuffer<StoredItemElement>(assignment.Worker, true).Length;
                decision.MaximumQuantity = PrepareSupply(manager, request.Action.Assignment, assignment, items);
                break;
            case DroneActionKindEnum.StoreCargo:
                decision.WantedQuantity = manager.GetBuffer<StoredItemElement>(assignment.Worker, true).Length;
                int capacity = CaptureStorageSlots(manager, assignment.Destination, assignment.ItemType,
                    registry, slots, decision.WantedQuantity);
                SelectStoredItems(manager, assignment.Worker, assignment.ItemType, items);
                decision.MaximumQuantity = math.min(capacity, items.Length);
                break;
            case DroneActionKindEnum.DropCargo:
                decision.WantedQuantity = manager.GetBuffer<StoredItemElement>(assignment.Worker, true).Length;
                SelectStoredItems(manager, assignment.Worker, assignment.ItemType, items);
                decision.MaximumQuantity = items.Length;
                break;
        }
        manager.SetComponentData(entity, decision);
    }

    private static int PrepareCollection(EntityManager manager, Entity entity, in DroneTaskAssignment assignment,
        DynamicBuffer<DroneItemTransferItemDecisionElement> items)
    {
        if (!DroneSchedulingUtility.IsSite(manager, assignment.Destination)) return 0;
        if (!DroneSchedulingUtility.IsStorage(manager, assignment.Source)) return 0;
        var reservation = manager.GetComponentData<ConstructionSupplyReservation>(entity);
        int limit = math.min(assignment.AssignedQuantity, reservation.RemainingQuantity);
        limit = math.min(limit, ConstructionSupplyReservationUtility.RemainingIncludingOwn(manager,
            assignment.Destination, assignment.ItemType, reservation.RemainingQuantity));
        // 모든 기존 실물을 남겨 선행 드론이 일부를 가져간 뒤에도 다른 기존 실물을 선택할 수 있다.
        SelectStoredItems(manager, assignment.Source, assignment.ItemType, items);
        return math.min(limit, items.Length);
    }

    private static void PrepareRecovery(EntityManager manager, in DroneTaskAssignment assignment,
        DynamicBuffer<DroneItemTransferItemDecisionElement> items, NativeArray<Entity> sites)
    {
        // 회수는 지정된 월드 실물 하나만 계획한다. 현장 정리 회수라면 현재도 현장 아래에 있어야 한다.
        // 이후 적재 출처가 Recovery로 유지되어 보관처 입고 전에는 건설 공급으로 전용되지 않는다.
        if (!DroneSchedulingUtility.IsStorage(manager, assignment.Destination)) return;
        if (!DroneItemTransferUtility.CanMove(manager, assignment.Source, assignment.ItemType, Entity.Null)) return;
        var task = manager.GetComponentData<DroneLogisticsTask>(assignment.Task);
        if (task.RecoveryReason != DroneRecoveryReasonEnum.DroneDrop)
        {
            if (task.RecoveryReason != DroneRecoveryReasonEnum.SiteClearance) return;
            if (!DroneSchedulingUtility.IsUnderSite(manager, assignment.Source, sites)) return;
        }
        items.Add(new DroneItemTransferItemDecisionElement { ItemEntity = assignment.Source });
    }

    private static int PrepareSupply(EntityManager manager, Entity entity, in DroneTaskAssignment assignment,
        DynamicBuffer<DroneItemTransferItemDecisionElement> items)
    {
        // 자기 예약을 포함한 남은 필요량까지만 계획하고 이번 틱에 새로 수집될 실물은 포함하지 않는다.
        if (!DroneSchedulingUtility.IsSite(manager, assignment.Destination)) return 0;
        if (!manager.HasBuffer<StoredItemElement>(assignment.Destination)) return 0;
        var reservation = manager.GetComponentData<ConstructionSupplyReservation>(entity);
        int limit = math.min(reservation.RemainingQuantity, ConstructionSupplyReservationUtility.RemainingIncludingOwn(
            manager, assignment.Destination, assignment.ItemType, reservation.RemainingQuantity));
        SelectStoredItems(manager, assignment.Worker, assignment.ItemType, items);
        return math.min(limit, items.Length);
    }

    private static void SelectStoredItems(EntityManager manager, Entity owner, ItemTypeEnum type,
        DynamicBuffer<DroneItemTransferItemDecisionElement> selected)
    {
        var stored = manager.GetBuffer<StoredItemElement>(owner, true);
        for (int i = 0; i < stored.Length; i++)
        {
            var entry = stored[i];
            if (entry.ItemType != type) continue;
            if (!DroneItemTransferUtility.CanMove(manager, entry.ItemEntity, type, owner)) continue;
            bool duplicate = false;
            for (int j = 0; j < selected.Length; j++) duplicate |= selected[j].ItemEntity == entry.ItemEntity;
            if (duplicate) continue;
            selected.Add(new DroneItemTransferItemDecisionElement { ItemEntity = entry.ItemEntity });
        }
    }

    private static int CaptureStorageSlots(EntityManager manager, Entity storage, ItemTypeEnum type, Entity registry,
        DynamicBuffer<DroneItemTransferSlotDecisionElement> snapshot, int wanted)
    {
        // 이 슬롯 기록은 런타임 Storage의 복제 원본이 아니라 이번 행동에 허용할 공간 상한이다.
        // 뒤의 출고로 공간이 늘어도 상한을 확장하지 않고, 실제 성공분만 Lifecycle의 슬롯 예산에서 차감한다.
        if (DroneSchedulingUtility.FindStorageSlot(manager, storage, type, registry) < 0) return 0;
        int maxStack = ItemRegistry.GetMaxStack(manager.GetBuffer<ItemConfigElement>(registry, true), type);
        int slotCount = math.min(manager.GetComponentData<Storage>(storage).SlotCount, GameConstants.MaxStorageSlots);
        var stored = manager.GetBuffer<StoredItemElement>(storage, true);
        bool hasInputSlots = manager.HasBuffer<BuildingInputSlotElement>(storage);
        int available = 0;
        for (int slot = 0; slot < slotCount; slot++)
        {
            int count = 0;
            ItemTypeEnum currentType = ItemTypeEnum.None;
            bool mixed = false;
            for (int i = 0; i < stored.Length; i++)
            {
                if (stored[i].SlotIndex != slot) continue;
                if (count == 0) currentType = stored[i].ItemType;
                else mixed |= currentType != stored[i].ItemType;
                count++;
            }
            snapshot.Add(new DroneItemTransferSlotDecisionElement
            {
                SlotIndex = slot,
                ItemType = mixed ? ItemTypeEnum.None : currentType,
                ItemCount = mixed ? math.max(maxStack, count) : count
            });
            bool allowed = true;
            if (hasInputSlots)
            {
                var inputs = manager.GetBuffer<BuildingInputSlotElement>(storage, true);
                allowed = slot < inputs.Length && inputs[slot].ItemType == type;
            }
            if (!allowed || mixed || (currentType != ItemTypeEnum.None && currentType != type)) continue;
            available += math.min(math.max(0, wanted - available), math.max(0, maxStack - count));
        }
        return available;
    }
}
