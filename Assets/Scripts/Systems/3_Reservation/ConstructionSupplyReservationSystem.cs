using Unity.Collections;
using Unity.Entities;

/// <summary>
/// 역할·목적: Reservation에서 수행자별 실행 가능한 후보를 선택하고 현장 공급 수량 경합을 중재한다.
/// 입력: 공통 후보 엔티티의 Decision 후보, 기존 배정·개별 예약, 현장 요구량과 공통 적재량.
/// 출력·소유권: 현장 ReservedQuantity와 기존 개별 예약의 해제량, 후보 Selected/Quantity/CommittedQuantity를 쓴다.
/// 공급원 재고·보관 공간은 예약하지 않으며, 회수 실물 중복 선택은 이번 처리의 로컬 집합으로 막는다.
/// 이용·가시화: StateApply의 Publish가 후보를 최종 재검사하고 배정 공개 또는 실패 예약 롤백을 기록한다.
/// 정리: 이전 틱 미공개 예약은 먼저 해제한다. 새 미공개 기록은 다음 틱 정산까지 후보 엔티티에 보존한다.
/// </summary>
[UpdateInGroup(typeof(ReservationGroup))]
public partial struct ConstructionSupplyReservationSystem : ISystem
{
    private Entity _candidates;
    private EntityQuery _assignments;
    private EntityQuery _registry;
    private EntityQuery _sites;
    private EntityQuery _capacity;

    public void OnCreate(ref SystemState state)
    {
        _candidates = DroneSchedulingUtility.GetOrCreateCandidates(state.EntityManager);
        _assignments = state.GetEntityQuery(ComponentType.ReadOnly<DroneTaskAssignment>());
        _registry = state.GetEntityQuery(ComponentType.ReadOnly<ItemRegistry>(), ComponentType.ReadOnly<ItemConfigElement>());
        _sites = state.GetEntityQuery(ComponentType.ReadOnly<ConstructionSite>(), ComponentType.ReadOnly<GridPosition>());
        _capacity = state.GetEntityQuery(ComponentType.ReadOnly<DroneCapacityState>());
    }

    public void OnUpdate(ref SystemState state)
    {
        state.CompleteDependency();
        var manager = state.EntityManager;
        int carryingCapacity = DroneSchedulingUtility.ReadCarryingCapacity(_capacity);
        var candidates = manager.GetBuffer<DroneTaskCandidateDecisionElement>(_candidates);
        // 이전 틱 미공개 CommittedQuantity도 현장 예약 합계에 포함된다.
        // Decision이 보존한 기록으로 먼저 합계를 되돌려 새 후보가 낡은 예약에 막히지 않게 한다.
        for (int i = candidates.Length - 1; i >= 0; i--)
        {
            var pending = candidates[i];
            if (pending.Published || pending.CommittedQuantity <= 0) continue;
            ConstructionSupplyReservationUtility.Release(manager, pending.Destination, pending.ItemType, pending.CommittedQuantity);
            candidates.RemoveAt(i);
        }
        using var usedWorkers = new NativeHashSet<Entity>(16, Allocator.Temp);
        using var claimedItems = new NativeHashSet<Entity>(16, Allocator.Temp);
        using var assignments = _assignments.ToEntityArray(Allocator.Temp);
        using var sites = _sites.ToEntityArray(Allocator.Temp);
        // 배정 상태를 닫기 전에 수량 예약만 해제한다. 연결/종료 상태 변경은 Lifecycle Apply의 책임이다.
        // 정상 배정의 드론과 회수 실물은 새 작업에 중복 선택하지 않는다.
        for (int i = 0; i < assignments.Length; i++)
        {
            Entity entity = assignments[i];
            var assignment = manager.GetComponentData<DroneTaskAssignment>(entity);
            if (DroneSchedulingUtility.AssignmentNeedsCleanup(manager, assignment, sites))
            {
                if (manager.HasComponent<ConstructionSupplyReservation>(entity))
                {
                    var reservation = manager.GetComponentData<ConstructionSupplyReservation>(entity);
                    ConstructionSupplyReservationUtility.Release(manager, reservation.Site, reservation.ItemType, reservation.RemainingQuantity);
                    reservation.RemainingQuantity = 0;
                    manager.SetComponentData(entity, reservation);
                }
                continue;
            }
            usedWorkers.Add(assignment.Worker);
            if (assignment.NextAction == DroneActionKindEnum.RecoverWorldItem) claimedItems.Add(assignment.Source);
        }

        Entity registry = _registry.IsEmptyIgnoreFilter ? Entity.Null : _registry.GetSingletonEntity();
        // 적재품 재배정을 신규 수집보다 먼저 선택한다. 기존 실물에는 변경된 공통 적재량을 소급 적용하지 않는다.
        SelectRetargetCandidates(manager, candidates, registry, carryingCapacity, usedWorkers);
        for (int i = 0; i < candidates.Length; i++)
        {
            Entity worker = candidates[i].Worker;
            if (usedWorkers.Contains(worker)) continue;
            int supplyHead = -1;
            int recoveryHead = -1;
            // 실행 가능한 공급끼리는 현장 PlacementStamp→작업 생성 순번, 회수끼리는 생성 순번으로 선두를 고른다.
            for (int c = 0; c < candidates.Length; c++)
            {
                var candidate = candidates[c];
                if (candidate.Assignment != Entity.Null || candidate.Worker != worker || candidate.Selected ||
                    DroneSchedulingUtility.CandidateQuantity(manager, candidate, registry, carryingCapacity) <= 0) continue;
                var task = manager.GetComponentData<DroneLogisticsTask>(candidate.Task);
                if (task.Kind == DroneLogisticsTaskKindEnum.ConstructionSupply)
                {
                    if (supplyHead < 0 || DroneSchedulingUtility.EarlierTask(manager, candidate.Task, candidates[supplyHead].Task))
                        supplyHead = c;
                }
                else if (!claimedItems.Contains(candidate.Source))
                {
                    if (recoveryHead < 0 || DroneSchedulingUtility.EarlierTask(manager, candidate.Task, candidates[recoveryHead].Task))
                        recoveryHead = c;
                }
            }

            // 공급/회수 전체를 한 정렬로 섞지 않고 각 선두의 생성 순번을 비교한다.
            int selected = supplyHead;
            if (selected < 0)
            {
                selected = recoveryHead;
            }
            else if (recoveryHead >= 0)
            {
                ulong supplyOrder = manager.GetComponentData<DroneLogisticsTask>(candidates[supplyHead].Task).CreationSequence;
                ulong recoveryOrder = manager.GetComponentData<DroneLogisticsTask>(candidates[recoveryHead].Task).CreationSequence;
                if (recoveryOrder < supplyOrder) selected = recoveryHead;
            }

            if (selected < 0) continue;
            var chosen = candidates[selected];
            var chosenTask = manager.GetComponentData<DroneLogisticsTask>(chosen.Task);
            int quantity = DroneSchedulingUtility.CandidateQuantity(manager, chosen, registry, carryingCapacity);
            if (chosenTask.Kind == DroneLogisticsTaskKindEnum.ConstructionSupply)
            {
                // 실제 확보량을 후보에 남겨 공개 실패 시 같은 수량을 롤백할 수 있게 한다.
                chosen.CommittedQuantity = ConstructionSupplyReservationUtility.Reserve(manager,
                    chosen.Destination, chosen.ItemType, quantity);
                quantity = chosen.CommittedQuantity;
            }
            else
            {
                claimedItems.Add(chosen.Source);
            }

            if (quantity <= 0) continue;
            chosen.Quantity = quantity;
            chosen.Selected = true;
            candidates[selected] = chosen;
            usedWorkers.Add(worker);
        }
    }

    private static void SelectRetargetCandidates(EntityManager manager,
        DynamicBuffer<DroneTaskCandidateDecisionElement> candidates, Entity registry, int capacity,
        NativeHashSet<Entity> usedWorkers)
    {
        var ordered = new NativeList<int>(Allocator.Temp);
        try
        {
            // 목적 작업이 바뀌어도 최초 작업 생성 순번을 유지하여 오래 운반 중인 자재부터 목적지를 확보한다.
            for (int i = 0; i < candidates.Length; i++)
            {
                if (candidates[i].Assignment == Entity.Null || candidates[i].Selected) continue;
                int insert = ordered.Length;
                ordered.Add(i);
                while (insert > 0 && EarlierRetarget(manager, candidates[i], candidates[ordered[insert - 1]]))
                {
                    ordered[insert] = ordered[insert - 1];
                    insert--;
                }
                ordered[insert] = i;
            }
            for (int i = 0; i < ordered.Length; i++)
            {
                int index = ordered[i];
                var candidate = candidates[index];
                if (usedWorkers.Contains(candidate.Worker)) continue;
                int quantity = DroneSchedulingUtility.CandidateQuantity(manager, candidate, registry, capacity);
                if (quantity <= 0) continue;
                if (candidate.NextAction == DroneActionKindEnum.SupplyConstructionSite)
                {
                    candidate.CommittedQuantity = ConstructionSupplyReservationUtility.Reserve(manager,
                        candidate.Destination, candidate.ItemType, quantity);
                    quantity = candidate.CommittedQuantity;
                }
                if (quantity <= 0) continue;
                candidate.Quantity = quantity;
                candidate.Selected = true;
                candidates[index] = candidate;
                usedWorkers.Add(candidate.Worker);
            }
        }
        finally
        {
            ordered.Dispose();
        }
    }

    private static bool EarlierRetarget(EntityManager manager, in DroneTaskCandidateDecisionElement left,
        in DroneTaskCandidateDecisionElement right)
    {
        ulong leftOrder = RetargetOrder(manager, left.Assignment);
        ulong rightOrder = RetargetOrder(manager, right.Assignment);
        if (leftOrder != rightOrder) return leftOrder < rightOrder;
        return left.Assignment.Index < right.Assignment.Index;
    }

    private static ulong RetargetOrder(EntityManager manager, Entity entity)
    {
        if (!manager.HasComponent<DroneTaskAssignment>(entity)) return ulong.MaxValue;
        return DroneSchedulingUtility.AssignmentCreationSequence(manager,
            manager.GetComponentData<DroneTaskAssignment>(entity));
    }
}
