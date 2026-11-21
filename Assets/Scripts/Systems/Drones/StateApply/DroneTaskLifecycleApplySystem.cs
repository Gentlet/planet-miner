using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: StateApply에서 행동 신호와 작업/배정 무효화 의도를 반영하고 실제 인계량을 정산한다.
/// 입력: 요청 엔티티의 DroneActionReadyRequest와 DroneItemTransferDecisionSystem/DroneItemTransferExecutionSystem이 준비한 건물 종료 인계 계획,
/// DroneTaskDecisionSystem이 후보 관리 엔티티에 작성한 DroneTaskInvalidationDecisionElement 및 최신 배정·실물·현장 상태를 읽는다.
/// 이용·소유권: 배정 엔티티의 상태/행동 번호/개별 예약, 수행자의 적재 출처/배정 연결,
/// 현장의 도착량/예약 합계는 이 경계에서 갱신한다. 월드 회수·방출은 실제 Entity의 삭제·생성을 기록한다.
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
    private EntityQuery _demolitionResults;
    private EntityQuery _refundConfig;
    private EntityQuery _capacity;

    public void OnCreate(ref SystemState state)
    {
        _candidates = DroneSchedulingUtility.GetOrCreateCandidates(state.EntityManager);
        _taskRequests = state.GetEntityQuery(typeof(DroneTaskRequest), typeof(DroneTaskRequestDecision));
        _tasks = state.GetEntityQuery(ComponentType.ReadWrite<DroneLogisticsTask>());
        _sites = state.GetEntityQuery(ComponentType.ReadOnly<ConstructionSite>(), ComponentType.ReadOnly<GridPosition>());
        _assignments = state.GetEntityQuery(ComponentType.ReadWrite<DroneTaskAssignment>());
        _actionRequests = state.GetEntityQuery(ComponentType.ReadOnly<DroneActionReadyRequest>());
        _actionResults = state.GetEntityQuery(ComponentType.ReadOnly<DroneItemTransferResult>());
        _demolitionResults = state.GetEntityQuery(ComponentType.ReadOnly<BuildingDemolitionResult>(), ComponentType.ReadOnly<DroneLogisticsTask>());
        _refundConfig = state.GetEntityQuery(ComponentType.ReadOnly<BuildingConstructionMaterialElement>());
        _capacity = state.GetEntityQuery(ComponentType.ReadOnly<DroneCapacityState>());
        _actionRegistry = state.GetEntityQuery(ComponentType.ReadOnly<ItemRegistry>(), ComponentType.ReadOnly<ItemConfigElement>());
        _pendingResults = new NativeHashSet<Entity>(16, Allocator.Persistent);
    }

    public void OnDestroy(ref SystemState state)
    {
        if (_pendingResults.IsCreated)
        {
            _pendingResults.Dispose();
        }
    }

    public void OnUpdate(ref SystemState state)
    {
        // 적용 시작에 현재 슬롯과 화물 수량, 살아 있는 월드 실물을 대조한다.
        state.CompleteDependency();
        var manager = state.EntityManager;
        var ecb = state.World.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>().CreateCommandBuffer();
        using var droppedBlocks = new NativeList<DroneTaskGenerationBlockElement>(Allocator.Temp);
        using var assignments = _assignments.ToEntityArray(Allocator.Temp);
        Entity registry = _actionRegistry.IsEmptyIgnoreFilter ? Entity.Null : _actionRegistry.GetSingletonEntity();
        ApplyDemolitionResults(manager, registry, assignments, ecb);
        ApplyActionTransfers(manager, ecb, droppedBlocks);
        ApplyDemolitionDecisions(manager, registry, assignments, ecb);
        // 행동 반영 뒤의 배정 상태로 무효화 의도를 재검사한다. 요청/결과 참조는 ECB 재생 전까지 남겨 삭제를 보류한다.
        using var tasks = _tasks.ToEntityArray(Allocator.Temp);
        using var sites = _sites.ToEntityArray(Allocator.Temp);
        using var requests = _actionRequests.ToComponentDataArray<DroneActionReadyRequest>(Allocator.Temp);
        using var results = _actionResults.ToComponentDataArray<DroneItemTransferResult>(Allocator.Temp);
        ApplyInvalidationDecisions(manager, requests, results, ecb, sites, registry);
        ApplyTaskRequests(manager, ecb);
        foreach (var block in droppedBlocks) ecb.AppendToBuffer(_candidates, block);
        RetireEndedTasks(manager, tasks, assignments, ecb);
    }

    private void ApplyDemolitionDecisions(EntityManager manager, Entity registry, NativeArray<Entity> assignments,
        EntityCommandBuffer ecb)
    {
        var decisions = manager.GetBuffer<DroneDemolitionDecisionElement>(_candidates);
        using var checkedRoots = new NativeHashSet<Entity>(4, Allocator.Temp);
        var invalidations = manager.GetBuffer<DroneTaskInvalidationDecisionElement>(_candidates, true);
        Entity config = _refundConfig.IsEmptyIgnoreFilter ? Entity.Null : _refundConfig.GetSingletonEntity();
        int capacity = DroneSchedulingUtility.ReadCarryingCapacity(_capacity);
        foreach (var decision in decisions)
        {
            if (!DroneDemolitionUtility.CanConfirm(manager, decision.Assignment, decision.AssignmentRevision,
                decision.WorkerObservationRevision)) continue;
            Entity root = manager.GetComponentData<DroneTaskAssignment>(decision.Assignment).Task;
            if (!checkedRoots.Add(root) ||
                DroneTaskRequestDecisionUtility.IsCancellationPlanned(manager, _candidates, root)) continue;

            bool invalid = false;
            foreach (var intent in invalidations)
            {
                if (intent.Kind != DroneTaskInvalidationDecisionKindEnum.CancelAssignment &&
                    intent.Kind != DroneTaskInvalidationDecisionKindEnum.InvalidateAssignment) continue;
                if (!manager.HasComponent<DroneTaskAssignment>(intent.Target) ||
                    manager.GetComponentData<DroneTaskAssignment>(intent.Target).Task != root) continue;
                invalid = true;
                break;
            }
            var task = manager.GetComponentData<DroneLogisticsTask>(root);
            if (invalid || !DroneDemolitionUtility.CanConfirmDemolitionCrew(manager, root, task, assignments, config, registry, capacity))
                continue;
            if (DroneDemolitionUtility.IsCommittedPickup(manager, root, task))
            {
                HandOffRefund(manager, root, task, registry, assignments, ecb);
                continue;
            }

            // 확인 뒤에도 배정과 수납 위치를 유지해 다음 EndBuilding에서 전량을 다시 대조한다.
            task.State = DroneLogisticsTaskStateEnum.ExecutionConfirmed;
            ecb.SetComponent(root, task);
            ecb.AddComponent<BuildingDemolitionExecutionRequest>(root);
            if (manager.HasComponent<PreparingStationDemolition>(task.Target))
                ecb.AddComponent(task.Target, new PendingBuildingDemolition { Task = root });
        }
        decisions.Clear();
    }

    private void ApplyDemolitionResults(EntityManager manager, Entity registry, NativeArray<Entity> assignments,
        EntityCommandBuffer ecb)
    {
        using var roots = _demolitionResults.ToEntityArray(Allocator.Temp);
        foreach (Entity root in roots)
        {
            var task = manager.GetComponentData<DroneLogisticsTask>(root);
            if (task.State != DroneLogisticsTaskStateEnum.ExecutionConfirmed || task.Target == root) continue;
            var result = manager.GetComponentData<BuildingDemolitionResult>(root).Status;
            if (result == BuildingDemolitionResultStatus.Completed)
            {
                HandOffRefund(manager, root, task, registry, assignments, ecb);
                continue;
            }

            foreach (Entity entity in assignments)
            {
                var assignment = manager.GetComponentData<DroneTaskAssignment>(entity);
                if (assignment.Task == root && assignment.NextAction == DroneActionKindEnum.DemolishBuilding)
                    ReleaseDemolitionWorker(manager, entity, assignment);
            }
            bool retry = result == BuildingDemolitionResultStatus.Retry;
            task.State = retry ? DroneLogisticsTaskStateEnum.Open : DroneLogisticsTaskStateEnum.Closed;
            ecb.SetComponent(root, task);
            if (retry)
            {
                ecb.RemoveComponent<BuildingDemolitionResult>(root);
                if (manager.HasComponent<PreparingStationDemolition>(task.Target) &&
                    manager.HasComponent<PendingBuildingDemolition>(task.Target))
                    ecb.RemoveComponent<PendingBuildingDemolition>(task.Target);
            }
        }
    }

    private static void HandOffRefund(EntityManager manager, Entity root, DroneLogisticsTask task, Entity registry,
        NativeArray<Entity> assignments, EntityCommandBuffer ecb)
    {
        var refund = manager.GetBuffer<DroneDemolitionRefundElement>(root);
        foreach (Entity entity in assignments)
        {
            var assignment = manager.GetComponentData<DroneTaskAssignment>(entity);
            if (assignment.Task != root || assignment.NextAction != DroneActionKindEnum.DemolishBuilding) continue;
            if (assignment.ItemType == ItemTypeEnum.None)
            {
                ReleaseDemolitionWorker(manager, entity, assignment);
                continue;
            }

            int material = -1;
            for (int i = 0; i < refund.Length; i++)
            {
                if (refund[i].ItemType != assignment.ItemType) continue;
                material = i;
                break;
            }
            if (material < 0 || refund[material].Quantity < assignment.AssignedQuantity ||
                !DroneDemolitionUtility.CanReceiveAtSite(manager, entity, assignment, root) ||
                !DroneDemolitionUtility.HasDestinationClaim(manager, entity, assignment, registry))
            {
                ReleaseDemolitionWorker(manager, entity, assignment);
                continue;
            }

            manager.SetComponentData(assignment.Worker, new DroneCargoState
            {
                ItemType = assignment.ItemType, Quantity = assignment.AssignedQuantity, Origin = DroneCargoOriginEnum.DemolitionRecovery
            });
            assignment.State = DroneTaskAssignmentStateEnum.MovingToDestination;
            assignment.NextAction = DroneActionKindEnum.StoreCargo;
            assignment.Revision = assignment.Revision == uint.MaxValue ? 1 : assignment.Revision + 1;
            manager.SetComponentData(entity, assignment);
            var remaining = refund[material];
            remaining.Quantity -= assignment.AssignedQuantity;
            if (remaining.Quantity == 0) refund.RemoveAt(material);
            else refund[material] = remaining;
        }

        // 삭제된 건물의 집결 위치와 미인계 환급 수량은 root에 남고, 납품할 창고는 수행자의 배정에 남는다.
        if (refund.IsEmpty) task.State = DroneLogisticsTaskStateEnum.Closed;
        else task.Target = root;
        ecb.SetComponent(root, task);
    }

    private static void ReleaseDemolitionWorker(EntityManager manager, Entity entity, DroneTaskAssignment assignment)
    {
        DroneDemolitionStorageUtility.ResizeClaim(manager, assignment.Destination, assignment.Worker, 0);
        if (manager.HasComponent<DroneWorkerAssignment>(assignment.Worker) &&
            manager.GetComponentData<DroneWorkerAssignment>(assignment.Worker).Assignment == entity)
            manager.SetComponentData(assignment.Worker, new DroneWorkerAssignment());
        assignment.State = DroneTaskAssignmentStateEnum.Completed;
        assignment.NextAction = DroneActionKindEnum.None;
        assignment.Revision = assignment.Revision == uint.MaxValue ? 1 : assignment.Revision + 1;
        manager.SetComponentData(entity, assignment);
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
                var key = DroneTaskRequestDecisionUtility.Key(request);
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
                var task = manager.GetComponentData<DroneLogisticsTask>(request.Task);
                var key = DroneTaskRequestDecisionUtility.Key(task);
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
                if (task.Kind == DroneLogisticsTaskKindEnum.WorldItemRecovery)
                    BlockAutomaticRecoveryForCargo(manager, task.CreationSequence);
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

    private void BlockAutomaticRecoveryForCargo(EntityManager manager, ulong taskSequence)
    {
        using var assignments = _assignments.ToComponentDataArray<DroneTaskAssignment>(Allocator.Temp);
        foreach (var assignment in assignments)
        {
            if (assignment.OriginalTaskCreationSequence != taskSequence ||
                !manager.HasComponent<DroneCargoState>(assignment.Worker) ||
                !manager.HasComponent<DroneWorkerAssignment>(assignment.Worker)) continue;
            Entity linked = manager.GetComponentData<DroneWorkerAssignment>(assignment.Worker).Assignment;
            if (!manager.HasComponent<DroneTaskAssignment>(linked) ||
                manager.GetComponentData<DroneTaskAssignment>(linked).OriginalTaskCreationSequence != taskSequence) continue;
            var cargo = manager.GetComponentData<DroneCargoState>(assignment.Worker);
            if (cargo.Origin != DroneCargoOriginEnum.Recovery || cargo.Quantity <= 0) continue;
            cargo.BlockAutomaticRecoveryOnDrop = true;
            manager.SetComponentData(assignment.Worker, cargo);
        }
    }

    private void ApplyInvalidationDecisions(EntityManager manager,
        NativeArray<DroneActionReadyRequest> requests, NativeArray<DroneItemTransferResult> results,
        EntityCommandBuffer ecb, NativeArray<Entity> sites, Entity registry)
    {
        // Decision이 지목한 대상만 처리한다. 여기서 새 무효 작업을 탐색하면 판단 단계와 변경 단계의 책임이 섞인다.
        var decisions = manager.GetBuffer<DroneTaskInvalidationDecisionElement>(_candidates);
        for (int i = 0; i < decisions.Length; i++)
        {
            var decision = decisions[i];
            if (decision.Target == Entity.Null) continue;
            if (decision.Kind == DroneTaskInvalidationDecisionKindEnum.CloseTask)
            {
                if (!manager.HasComponent<DroneLogisticsTask>(decision.Target)) continue;
                var task = manager.GetComponentData<DroneLogisticsTask>(decision.Target);
                if (task.State != DroneLogisticsTaskStateEnum.Open || DroneSchedulingUtility.IsTaskValid(manager, task, sites)) continue;
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
                ApplyAssignmentInvalidation(manager, decision, requests, results, ecb, sites, registry);
            }
        }
        decisions.Clear();
    }

    private static void ApplyAssignmentInvalidation(EntityManager manager, in DroneTaskInvalidationDecisionElement decision,
        NativeArray<DroneActionReadyRequest> requests, NativeArray<DroneItemTransferResult> results,
        EntityCommandBuffer ecb, NativeArray<Entity> sites, Entity registry)
    {
        Entity entity = decision.Target;
        if (!manager.HasComponent<DroneTaskAssignment>(entity)) return;
        var assignment = manager.GetComponentData<DroneTaskAssignment>(entity);
        // 과거 revision의 의도는 적용하지 않는다. 일반 무효화만 최신 자격을 재검사하고 명시 취소는 승인된 의도를 따른다.
        if (assignment.Revision != decision.AssignmentRevision) return;
        bool isExplicitCancellation = decision.Kind == DroneTaskInvalidationDecisionKindEnum.CancelAssignment;
        if (!isExplicitCancellation && !DroneSchedulingUtility.AssignmentNeedsCleanup(manager, entity, assignment, sites, registry)) return;

        bool workerExists = DroneSchedulingUtility.IsActiveEntity(manager, assignment.Worker);
        bool ownsWorker = workerExists && manager.HasComponent<DroneWorkerAssignment>(assignment.Worker) &&
                          manager.GetComponentData<DroneWorkerAssignment>(assignment.Worker).Assignment == entity;
        bool hasCargo = ownsWorker && manager.HasComponent<DroneCargoState>(assignment.Worker) &&
                        manager.GetComponentData<DroneCargoState>(assignment.Worker).Quantity > 0;
        // Retargeting 진입 때 기존 슬롯은 반환했다. 같은 창고에 새로 확보한 재배정 슬롯은 Publish가 정산한다.
        if (assignment.State != DroneTaskAssignmentStateEnum.Retargeting)
            DroneDemolitionStorageUtility.ResizeClaim(manager, assignment.Destination, assignment.Worker, 0);
        // 적재품이 있으면 수행자 연결을 보존해 다음 Decision이 목적지만 재배정한다. 빈 적재의 확정 종료 상태는 유지한다.
        if (hasCargo) assignment.State = DroneTaskAssignmentStateEnum.Retargeting;
        else if (assignment.State != DroneTaskAssignmentStateEnum.Completed &&
                 assignment.State != DroneTaskAssignmentStateEnum.Cancelled)
            assignment.State = DroneTaskAssignmentStateEnum.Cancelled;
        if (isExplicitCancellation) assignment.NextAction = DroneActionKindEnum.None;
        manager.SetComponentData(entity, assignment);
        if (hasCargo) return;

        if (ownsWorker)
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
        NativeHashSet<Entity> referencedTasks = default;
        try
        {
            for (int t = 0; t < tasks.Length; t++)
            {
                Entity taskEntity = tasks[t];
                var task = manager.GetComponentData<DroneLogisticsTask>(taskEntity);
                if (task.State != DroneLogisticsTaskStateEnum.Closed && task.State != DroneLogisticsTaskStateEnum.Cancelled)
                {
                    continue;
                }
                if (DroneSchedulingUtility.IsTransportKind(task.Kind) &&
                    manager.GetComponentData<DroneTransportProgress>(taskEntity).ReservedQuantity > 0)
                {
                    continue;
                }
                // 무효화와 요청 반영이 끝난 배정의 현재 참조를 첫 종료 후보에서 한 번 모은다.
                if (!referencedTasks.IsCreated)
                {
                    referencedTasks = new NativeHashSet<Entity>(assignments.Length, Allocator.Temp);
                    for (int a = 0; a < assignments.Length; a++)
                    {
                        referencedTasks.Add(manager.GetComponentData<DroneTaskAssignment>(assignments[a]).Task);
                    }
                }
                bool referenced = referencedTasks.Contains(taskEntity);
                if (!referenced && manager.HasBuffer<DroneStationRelocationMemberElement>(taskEntity))
                {
                    foreach (var member in manager.GetBuffer<DroneStationRelocationMemberElement>(taskEntity, true))
                    {
                        referenced |= !member.Finished;
                    }
                }
                if (!referenced)
                {
                    ecb.DestroyEntity(taskEntity);
                }
            }
        }
        finally
        {
            if (referencedTasks.IsCreated)
            {
                referencedTasks.Dispose();
            }
        }
    }


    private EntityQuery _actionRegistry;
    private NativeHashSet<Entity> _pendingResults;

    private void ApplyActionTransfers(EntityManager manager, EntityCommandBuffer ecb,
        NativeList<DroneTaskGenerationBlockElement> droppedBlocks)
    {
        ClearPublishedRequests(manager);
        if (_actionRequests.IsEmptyIgnoreFilter) return;
        Entity registry = _actionRegistry.IsEmptyIgnoreFilter ? Entity.Null : _actionRegistry.GetSingletonEntity();
        using var sites = _sites.ToEntityArray(Allocator.Temp);
        // 준비와 같은 비교자를 사용하되 실제 반영 시점의 요청을 다시 캡처한다.
        using var ordered = DroneActionRequestUtility.ReadInReceiptOrder(manager, _actionRequests, Allocator.Temp);
        var slotBudgets = new Dictionary<Entity, List<DroneItemTransferSlotDecisionElement>>();
        var inputBudgets = new Dictionary<(Entity, ItemTypeEnum), int>();
        // 모든 행동 전의 입력 한도를 고정한다. 같은 드론 단계의 픽업으로 생긴 여유는 다시 배분하지 않는다.
        foreach (var pending in ordered)
        {
            if (pending.Request.Action.Kind != DroneActionKindEnum.StoreCargo)
            {
                continue;
            }
            if (!manager.HasComponent<DroneTaskAssignment>(pending.Request.Action.Assignment))
            {
                continue;
            }
            if (!manager.HasComponent<DroneItemTransferDecision>(pending.Entity))
            {
                continue;
            }
            var assignment = manager.GetComponentData<DroneTaskAssignment>(pending.Request.Action.Assignment);
            if (assignment.DestinationArea != DroneTransportAreaEnum.Input)
            {
                continue;
            }
            var plan = manager.GetComponentData<DroneItemTransferDecision>(pending.Entity);
            var key = (assignment.Destination, assignment.ItemType);
            if (inputBudgets.TryGetValue(key, out int previous))
            {
                inputBudgets[key] = math.min(previous, plan.InputQuantityLimit);
            }
            else
            {
                inputBudgets.Add(key, plan.InputQuantityLimit);
            }
        }
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
            var result = ApplyRequest(manager, _candidates, pending.Entity, pending.Request, registry, ecb, sites, slotBudgets, inputBudgets, droppedBlocks);
            ecb.AddComponent(pending.Entity, result);
        }
    }

    private void ClearPublishedRequests(EntityManager manager)
    {
        using var pending = _pendingResults.ToNativeArray(Allocator.Temp);
        for (int i = 0; i < pending.Length; i++)
        {
            if (!manager.HasComponent<DroneActionReadyRequest>(pending[i]))
            {
                _pendingResults.Remove(pending[i]);
            }
        }
    }

    private static DroneItemTransferResult ApplyRequest(EntityManager manager, Entity candidates, Entity requestEntity,
        in DroneActionReadyRequest request, Entity registry, EntityCommandBuffer ecb, NativeArray<Entity> sites,
        Dictionary<Entity, List<DroneItemTransferSlotDecisionElement>> slotBudgets,
        Dictionary<(Entity, ItemTypeEnum), int> inputBudgets, NativeList<DroneTaskGenerationBlockElement> droppedBlocks)
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
                moved = CollectStorage(manager, request.Action.Assignment, assignment, eligible, plan.MaximumQuantity,
                    manager.GetBuffer<DroneInventoryWithdrawalElement>(candidates));
                break;
            case DroneActionKindEnum.RecoverWorldItem:
                moved = plan.MaximumQuantity > 0 && eligible.Length == 1 && eligible[0].ItemEntity == assignment.Source
                    ? RecoverWorldItem(manager, assignment, ecb, sites) : 0;
                break;
            case DroneActionKindEnum.SupplyConstructionSite:
                moved = SupplySite(manager, request.Action.Assignment, assignment, plan.MaximumQuantity);
                break;
            case DroneActionKindEnum.StoreCargo:
                moved = StoreCargo(manager, requestEntity, request.Action.Assignment, assignment, registry, plan.MaximumQuantity, slotBudgets, inputBudgets);
                break;
            case DroneActionKindEnum.DropCargo:
                moved = DropCargo(manager, assignment, request.WorldPosition, plan.MaximumQuantity, ecb, droppedBlocks);
                break;
        }

        // 실제로 반영한 수량만 배정, 예약, 행동 결과에 정산한다.
        // 유효 행동이 실물을 못 옮긴 경우도 행동 번호를 소비해 동일 신호가 재시도처럼 중복 적용되지 않게 한다.
        assignment.LastAppliedActionSequence = request.Action.Sequence;
        if (collection)
        {
            ApplyCollectionState(manager, request.Action.Assignment, ref assignment, moved);
        }
        else
        {
            var cargo = manager.GetComponentData<DroneCargoState>(assignment.Worker);
            cargo.Quantity -= moved;
            manager.SetComponentData(assignment.Worker, cargo.Quantity == 0 ? default : cargo);
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
        DynamicBuffer<DroneItemTransferItemDecisionElement> eligible, int maximum,
        DynamicBuffer<DroneInventoryWithdrawalElement> withdrawals)
    {
        int limit = math.min(maximum, DroneItemTransferUtility.CollectionLimit(manager, entity, assignment));
        if (limit <= 0) return 0;
        Entity source = assignment.Source;
        if (assignment.ItemType == ItemTypeEnum.Drone && manager.HasComponent<PreparingStationDemolition>(source)) return 0;
        bool product = assignment.SourceArea == DroneTransportAreaEnum.Product;
        var output = product ? manager.GetBuffer<ProductSlotElement>(source) : default;
        var storage = product ? default : manager.GetBuffer<StorageSlotElement>(source);
        var recovery = manager.HasBuffer<ResearchRecoveryElement>(source)
            ? manager.GetBuffer<ResearchRecoveryElement>(source) : default;
        bool recoveryOnly = manager.GetComponentData<DroneLogisticsTask>(assignment.Task).Purpose ==
                            DroneTransportPurposeEnum.ResearchRecovery;

        int moved = 0;
        foreach (var candidate in eligible)
        {
            if (moved >= limit) break;
            int slot = candidate.SlotIndex;
            var type = assignment.ItemType;
            int budgetIndex = DroneItemTransferUtility.FindWithdrawal(withdrawals, source, product, slot, type);
            if (budgetIndex < 0) continue;
            var budget = withdrawals[budgetIndex];
            int quantity = math.min(limit - moved, math.min(candidate.Quantity, budget.Remaining));
            int recoverable = recovery.IsCreated ? ResearchRecoveryUtility.ReadSlotQuantity(recovery, slot, type) : 0;
            if (recoveryOnly) quantity = math.min(quantity, recoverable);
            int taken = product
                ? InventorySlotUtility.TakeProduct(output, slot, type, quantity)
                : InventorySlotUtility.Take(storage, slot, type, quantity);
            if (taken == 0) continue;
            if (recoverable > 0)
            {
                int removedRecovery = recoveryOnly ? taken : math.max(0, recoverable - storage[slot].Quantity);
                if (removedRecovery > 0) ResearchRecoveryUtility.ConsumeSlot(recovery, slot, type, removedRecovery);
            }
            budget.Remaining -= taken;
            withdrawals[budgetIndex] = budget;
            moved += taken;
        }
        return moved;
    }

    private static int RecoverWorldItem(EntityManager manager, in DroneTaskAssignment assignment,
        EntityCommandBuffer ecb, NativeArray<Entity> sites)
    {
        if (!DroneWorldItemRecoveryUtility.CanRecoverAssignedItem(manager, assignment, sites) ||
            !manager.HasComponent<DestroyItemRequest>(assignment.Source)) return 0;

        // 다른 행동은 EndSimulation 이전에도 삭제 예정 실물을 수집할 수 없다.
        manager.SetComponentEnabled<DestroyItemRequest>(assignment.Source, true);
        if (manager.HasComponent<BeltMovementState>(assignment.Source))
            manager.SetComponentEnabled<BeltMovementState>(assignment.Source, false);
        ecb.DestroyEntity(assignment.Source);
        return 1;
    }

    private static int SupplySite(EntityManager manager, Entity entity, in DroneTaskAssignment assignment, int maximum)
    {
        if (!DroneSchedulingUtility.IsSite(manager, assignment.Destination)) return 0;
        var reservation = manager.GetComponentData<ConstructionSupplyReservation>(entity);
        int limit = math.min(reservation.RemainingQuantity, ConstructionSupplyReservationUtility.RemainingIncludingOwn(
            manager, assignment.Destination, assignment.ItemType, reservation.RemainingQuantity));
        int moved = math.min(maximum, math.min(limit, manager.GetComponentData<DroneCargoState>(assignment.Worker).Quantity));
        DroneItemTransferUtility.RecordDelivered(manager, assignment.Destination, assignment.ItemType, moved);
        return moved;
    }

    private static int StoreCargo(EntityManager manager, Entity requestEntity, Entity assignmentEntity,
        in DroneTaskAssignment assignment, Entity registry, int maximum,
        Dictionary<Entity, List<DroneItemTransferSlotDecisionElement>> sharedBudgets,
        Dictionary<(Entity, ItemTypeEnum), int> inputBudgets)
    {
        bool hasTransportReservation = manager.HasComponent<DroneTransportReservation>(assignmentEntity);
        if (hasTransportReservation)
            maximum = math.min(maximum, manager.GetComponentData<DroneTransportReservation>(assignmentEntity).RemainingQuantity);
        maximum = math.min(maximum, manager.GetComponentData<DroneCargoState>(assignment.Worker).Quantity);
        if (maximum <= 0 || !DroneTransportEndpointUtility.IsArea(manager, assignment.Destination, assignment.DestinationArea)) return 0;
        if (assignment.ItemType == ItemTypeEnum.Drone && manager.HasComponent<PreparingStationDemolition>(assignment.Destination) &&
            !DroneStationRelocationUtility.CanReceiveCommittedDroneCargo(manager, assignment.Destination, assignmentEntity, assignment.Worker)) return 0;

        var inputKey = (assignment.Destination, assignment.ItemType);
        if (assignment.DestinationArea == DroneTransportAreaEnum.Input)
        {
            if (!inputBudgets.TryGetValue(inputKey, out int remaining)) return 0;
            maximum = math.min(maximum, remaining);
        }

        if (!sharedBudgets.TryGetValue(assignment.Destination, out var budget))
        {
            var baseline = manager.GetBuffer<DroneItemTransferSlotDecisionElement>(requestEntity, true);
            budget = new List<DroneItemTransferSlotDecisionElement>(baseline.Length);
            foreach (var slot in baseline) budget.Add(slot);
            sharedBudgets.Add(assignment.Destination, budget);
        }

        var storage = manager.GetBuffer<StorageSlotElement>(assignment.Destination);
        bool generalOnly = assignment.DestinationArea == DroneTransportAreaEnum.GeneralStorage && hasTransportReservation;
        var limits = new StoreCargoStorageLimits();
        int moved = 0;
        while (moved < maximum)
        {
            int budgetIndex = FindPreviousStorageSlot(manager, assignment, registry, budget, generalOnly, ref limits);
            if (budgetIndex < 0) break;
            var slot = budget[budgetIndex];
            int count = math.min(maximum - moved, limits.MaxStack - slot.ItemCount);
            var storageRead = StationStorageUtility.StorageReadContext.Create(manager, assignment.Destination);
            int free = storageRead.FreeAfterReservations(slot.SlotIndex, assignment.ItemType,
                InventorySlotUtility.FreeQuantity(storage[slot.SlotIndex], assignment.ItemType, limits.MaxStack), assignment.Worker);
            count = math.min(count, free);
            if (!InventorySlotUtility.TryAdd(storage, slot.SlotIndex, assignment.ItemType, count, limits.MaxStack)) break;
            DroneDemolitionStorageUtility.Consume(manager, assignment.Destination, assignment.Worker, slot.SlotIndex, count);
            slot.ItemType = assignment.ItemType;
            slot.ItemCount += count;
            budget[budgetIndex] = slot;
            moved += count;
        }
        if (assignment.DestinationArea == DroneTransportAreaEnum.Input) inputBudgets[inputKey] -= moved;
        return moved;
    }

    private static int DropCargo(EntityManager manager, in DroneTaskAssignment assignment, int2 position, int maximum,
        EntityCommandBuffer ecb, NativeList<DroneTaskGenerationBlockElement> droppedBlocks)
    {
        var cargo = manager.GetComponentData<DroneCargoState>(assignment.Worker);
        int quantity = math.min(maximum, cargo.Quantity);
        if (quantity <= 0) return 0;
        using var databases = manager.CreateEntityQuery(typeof(ItemPrefabDatabase), typeof(ItemPrefabElement));
        if (!databases.TryGetSingletonEntity<ItemPrefabDatabase>(out var database) ||
            !PrefabLookupUtility.TryGetItemPrefab(manager.GetBuffer<ItemPrefabElement>(database, true), cargo.ItemType, out var prefab))
        {
            SimulationFailureUtility.Record(ref ecb, "[DroneTaskLifecycleApplySystem] Missing item prefab for cargo drop.");
            return 0;
        }

        for (int index = 0; index < quantity; index++)
        {
            Entity item = ItemLifecycleUtility.SpawnPrefabItem(ref ecb, prefab, cargo.ItemType, position);
            ecb.AddComponent<DroneRecoveryPending>(item);
            if (cargo.BlockAutomaticRecoveryOnDrop)
            {
                droppedBlocks.Add(new DroneTaskGenerationBlockElement
                {
                    Key = DroneTaskGenerationKey.From(DroneLogisticsTaskKindEnum.WorldItemRecovery, item, cargo.ItemType)
                });
            }
        }
        return quantity;
    }

    private struct StoreCargoStorageLimits
    {
        public bool HasReadRegistry;
        public int MaxStack;
        public bool HasReadSlots;
        public int SlotCount;
        public int DedicatedSlotCount;
    }

    private static int FindPreviousStorageSlot(EntityManager manager, in DroneTaskAssignment assignment, Entity registry,
        List<DroneItemTransferSlotDecisionElement> budget, bool generalOnly, ref StoreCargoStorageLimits limits)
    {
        if (!limits.HasReadRegistry)
        {
            if (registry == Entity.Null)
            {
                return -1;
            }
            if (!manager.HasComponent<ItemRegistry>(registry))
            {
                return -1;
            }
            if (!manager.HasBuffer<ItemConfigElement>(registry))
            {
                return -1;
            }
            limits.MaxStack = ItemRegistry.GetMaxStack(manager.GetBuffer<ItemConfigElement>(registry, true), assignment.ItemType);
            limits.HasReadRegistry = true;
        }
        int maxStack = limits.MaxStack;
        var storageRead = StationStorageUtility.StorageReadContext.Create(manager, assignment.Destination);
        for (int i = 0; i < budget.Count; i++)
        {
            var slot = budget[i];
            if (generalOnly && !limits.HasReadSlots)
            {
                limits.SlotCount = storageRead.SlotCount;
                limits.DedicatedSlotCount = storageRead.DedicatedSlotCount;
                limits.HasReadSlots = true;
            }
            if (generalOnly && slot.SlotIndex >= limits.SlotCount - limits.DedicatedSlotCount)
            {
                continue;
            }
            if (slot.ItemType != ItemTypeEnum.None && slot.ItemType != assignment.ItemType) continue;
            if (slot.ItemCount >= maxStack) continue;
            if (DroneSchedulingUtility.CanStoreInSlot(manager, assignment.Destination, assignment.ItemType, slot.SlotIndex,
                maxStack, ref limits.SlotCount, ref limits.DedicatedSlotCount, ref limits.HasReadSlots, assignment.DestinationArea,
                ref storageRead, assignment.Worker))
            {
                return i;
            }
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
        bool hasTransportReservation = manager.HasComponent<DroneTransportReservation>(entity);
        if (hasTransportReservation)
        {
            var reservation = manager.GetComponentData<DroneTransportReservation>(entity);
            var progress = manager.GetComponentData<DroneTransportProgress>(reservation.Task);
            DroneTransportReservationUtility.TryReduce(ref progress, ref reservation, moved);
            manager.SetComponentData(reservation.Task, progress);
            manager.SetComponentData(entity, reservation);
        }
        DroneDemolitionStorageUtility.ResizeClaim(manager, assignment.Destination, assignment.Worker, moved);
        assignment.AssignedQuantity = moved;
        if (moved > 0)
        {
            bool recovery = assignment.NextAction == DroneActionKindEnum.RecoverWorldItem;
            bool demolition = manager.GetComponentData<DroneLogisticsTask>(assignment.Task).DemolitionRoot != Entity.Null;
            manager.SetComponentData(assignment.Worker, new DroneCargoState
            {
                ItemType = assignment.ItemType,
                Quantity = moved,
                Origin = demolition ? DroneCargoOriginEnum.DemolitionRecovery :
                    recovery ? DroneCargoOriginEnum.Recovery : DroneCargoOriginEnum.Supply
            });
            assignment.State = DroneTaskAssignmentStateEnum.MovingToDestination;
            assignment.NextAction = recovery || hasTransportReservation
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
        bool hasCargo = manager.GetComponentData<DroneCargoState>(assignment.Worker).Quantity > 0;
        if (assignment.NextAction == DroneActionKindEnum.StoreCargo && manager.HasComponent<DroneTransportReservation>(entity))
        {
            var reservation = manager.GetComponentData<DroneTransportReservation>(entity);
            var progress = manager.GetComponentData<DroneTransportProgress>(reservation.Task);
            // 계획/최종 인계 상한이 해당 확보 잔량이므로 실제 성공량은 이를 넘지 않는다.
            DroneTransportReservationUtility.TryRecordDelivery(ref progress, ref reservation, moved);
            bool waitingForSpace = hasCargo && reservation.RemainingQuantity > 0 &&
                manager.GetComponentData<DroneBatteryState>(assignment.Worker).CurrentCharge > 0f &&
                DroneSchedulingUtility.CanReceiveStorageItem(manager, assignment.Destination, assignment.ItemType, registry, assignment.DestinationArea);
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
        DroneDemolitionStorageUtility.ResizeClaim(manager, assignment.Destination, assignment.Worker, 0);
        assignment.State = hasCargo ? DroneTaskAssignmentStateEnum.Retargeting : DroneTaskAssignmentStateEnum.Completed;
        assignment.NextAction = DroneActionKindEnum.None;
        if (!hasCargo)
        {
            manager.SetComponentData(assignment.Worker, new DroneCargoState());
            manager.SetComponentData(assignment.Worker, new DroneWorkerAssignment());
        }
    }
}
