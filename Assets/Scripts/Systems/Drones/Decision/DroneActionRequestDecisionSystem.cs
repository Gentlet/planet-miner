using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// 공개된 수집/납품/안전 방출 도착으로 이번 틱 인계 요청을 판단한다. 별도 타이머나 실물 Writer는 없다.
/// 후보 판단은 병렬 Job이며 기존 접수 번호/요청 생성 API 호출만 메인 스레드에서 수행한다.
/// 자신이 생성한 결과만 다음 Decision에 소비하여 종료 배정의 참조를 남기지 않는다.
/// </summary>
[UpdateInGroup(typeof(DroneDecisionGroup))]
[UpdateAfter(typeof(DroneTaskDecisionSystem))]
[UpdateBefore(typeof(DroneItemTransferDecisionSystem))]
public partial struct DroneActionRequestDecisionSystem : ISystem
{
    private EntityQuery _ownedRequests;
    private EntityQuery _pendingRequests;
    private Entity _candidates;

    public void OnCreate(ref SystemState state)
    {
        _ownedRequests = state.GetEntityQuery(ComponentType.ReadOnly<DroneAutomaticActionRequest>());
        _pendingRequests = state.GetEntityQuery(ComponentType.ReadOnly<DroneActionReadyRequest>());
        _candidates = DroneSchedulingUtility.GetOrCreateCandidates(state.EntityManager);
    }

    public void OnUpdate(ref SystemState state)
    {
        var manager = state.EntityManager;
        using (var owned = _ownedRequests.ToEntityArray(Allocator.Temp))
        {
            for (int i = 0; i < owned.Length; i++)
            {
                DroneActionRequestUtility.TryConsumeResult(manager, owned[i], out _);
            }
        }

        using var decisions = new NativeQueue<ActionIntent>(Allocator.TempJob);
        state.Dependency = new PrepareActionsJob
        {
            Assignments = SystemAPI.GetComponentLookup<DroneTaskAssignment>(true),
            Invalidations = SystemAPI.GetBufferLookup<DroneTaskInvalidationDecisionElement>(true),
            Candidates = _candidates,
            Decisions = decisions.AsParallelWriter()
        }.ScheduleParallel(state.Dependency);
        // 접수 번호와 구조 변경은 기존 API가 소유하므로 이 판단 Job의 완료만 기다린다.
        state.Dependency.Complete();
        using var ordered = new NativeList<ActionIntent>(Allocator.Temp);
        while (decisions.TryDequeue(out var decision))
        {
            ordered.Add(decision);
        }
        ordered.AsArray().Sort(new ActionOrder());
        using var pending = _pendingRequests.ToComponentDataArray<DroneActionReadyRequest>(Allocator.Temp);
        for (int i = 0; i < ordered.Length; i++)
        {
            var intent = ordered[i];
            if (HasPending(pending, intent.Request.Action))
            {
                continue;
            }
            var assignment = manager.GetComponentData<DroneTaskAssignment>(intent.Request.Action.Assignment);
            bool collection = intent.Request.Action.Kind == DroneActionKindEnum.CollectFromStorage ||
                intent.Request.Action.Kind == DroneActionKindEnum.RecoverWorldItem;
            bool arrived;
            if (collection)
                arrived = DroneItemTransferValidationUtility.IsAtCollectionPoint(manager, assignment);
            else if (intent.Request.Action.Kind == DroneActionKindEnum.DropCargo)
                arrived = math.all(intent.Request.WorldPosition == assignment.DropPosition);
            else
                arrived = DroneItemTransferValidationUtility.IsAtDeliveryPoint(manager, assignment);
            if (!arrived)
            {
                continue;
            }
            Entity request = DroneActionRequestUtility.Submit(manager, intent.Request);
            manager.AddComponent<DroneAutomaticActionRequest>(request);
        }
    }

    private static bool HasPending(NativeArray<DroneActionReadyRequest> pending, in DroneActionIdentity action)
    {
        for (int i = 0; i < pending.Length; i++)
        {
            var existing = pending[i].Action;
            if (existing.Assignment == action.Assignment && existing.AssignmentRevision == action.AssignmentRevision &&
                existing.Worker == action.Worker && existing.Kind == action.Kind)
            {
                return true;
            }
        }
        return false;
    }

    private struct ActionIntent
    {
        public DroneActionReadyRequest Request;
        public ulong TaskCreationSequence;
    }

    private struct ActionOrder : IComparer<ActionIntent>
    {
        public int Compare(ActionIntent first, ActionIntent second)
        {
            int order = first.TaskCreationSequence.CompareTo(second.TaskCreationSequence);
            if (order != 0)
            {
                return order;
            }
            order = first.Request.Action.Worker.Index.CompareTo(second.Request.Action.Worker.Index);
            return order != 0 ? order : first.Request.Action.Worker.Version.CompareTo(second.Request.Action.Worker.Version);
        }
    }

    [BurstCompile]
    [WithNone(typeof(DroneDocked))]
    private partial struct PrepareActionsJob : IJobEntity
    {
        [ReadOnly] public ComponentLookup<DroneTaskAssignment> Assignments;
        [ReadOnly] public BufferLookup<DroneTaskInvalidationDecisionElement> Invalidations;
        public Entity Candidates;
        public NativeQueue<ActionIntent>.ParallelWriter Decisions;

        private void Execute(Entity entity, in DroneWorker worker, in DroneWorkerAssignment link,
            in DroneWorkerObservation observation, in LocalTransform transform, in DroneBatteryState battery,
            in DroneCargoState cargo, in DynamicBuffer<StoredItemElement> items)
        {
            if (link.Assignment == Entity.Null)
            {
                return;
            }
            if (!Assignments.HasComponent(link.Assignment))
            {
                return;
            }
            var assignment = Assignments[link.Assignment];
            if (assignment.Worker != entity || assignment.LastAppliedActionSequence == ulong.MaxValue)
            {
                return;
            }
            bool collection = assignment.State == DroneTaskAssignmentStateEnum.AwaitingCollection &&
                (assignment.NextAction == DroneActionKindEnum.CollectFromStorage || assignment.NextAction == DroneActionKindEnum.RecoverWorldItem);
            bool delivery = assignment.State == DroneTaskAssignmentStateEnum.AwaitingDelivery &&
                (assignment.NextAction == DroneActionKindEnum.SupplyConstructionSite || assignment.NextAction == DroneActionKindEnum.StoreCargo);
            bool dropping = assignment.State == DroneTaskAssignmentStateEnum.AwaitingDelivery &&
                assignment.NextAction == DroneActionKindEnum.DropCargo;
            if (!collection && !delivery && !dropping)
            {
                return;
            }
            if (collection && (battery.CurrentCharge <= 0f || cargo.Origin != DroneCargoOriginEnum.None || !items.IsEmpty))
            {
                return;
            }
            // 공개 도착 뒤 납품/방출에는 추가 이동 에너지가 없다. 0 배터리의 새 목표 선택은 재배정/비행 판단에서 차단한다.
            if ((delivery || dropping) && items.IsEmpty)
            {
                return;
            }
            var invalidations = Invalidations[Candidates];
            for (int i = 0; i < invalidations.Length; i++)
            {
                var invalidation = invalidations[i];
                if ((collection && invalidation.Target == assignment.Task) ||
                    (invalidation.Target == link.Assignment && invalidation.AssignmentRevision == assignment.Revision))
                {
                    return;
                }
            }
            Entity target = collection ? assignment.Source : assignment.Destination;
            if (dropping) target = Entity.Null;
            Decisions.Enqueue(new ActionIntent
            {
                TaskCreationSequence = assignment.OriginalTaskCreationSequence,
                Request = new DroneActionReadyRequest
                {
                    Action = new DroneActionIdentity
                    {
                        Assignment = link.Assignment, AssignmentRevision = assignment.Revision,
                        Worker = entity, Kind = assignment.NextAction, Target = target,
                        Sequence = assignment.LastAppliedActionSequence + 1
                    },
                    WorkerObservationRevision = observation.Revision,
                    WorldPosition = (int2)math.floor(transform.Position.xy)
                }
            });
        }
    }
}
