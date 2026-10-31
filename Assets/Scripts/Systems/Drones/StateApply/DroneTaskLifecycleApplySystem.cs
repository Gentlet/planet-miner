using Unity.Collections;
using Unity.Entities;
using System.Collections.Generic;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: StateApply에서 행동 신호와 작업/배정 무효화 의도를 반영하고 실제 인계량을 정산한다.
/// 입력: 요청 엔티티의 DroneActionReadyRequest와 DroneItemTransferDecisionSystem/DroneItemTransferExecutionSystem이 준비한 건물 종료 인계 계획,
/// DroneTaskDecisionSystem이 후보 관리 엔티티에 작성한 DroneTaskInvalidationDecisionElement 및 최신 배정·실물·현장 상태를 읽는다.
/// 이용·소유권: 배정 엔티티의 상태/행동 번호/개별 예약, 수행자의 적재 출처/배정 연결,
/// 현장의 도착량/예약 합계는 이 경계에서 갱신한다. 실물 변경은 ItemOwnershipApplySystem 공통 API에 위임한다.
/// 정리·가시화: 값 변경은 즉시 반영하고 요청/계획 제거·결과 게시·참조 없는 종료 엔티티 삭제는 EndSimulation에 기록한다.
/// 미소비 행동 결과나 미정산 예약이 참조하는 배정은 보존하며, 생성·경로 명령과 신규 배정 공개는 다른 시스템이 소유한다.
/// </summary>
[UpdateInGroup(typeof(DroneStateApplyGroup))]
public partial struct DroneTaskLifecycleApplySystem : ISystem
{
    private Entity _candidates;
    private EntityQuery _tasks;
    private EntityQuery _sites;
    private EntityQuery _assignments;
    private EntityQuery _actionRequests;
    private EntityQuery _actionResults;

    public void OnCreate(ref SystemState state)
    {
        _candidates = DroneSchedulingUtility.GetOrCreateCandidates(state.EntityManager);
        _taskRequests = state.GetEntityQuery(typeof(DroneTaskRequest), typeof(DroneTaskRequestDecision));
        _tasks = state.GetEntityQuery(ComponentType.ReadWrite<DroneLogisticsTask>());
        _sites = state.GetEntityQuery(ComponentType.ReadOnly<ConstructionSite>(), ComponentType.ReadOnly<GridPosition>());
        _assignments = state.GetEntityQuery(ComponentType.ReadWrite<DroneTaskAssignment>());
        _actionRequests = state.GetEntityQuery(ComponentType.ReadOnly<DroneActionReadyRequest>());
        _actionResults = state.GetEntityQuery(ComponentType.ReadOnly<DroneItemTransferResult>());
        InitializeActionTransfer(ref state);
    }

    public void OnDestroy(ref SystemState state)
    {
        DisposeActionTransfer();
    }

    public void OnUpdate(ref SystemState state)
    {
        // 일반 Ownership Job이 끝난 실물 상태로 계획을 재검사한다. 결정 당시 Owner를 그대로 신뢰하지 않는다.
        state.CompleteDependency();
        var manager = state.EntityManager;
        var ecb = state.World.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>().CreateCommandBuffer();
        ApplyActionTransfers(manager, ecb);
        ApplyDemolitionDecisions(manager, ecb);
        // 행동 반영 뒤의 배정 상태로 무효화 의도를 재검사한다. 요청/결과 참조는 ECB 재생 전까지 남겨 삭제를 보류한다.
        using var tasks = _tasks.ToEntityArray(Allocator.Temp);
        using var sites = _sites.ToEntityArray(Allocator.Temp);
        using var assignments = _assignments.ToEntityArray(Allocator.Temp);
        using var requests = _actionRequests.ToComponentDataArray<DroneActionReadyRequest>(Allocator.Temp);
        using var results = _actionResults.ToComponentDataArray<DroneItemTransferResult>(Allocator.Temp);
        ApplyInvalidationDecisions(manager, requests, results, ecb, sites);
        ApplyTaskRequests(manager, ecb);
        RetireEndedTasks(manager, tasks, assignments, ecb);
    }

    private void ApplyDemolitionDecisions(EntityManager manager, EntityCommandBuffer ecb)
    {
        var decisions = manager.GetBuffer<DroneDemolitionDecisionElement>(_candidates);
        foreach (var decision in decisions)
        {
            if (!DroneDemolitionUtility.CanConfirm(manager, decision.Assignment, decision.AssignmentRevision,
                decision.WorkerObservationRevision)) continue;
            var assignment = manager.GetComponentData<DroneTaskAssignment>(decision.Assignment);
            if (DroneTaskRequestDecisionUtility.IsCancellationPlanned(manager, _candidates, assignment.Task)) continue;
            var task = manager.GetComponentData<DroneLogisticsTask>(assignment.Task);
            // 확인과 건물 삭제는 별도 공개 경계다. root는 건물 결과를 받을 때까지 보존한다.
            task.State = DroneLogisticsTaskStateEnum.ExecutionConfirmed;
            ecb.SetComponent(assignment.Task, task);
            ecb.AddComponent<BuildingDemolitionExecutionRequest>(assignment.Task);
            assignment.State = DroneTaskAssignmentStateEnum.Completed;
            assignment.NextAction = DroneActionKindEnum.None;
            manager.SetComponentData(decision.Assignment, assignment);
            manager.SetComponentData(assignment.Worker, new DroneWorkerAssignment());
        }
        decisions.Clear();
    }

    private EntityQuery _taskRequests;

    private void ApplyTaskRequests(EntityManager manager, EntityCommandBuffer ecb)
    {
        var original = manager.GetBuffer<DroneTaskGenerationBlockElement>(_candidates, true);
        using var blocks = new NativeList<DroneTaskGenerationBlockElement>(Allocator.Temp);
        bool changed = false;
        foreach (var block in original)
        {
            if (manager.Exists(block.Key.Target))
            {
                blocks.Add(block);
            }
            else
            {
                changed = true;
            }
        }

        using var requests = DroneTaskRequestDecisionUtility.ReadRequests(manager, _taskRequests);
        foreach (var record in requests)
        {
            var request = record.Request;
            var decision = manager.GetComponentData<DroneTaskRequestDecision>(record.Entity);
            if (request.Operation == DroneTaskRequestOperationEnum.Create && manager.GetComponentData<DroneTaskRequestExecution>(record.Entity).CreationRecorded)
            {
                // 공개된 기존 root만 여기서 병합한다. 같은 ECB의 새 root는 생성할 때 이미 의도를 기록했다.
                // root 전체를 ECB Set으로 예약하면 뒤의 명시 취소 상태를 Playback 때 덮을 수 있으므로 소유 단계에서 반영한다.
                if (request.Kind == DroneLogisticsTaskKindEnum.WorldItemRecovery && decision.Task != Entity.Null)
                {
                    var recoveryTask = manager.GetComponentData<DroneLogisticsTask>(decision.Task);
                    if (!recoveryTask.HasExplicitRecoveryIntent)
                    {
                        recoveryTask.HasExplicitRecoveryIntent = true;
                        manager.SetComponentData(decision.Task, recoveryTask);
                    }
                }
                var key = DroneTaskGenerationKey.From(request.Kind, request.Target, request.ItemType);
                for (int i = blocks.Length - 1; i >= 0; i--)
                {
                    if (blocks[i].Key.Equals(key))
                    {
                        blocks.RemoveAt(i);
                        changed = true;
                    }
                }
            }

            if (request.Operation != DroneTaskRequestOperationEnum.Cancel)
            {
                continue;
            }

            var status = decision.Status;
            if (status == DroneTaskRequestStatusEnum.Cancelled)
            {
                var key = DroneTaskRequestDecisionUtility.Key(manager.GetComponentData<DroneLogisticsTask>(request.Task));
                bool exists = false;
                foreach (var block in blocks)
                {
                    exists |= block.Key.Equals(key);
                }

                if (!exists)
                {
                    blocks.Add(new DroneTaskGenerationBlockElement { Key = key });
                    changed = true;
                }

                // Decision 승인 뒤 신규 배정/이동/인계를 막았으므로 자체 정산으로 줄어든 참조를 Busy로 재판정하지 않는다.
                // 취소 상태 반영과 실물 복구 완료는 다르다. 삭제는 아래 참조 정리가 끝난 뒤 한 곳에서 기록한다.
                var task = manager.GetComponentData<DroneLogisticsTask>(request.Task);
                task.State = DroneLogisticsTaskStateEnum.Cancelled;
                manager.SetComponentData(request.Task, task);
            }

            DroneTaskRequestUtility.RecordResult(ecb, record.Entity, request.Operation, status, request.Task);
        }

        if (changed)
        {
            ecb.SetBuffer<DroneTaskGenerationBlockElement>(_candidates).AddRange(blocks.AsArray());
        }
    }

    private void ApplyInvalidationDecisions(EntityManager manager,
        NativeArray<DroneActionReadyRequest> requests, NativeArray<DroneItemTransferResult> results,
        EntityCommandBuffer ecb, NativeArray<Entity> sites)
    {
        // Decision이 지목한 대상만 처리한다. 여기서 새 무효 작업을 탐색하면 판단 단계와 변경 단계의 책임이 섞인다.
        var decisions = manager.GetBuffer<DroneTaskInvalidationDecisionElement>(_candidates);
        for (int i = 0; i < decisions.Length; i++)
        {
            var decision = decisions[i];
            if (decision.Target == Entity.Null) continue;
            if (!manager.Exists(decision.Target)) continue;
            if (decision.Kind == DroneTaskInvalidationDecisionKindEnum.CloseTask)
            {
                if (!manager.HasComponent<DroneLogisticsTask>(decision.Target)) continue;
                var task = manager.GetComponentData<DroneLogisticsTask>(decision.Target);
                if (task.State != DroneLogisticsTaskStateEnum.Open || DroneSchedulingUtility.IsTaskValid(manager, task, sites)) continue;
                task.State = DroneLogisticsTaskStateEnum.Closed;
                manager.SetComponentData(decision.Target, task);
            }
            else if (decision.Kind == DroneTaskInvalidationDecisionKindEnum.FinishDemolitionTask)
            {
                if (!manager.HasComponent<BuildingDemolitionResult>(decision.Target)) continue;
                var task = manager.GetComponentData<DroneLogisticsTask>(decision.Target);
                if (task.Kind != DroneLogisticsTaskKindEnum.BuildingDemolition ||
                    task.State != DroneLogisticsTaskStateEnum.ExecutionConfirmed) continue;
                task.State = DroneLogisticsTaskStateEnum.Closed;
                manager.SetComponentData(decision.Target, task);
            }
            else if (decision.Kind == DroneTaskInvalidationDecisionKindEnum.CompleteTransportTask)
            {
                if (!manager.HasComponent<DroneLogisticsTask>(decision.Target))
                {
                    continue;
                }
                var task = manager.GetComponentData<DroneLogisticsTask>(decision.Target);
                if (task.State != DroneLogisticsTaskStateEnum.Open)
                {
                    continue;
                }
                if (!DroneSchedulingUtility.IsTransportKind(task.Kind))
                {
                    continue;
                }
                if (!DroneTransportReservationUtility.IsCompleted(
                    manager.GetComponentData<DroneTransportRequirement>(decision.Target),
                    manager.GetComponentData<DroneTransportProgress>(decision.Target)))
                {
                    continue;
                }
                task.State = DroneLogisticsTaskStateEnum.Closed;
                manager.SetComponentData(decision.Target, task);
            }
            else if (decision.Kind == DroneTaskInvalidationDecisionKindEnum.InvalidateAssignment ||
                     decision.Kind == DroneTaskInvalidationDecisionKindEnum.CancelAssignment)
            {
                ApplyAssignmentInvalidation(manager, decision, requests, results, ecb, sites);
            }
        }
        decisions.Clear();
    }

    private static void ApplyAssignmentInvalidation(EntityManager manager, in DroneTaskInvalidationDecisionElement decision,
        NativeArray<DroneActionReadyRequest> requests, NativeArray<DroneItemTransferResult> results,
        EntityCommandBuffer ecb, NativeArray<Entity> sites)
    {
        Entity entity = decision.Target;
        if (!manager.HasComponent<DroneTaskAssignment>(entity)) return;
        var assignment = manager.GetComponentData<DroneTaskAssignment>(entity);
        // 과거 revision의 의도는 적용하지 않는다. 일반 무효화만 최신 자격을 재검사하고 명시 취소는 승인된 의도를 따른다.
        if (assignment.Revision != decision.AssignmentRevision) return;
        bool isExplicitCancellation = decision.Kind == DroneTaskInvalidationDecisionKindEnum.CancelAssignment;
        if (!isExplicitCancellation && !DroneSchedulingUtility.AssignmentNeedsCleanup(manager, entity, assignment, sites)) return;

        bool workerExists = DroneSchedulingUtility.IsActiveEntity(manager, assignment.Worker);
        bool ownsWorker = workerExists && manager.HasComponent<DroneWorkerAssignment>(assignment.Worker) &&
                          manager.GetComponentData<DroneWorkerAssignment>(assignment.Worker).Assignment == entity;
        bool hasCargo = ownsWorker && manager.HasBuffer<StoredItemElement>(assignment.Worker) &&
                        !manager.GetBuffer<StoredItemElement>(assignment.Worker, true).IsEmpty;
        // 적재품이 있으면 수행자 연결을 보존해 다음 Decision이 목적지만 재배정한다. 빈 적재의 확정 종료 상태는 유지한다.
        if (hasCargo) assignment.State = DroneTaskAssignmentStateEnum.Retargeting;
        else if (assignment.State != DroneTaskAssignmentStateEnum.Completed &&
                 assignment.State != DroneTaskAssignmentStateEnum.Cancelled)
            assignment.State = DroneTaskAssignmentStateEnum.Cancelled;
        if (isExplicitCancellation) assignment.NextAction = DroneActionKindEnum.None;
        manager.SetComponentData(entity, assignment);
        if (hasCargo) return;

        if (workerExists && manager.HasComponent<DroneWorkerAssignment>(assignment.Worker) &&
            manager.GetComponentData<DroneWorkerAssignment>(assignment.Worker).Assignment == entity)
            manager.SetComponentData(assignment.Worker, new DroneWorkerAssignment());
        bool hasResult = false;
        for (int r = 0; r < requests.Length; r++) hasResult |= requests[r].Action.Assignment == entity;
        for (int r = 0; r < results.Length; r++) hasResult |= results[r].Action.Assignment == entity;
        bool hasReservation = manager.HasComponent<ConstructionSupplyReservation>(entity) &&
            manager.GetComponentData<ConstructionSupplyReservation>(entity).RemainingQuantity > 0;
        hasReservation |= manager.HasComponent<DroneTransportReservation>(entity) &&
            manager.GetComponentData<DroneTransportReservation>(entity).RemainingQuantity > 0;
        // 결과 소비와 예약 정산이 배정을 식별할 수 있어야 한다. 연결 해제와 배정 엔티티 삭제의 시점은 다를 수 있다.
        if (!hasResult && !hasReservation) ecb.DestroyEntity(entity);
    }

    private static void RetireEndedTasks(EntityManager manager, NativeArray<Entity> tasks, NativeArray<Entity> assignments, EntityCommandBuffer ecb)
    {
        // 일반 종료와 명시 취소 모두 새 작업 이용을 끝낸다. 화물 복구 등 살아 있는 배정의 참조는 보존한다.
        for (int t = 0; t < tasks.Length; t++)
        {
            var task = manager.GetComponentData<DroneLogisticsTask>(tasks[t]);
            if (task.State != DroneLogisticsTaskStateEnum.Closed && task.State != DroneLogisticsTaskStateEnum.Cancelled)
            {
                continue;
            }
            if (DroneSchedulingUtility.IsTransportKind(task.Kind) &&
                manager.GetComponentData<DroneTransportProgress>(tasks[t]).ReservedQuantity > 0)
            {
                continue;
            }
            bool referenced = false;
            for (int a = 0; a < assignments.Length; a++)
                referenced |= manager.GetComponentData<DroneTaskAssignment>(assignments[a]).Task == tasks[t];
            if (!referenced) ecb.DestroyEntity(tasks[t]);
        }
    }


    private EntityQuery _actionRegistry;
    private NativeHashSet<Entity> _pendingResults;

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

    private void InitializeActionTransfer(ref SystemState state)
    {
        _actionRegistry = state.GetEntityQuery(ComponentType.ReadOnly<ItemRegistry>(), ComponentType.ReadOnly<ItemConfigElement>());
        _pendingResults = new NativeHashSet<Entity>(16, Allocator.Persistent);
    }

    private void DisposeActionTransfer()
    {
        if (_pendingResults.IsCreated) _pendingResults.Dispose();
    }

    private void ApplyActionTransfers(EntityManager manager, EntityCommandBuffer ecb)
    {
        ClearPublishedRequests(manager);
        if (_actionRequests.IsEmptyIgnoreFilter) return;
        Entity registry = _actionRegistry.IsEmptyIgnoreFilter ? Entity.Null : _actionRegistry.GetSingletonEntity();
        using var entities = _actionRequests.ToEntityArray(Allocator.Temp);
        using var sites = _sites.ToEntityArray(Allocator.Temp);
        using var ordered = new NativeList<PendingAction>(entities.Length, Allocator.Temp);
        for (int i = 0; i < entities.Length; i++)
        {
            ordered.Add(new PendingAction
            {
                Entity = entities[i], Request = manager.GetComponentData<DroneActionReadyRequest>(entities[i])
            });
        }
        // 수행자별 행동 번호와 별개로 World 접수 순서를 적용해 같은 실물/보관 공간을 요청 배열 순서로 선점하지 않는다.
        ordered.AsArray().Sort(new ReceiptComparer());
        var slotBudgets = new Dictionary<Entity, List<DroneItemTransferSlotDecisionElement>>();
        for (int i = 0; i < ordered.Length; i++)
        {
            var pending = ordered[i];
            // 요청 제거가 EndSimulation까지 지연되므로 ECB 재생 전에 같은 요청을 다시 반영하지 않도록 기억한다.
            if (!_pendingResults.Add(pending.Entity)) continue;
            ecb.RemoveComponent<DroneActionReadyRequest>(pending.Entity);
            ecb.RemoveComponent<DroneItemTransferDecision>(pending.Entity);
            ecb.RemoveComponent<DroneItemTransferItemDecisionElement>(pending.Entity);
            ecb.RemoveComponent<DroneItemTransferSlotDecisionElement>(pending.Entity);
            // 미소비 결과가 있는 엔티티를 요청으로 재사용해 이전 결과를 덮어쓰지 않는다.
            if (manager.HasComponent<DroneItemTransferResult>(pending.Entity)) continue;
            var result = ApplyRequest(manager, _candidates, pending.Entity, pending.Request, registry, ecb, sites, slotBudgets);
            ecb.AddComponent(pending.Entity, result);
        }
    }

    private void ClearPublishedRequests(EntityManager manager)
    {
        using var pending = _pendingResults.ToNativeArray(Allocator.Temp);
        for (int i = 0; i < pending.Length; i++)
        {
            if (!manager.Exists(pending[i]) || !manager.HasComponent<DroneActionReadyRequest>(pending[i]))
            {
                _pendingResults.Remove(pending[i]);
            }
        }
    }

    private static DroneItemTransferResult ApplyRequest(EntityManager manager, Entity candidates, Entity requestEntity,
        in DroneActionReadyRequest request, Entity registry, EntityCommandBuffer ecb, NativeArray<Entity> sites,
        Dictionary<Entity, List<DroneItemTransferSlotDecisionElement>> slotBudgets)
    {
        var result = new DroneItemTransferResult { Action = request.Action, Status = DroneItemTransferStatusEnum.Rejected };
        foreach (var invalidation in manager.GetBuffer<DroneTaskInvalidationDecisionElement>(candidates, true))
        {
            if (invalidation.Kind == DroneTaskInvalidationDecisionKindEnum.CancelAssignment &&
                invalidation.Target == request.Action.Assignment &&
                invalidation.AssignmentRevision == request.Action.AssignmentRevision)
            {
                return result;
            }
        }
        if (!manager.HasComponent<DroneItemTransferDecision>(requestEntity)) return result;
        if (!manager.HasBuffer<DroneItemTransferItemDecisionElement>(requestEntity)) return result;
        if (!manager.HasBuffer<DroneItemTransferSlotDecisionElement>(requestEntity)) return result;
        var plan = manager.GetComponentData<DroneItemTransferDecision>(requestEntity);
        if (!plan.CanExecute || !plan.Prepared) return result;
        RetargetBlockedDrop(manager, request);
        // 건물 종료 계획은 허용 실물/수량의 상한이다. 최신 배정·관측 revision을 대조하되 드론 단계 새 실물을 추가하지 않는다.
        if (!DroneItemTransferValidationUtility.TryValidate(manager, request, out var assignment)) return result;
        var eligible = manager.GetBuffer<DroneItemTransferItemDecisionElement>(requestEntity, true);
        int moved = 0;
        int wanted = plan.WantedQuantity;
        bool collection = request.Action.Kind == DroneActionKindEnum.CollectFromStorage ||
                          request.Action.Kind == DroneActionKindEnum.RecoverWorldItem;
        switch (request.Action.Kind)
        {
            case DroneActionKindEnum.CollectFromStorage:
                moved = CollectStorage(manager, request.Action.Assignment, assignment, eligible, plan.MaximumQuantity, ecb);
                break;
            case DroneActionKindEnum.RecoverWorldItem:
                moved = plan.MaximumQuantity > 0 && ContainsItem(eligible, assignment.Source)
                    ? RecoverWorldItem(manager, assignment, ecb, sites) : 0;
                break;
            case DroneActionKindEnum.SupplyConstructionSite:
                moved = SupplySite(manager, request.Action.Assignment, assignment, eligible, plan.MaximumQuantity, ecb);
                break;
            case DroneActionKindEnum.StoreCargo:
                moved = StoreCargo(manager, requestEntity, request.Action.Assignment, assignment, registry, eligible, plan.MaximumQuantity, slotBudgets, ecb);
                break;
            case DroneActionKindEnum.DropCargo:
                moved = DropCargo(manager, assignment, request.WorldPosition, ecb, eligible, plan.MaximumQuantity);
                break;
        }

        // 요청량이 아니라 공통 Ownership API가 실제 이동시킨 수량으로 배정/예약/결과를 정산한다.
        // 유효 행동이 실물을 못 옮긴 경우도 행동 번호를 소비해 동일 신호가 재시도처럼 중복 적용되지 않게 한다.
        assignment.LastAppliedActionSequence = request.Action.Sequence;
        if (collection)
        {
            ApplyCollectionState(manager, request.Action.Assignment, ref assignment, moved);
        }
        else
        {
            ApplyDeliveryState(manager, request.Action.Assignment, ref assignment, moved, registry);
        }
        manager.SetComponentData(request.Action.Assignment, assignment);
        result.MovedQuantity = moved;
        result.Status = moved == 0 ? DroneItemTransferStatusEnum.Unavailable :
            moved < wanted ? DroneItemTransferStatusEnum.Partial : DroneItemTransferStatusEnum.Completed;
        return result;
    }


    private static void RetargetBlockedDrop(EntityManager manager, in DroneActionReadyRequest request)
    {
        var action = request.Action;
        if (action.Kind != DroneActionKindEnum.DropCargo) return;
        if (!DroneSchedulingUtility.IsActiveEntity(manager, action.Assignment)) return;
        if (!manager.HasComponent<DroneTaskAssignment>(action.Assignment)) return;
        var assignment = manager.GetComponentData<DroneTaskAssignment>(action.Assignment);
        if (assignment.Revision != action.AssignmentRevision || assignment.Worker != action.Worker) return;
        if (assignment.NextAction != DroneActionKindEnum.DropCargo) return;
        if (DroneSchedulingUtility.IsTerminal(assignment.State)) return;
        if (!DroneSchedulingUtility.IsActiveEntity(manager, assignment.Worker)) return;
        if (!manager.HasComponent<DroneWorkerAssignment>(assignment.Worker)) return;
        if (manager.GetComponentData<DroneWorkerAssignment>(assignment.Worker).Assignment != action.Assignment) return;
        if (ConstructionSiteWorldItemUtility.IsWorldPositionAllowed(manager, assignment.DropPosition)) return;
        // 목표 선정 이후 새 현장이 배출 셀을 막을 수 있다. 적재품을 남긴 채 다음 Decision에 외부 목적지 탐색을 맡긴다.
        assignment.State = DroneTaskAssignmentStateEnum.Retargeting;
        manager.SetComponentData(action.Assignment, assignment);
    }

    private static int CollectStorage(EntityManager manager, Entity entity, in DroneTaskAssignment assignment,
        DynamicBuffer<DroneItemTransferItemDecisionElement> eligible, int maximum, EntityCommandBuffer ecb)
    {
        int limit = math.min(maximum, DroneItemTransferUtility.CollectionLimit(manager, entity, assignment));
        var source = manager.GetBuffer<StoredItemElement>(assignment.Source);
        int2 position = (int2)math.floor(manager.GetComponentData<DroneWorkerObservation>(assignment.Worker).Position.xy);
        int moved = 0;
        // 출발 재고는 예약하지 않았으므로 도착 시 생존/Owner/Destroy를 다시 검사한다. 계획에 있던 실물만 수집한다.
        for (int i = 0; i < source.Length && moved < limit;)
        {
            var entry = source[i];
            if (!ContainsItem(eligible, entry.ItemEntity) || entry.ItemType != assignment.ItemType ||
                !ItemOwnershipApplySystem.CanTransferItem(manager, entry.ItemEntity, assignment.ItemType, assignment.Source))
            {
                i++;
                continue;
            }
            if (ItemOwnershipApplySystem.TryTransferItem(manager, entry.ItemEntity, assignment.ItemType,
                assignment.Source, assignment.Worker, 0, position, ecb)) moved++;
            else i++;
        }
        return moved;
    }

    private static int RecoverWorldItem(EntityManager manager, in DroneTaskAssignment assignment,
        EntityCommandBuffer ecb, NativeArray<Entity> sites)
    {
        if (!DroneSchedulingUtility.IsStorage(manager, assignment.Destination)) return 0;
        if (!ItemOwnershipApplySystem.CanTransferItem(manager, assignment.Source, assignment.ItemType, Entity.Null)) return 0;
        var task = manager.GetComponentData<DroneLogisticsTask>(assignment.Task);
        if (!DroneWorldItemRecoveryUtility.IsReasonValid(manager, task, sites))
        {
            return 0;
        }
        int2 position = (int2)math.floor(manager.GetComponentData<DroneWorkerObservation>(assignment.Worker).Position.xy);
        if (!ItemOwnershipApplySystem.TryTransferItem(manager, assignment.Source, assignment.ItemType,
            Entity.Null, assignment.Worker, 0, position, ecb)) return 0;
        if (manager.HasComponent<DroneRecoveryPending>(assignment.Source)) ecb.RemoveComponent<DroneRecoveryPending>(assignment.Source);
        return 1;
    }

    private static int SupplySite(EntityManager manager, Entity entity, in DroneTaskAssignment assignment,
        DynamicBuffer<DroneItemTransferItemDecisionElement> eligible, int maximum, EntityCommandBuffer ecb)
    {
        var cargo = manager.GetBuffer<StoredItemElement>(assignment.Worker);
        if (!DroneSchedulingUtility.IsSite(manager, assignment.Destination)) return 0;
        if (!manager.HasBuffer<StoredItemElement>(assignment.Destination)) return 0;
        var reservation = manager.GetComponentData<ConstructionSupplyReservation>(entity);
        int limit = math.min(maximum, math.min(reservation.RemainingQuantity, ConstructionSupplyReservationUtility.RemainingIncludingOwn(
            manager, assignment.Destination, assignment.ItemType, reservation.RemainingQuantity)));
        int2 position = manager.GetComponentData<GridPosition>(assignment.Destination).Value;
        int moved = MoveCargo(manager, assignment, cargo, position, limit, ecb, false, eligible);
        // 실제 현장 버퍼/Owner가 인계된 뒤에만 도착량을 올려 완공 조건과 실물 보관량이 같은 성공분을 사용하게 한다.
        DroneItemTransferUtility.RecordDelivered(manager, assignment.Destination, assignment.ItemType, moved);
        return moved;
    }

    private static int StoreCargo(EntityManager manager, Entity requestEntity, Entity assignmentEntity, in DroneTaskAssignment assignment, Entity registry,
        DynamicBuffer<DroneItemTransferItemDecisionElement> eligible, int maximum,
        Dictionary<Entity, List<DroneItemTransferSlotDecisionElement>> sharedBudgets, EntityCommandBuffer ecb)
    {
        if (manager.HasComponent<DroneTransportReservation>(assignmentEntity))
        {
            maximum = math.min(maximum, manager.GetComponentData<DroneTransportReservation>(assignmentEntity).RemainingQuantity);
        }
        if (maximum <= 0) return 0;
        var cargo = manager.GetBuffer<StoredItemElement>(assignment.Worker);
        if (!DroneSchedulingUtility.IsStorage(manager, assignment.Destination)) return 0;
        // 수집된 회수품의 보관은 원래 회수 작업의 대상이 더 이상 월드 아이템이 아니어도 계속 유효하다.
        // 행동/적재 출처 검사는 공통 검증을 따르고, 여기서는 현재 목적 보관처의 유효성과 계획 공간을 확인한다.
        if (!sharedBudgets.TryGetValue(assignment.Destination, out var budget))
        {
            // 건물 종료 시 목적지 슬롯 상한을 요청 간 공유한다. 드론 단계에서 새로 열린 공간은 상한에 더하지 않는다.
            var baseline = manager.GetBuffer<DroneItemTransferSlotDecisionElement>(requestEntity, true);
            budget = new List<DroneItemTransferSlotDecisionElement>(baseline.Length);
            for (int slot = 0; slot < baseline.Length; slot++) budget.Add(baseline[slot]);
            sharedBudgets.Add(assignment.Destination, budget);
        }
        int2 position = manager.GetComponentData<GridPosition>(assignment.Destination).Value;
        int moved = 0;
        for (int i = 0; i < cargo.Length && moved < maximum;)
        {
            var entry = cargo[i];
            if (!ContainsItem(eligible, entry.ItemEntity) ||
                !ItemOwnershipApplySystem.CanTransferItem(manager, entry.ItemEntity, assignment.ItemType, assignment.Worker))
            {
                i++;
                continue;
            }
            int budgetIndex = FindPreviousStorageSlot(manager, assignment, registry, budget);
            int slot = budgetIndex >= 0 ? budget[budgetIndex].SlotIndex : -1;
            if (slot < 0) break;
            if (!ItemOwnershipApplySystem.TryTransferItem(manager, entry.ItemEntity, assignment.ItemType,
                assignment.Worker, assignment.Destination, slot, position, ecb))
            {
                i++;
                continue;
            }
            var consumed = budget[budgetIndex];
            // 전송 실패는 공간을 소비하지 않는다. 성공한 실물만 접수 순서대로 공유 예산을 차감한다.
            consumed.ItemType = assignment.ItemType;
            consumed.ItemCount++;
            budget[budgetIndex] = consumed;
            moved++;
        }
        return moved;
    }

    private static int DropCargo(EntityManager manager, in DroneTaskAssignment assignment, int2 position,
        EntityCommandBuffer ecb, DynamicBuffer<DroneItemTransferItemDecisionElement> eligible, int maximum)
    {
        var cargo = manager.GetBuffer<StoredItemElement>(assignment.Worker);
        return MoveCargo(manager, assignment, cargo, position, maximum, ecb, true, eligible);
    }

    private static int MoveCargo(EntityManager manager, in DroneTaskAssignment assignment,
        DynamicBuffer<StoredItemElement> cargo,
        int2 position, int limit, EntityCommandBuffer ecb, bool drop,
        DynamicBuffer<DroneItemTransferItemDecisionElement> eligible)
    {
        int moved = 0;
        // 공통 API가 출발/도착 버퍼와 Owner·위치·벨트·렌더를 함께 변경한다. 드론은 Transfer 요청을 추가 발행하지 않는다.
        for (int i = 0; i < cargo.Length && moved < limit;)
        {
            var entry = cargo[i];
            if (!ContainsItem(eligible, entry.ItemEntity) ||
                !ItemOwnershipApplySystem.CanTransferItem(manager, entry.ItemEntity, assignment.ItemType, assignment.Worker))
            {
                i++;
                continue;
            }
            if (!ItemOwnershipApplySystem.TryTransferItem(manager, entry.ItemEntity, assignment.ItemType,
                assignment.Worker, drop ? Entity.Null : assignment.Destination, drop ? -1 : 0, position, ecb))
            {
                i++;
                continue;
            }
            // 방출 성공품은 같은 엔티티를 월드에 유지하고 다음 작업 생성에서 회수 대상으로 발견할 표시만 남긴다.
            if (drop && !manager.HasComponent<DroneRecoveryPending>(entry.ItemEntity)) ecb.AddComponent<DroneRecoveryPending>(entry.ItemEntity);
            moved++;
        }
        return moved;
    }

    private static bool ContainsItem(DynamicBuffer<DroneItemTransferItemDecisionElement> eligible, Entity item)
    {
        for (int i = 0; i < eligible.Length; i++)
        {
            if (eligible[i].ItemEntity == item) return true;
        }
        return false;
    }

    private static int FindPreviousStorageSlot(EntityManager manager, in DroneTaskAssignment assignment, Entity registry,
        List<DroneItemTransferSlotDecisionElement> budget)
    {
        if (registry == Entity.Null) return -1;
        if (!manager.Exists(registry)) return -1;
        if (!manager.HasComponent<ItemRegistry>(registry)) return -1;
        if (!manager.HasBuffer<ItemConfigElement>(registry)) return -1;
        int maxStack = ItemRegistry.GetMaxStack(manager.GetBuffer<ItemConfigElement>(registry, true), assignment.ItemType);
        for (int i = 0; i < budget.Count; i++)
        {
            var slot = budget[i];
            if (slot.ItemType != ItemTypeEnum.None && slot.ItemType != assignment.ItemType) continue;
            if (slot.ItemCount >= maxStack) continue;
            if (DroneSchedulingUtility.CanStoreInSlot(manager, assignment.Destination, assignment.ItemType, registry, slot.SlotIndex)) return i;
        }
        return -1;
    }

    private static void ApplyCollectionState(EntityManager manager, Entity entity, ref DroneTaskAssignment assignment, int moved)
    {
        if (manager.HasComponent<ConstructionSupplyReservation>(entity))
        {
            var reservation = manager.GetComponentData<ConstructionSupplyReservation>(entity);
            // 공급원에서 못 수집한 부족분은 현장 예약에서 해제하고 실제 적재량만 도착 예정으로 남긴다.
            DroneItemTransferUtility.ReduceReservation(manager, entity, ref reservation, moved);
        }
        if (manager.HasComponent<DroneTransportReservation>(entity))
        {
            var reservation = manager.GetComponentData<DroneTransportReservation>(entity);
            var progress = manager.GetComponentData<DroneTransportProgress>(reservation.Task);
            DroneTransportReservationUtility.TryReduce(ref progress, ref reservation, moved);
            manager.SetComponentData(reservation.Task, progress);
            manager.SetComponentData(entity, reservation);
        }
        assignment.AssignedQuantity = moved;
        if (moved > 0)
        {
            bool recovery = assignment.NextAction == DroneActionKindEnum.RecoverWorldItem;
            manager.SetComponentData(assignment.Worker, new DroneCargoState
            {
                Origin = recovery ? DroneCargoOriginEnum.Recovery : DroneCargoOriginEnum.Supply
            });
            assignment.State = DroneTaskAssignmentStateEnum.MovingToDestination;
            assignment.NextAction = recovery || manager.HasComponent<DroneTransportReservation>(entity)
                ? DroneActionKindEnum.StoreCargo : DroneActionKindEnum.SupplyConstructionSite;
        }
        else
        {
            assignment.State = DroneTaskAssignmentStateEnum.Cancelled;
            assignment.NextAction = DroneActionKindEnum.None;
            manager.SetComponentData(assignment.Worker, new DroneWorkerAssignment());
        }
    }

    private static void ApplyDeliveryState(EntityManager manager, Entity entity, ref DroneTaskAssignment assignment,
        int moved, Entity registry)
    {
        bool hasCargo = !manager.GetBuffer<StoredItemElement>(assignment.Worker, true).IsEmpty;
        if (assignment.NextAction == DroneActionKindEnum.StoreCargo && manager.HasComponent<DroneTransportReservation>(entity))
        {
            var reservation = manager.GetComponentData<DroneTransportReservation>(entity);
            var progress = manager.GetComponentData<DroneTransportProgress>(reservation.Task);
            // 계획/최종 인계 상한이 해당 확보 잔량이므로 실제 성공량은 이를 넘지 않는다.
            DroneTransportReservationUtility.TryRecordDelivery(ref progress, ref reservation, moved);
            bool waitingForSpace = hasCargo && reservation.RemainingQuantity > 0 &&
                manager.GetComponentData<DroneBatteryState>(assignment.Worker).CurrentCharge > 0f &&
                DroneSchedulingUtility.CanReceiveStorageItem(manager, assignment.Destination, assignment.ItemType, registry);
            if (waitingForSpace)
            {
                manager.SetComponentData(reservation.Task, progress);
                manager.SetComponentData(entity, reservation);
                assignment.State = DroneTaskAssignmentStateEnum.AwaitingDelivery;
                return;
            }
            // 화물이 없거나 실제 수령 불가/0 배터리 중단이면 남은 확보만 반환한다. 이미 완료한 양은 유지한다.
            DroneTransportReservationUtility.Release(ref progress, ref reservation);
            manager.SetComponentData(reservation.Task, progress);
            manager.SetComponentData(entity, reservation);
        }
        if (manager.HasComponent<ConstructionSupplyReservation>(entity))
        {
            var reservation = manager.GetComponentData<ConstructionSupplyReservation>(entity);
            // 현장/회수의 기존 부분 인계는 목적지 예약을 정산하고 잔여 화물을 재배정한다.
            DroneItemTransferUtility.ReduceReservation(manager, entity, ref reservation, 0);
        }
        assignment.State = hasCargo ? DroneTaskAssignmentStateEnum.Retargeting : DroneTaskAssignmentStateEnum.Completed;
        assignment.NextAction = DroneActionKindEnum.None;
        if (!hasCargo)
        {
            manager.SetComponentData(assignment.Worker, new DroneCargoState());
            manager.SetComponentData(assignment.Worker, new DroneWorkerAssignment());
        }
    }
}
