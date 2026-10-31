using Unity.Entities;

/// <summary>
/// 역할·목적: StateApply 마지막에 DroneTaskReservationSystem의 공개 대기 기록을 재검사하고 수행자가 이용할 배정을 공개한다.
/// 입력: 기존 공개 대기/일반 운반 승인 버퍼와 최신 작업/수행자/적재/현장/경로/공통 적재량을 읽는다.
/// 생성/이용: 신규 배정 또는 기존 적재 배정과 유형별 현장/일반 운반 확보 기록을 기록하고,
/// 수행자 엔티티의 DroneWorkerAssignment를 연결한다. 수행자가 보는 새 배정은 EndSimulation 재생 후부터 유효하다.
/// 소유권/정리: 승인량 안에서 감량하고 차액/거부 확보량을 즉시 반환한다. 실패한 후보는 여기서 다시 선택하지 않는다.
/// Take 후 원본 기록을 비우고 PublicationQueued로 중복 공개/Playback 전 정거장 전환을 막는다.
/// 실물/적재 출처와 수행자의 관측 상태는 바꾸지 않으며, 공개 대기 버퍼는 다음 Reservation에서 정리한다.
/// </summary>
[UpdateInGroup(typeof(DroneStateApplyGroup), OrderLast = true)]
public partial struct DroneTaskAssignmentPublishSystem : ISystem
{
    private Entity _candidates;
    private EntityQuery _registry;
    private EntityQuery _capacity;

    public void OnCreate(ref SystemState state)
    {
        _candidates = DroneSchedulingUtility.GetOrCreateCandidates(state.EntityManager);
        _registry = state.GetEntityQuery(ComponentType.ReadOnly<ItemRegistry>(), ComponentType.ReadOnly<ItemConfigElement>());
        _capacity = state.GetEntityQuery(ComponentType.ReadOnly<DroneCapacityState>());
    }

    public void OnUpdate(ref SystemState state)
    {
        state.CompleteDependency();
        var manager = state.EntityManager;
        int carryingCapacity = DroneSchedulingUtility.ReadCarryingCapacity(_capacity);
        var ecb = state.World.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>().CreateCommandBuffer();
        var pendingPublications = manager.GetBuffer<DroneTaskPendingPublicationElement>(_candidates);
        Entity registry = _registry.IsEmptyIgnoreFilter ? Entity.Null : _registry.GetSingletonEntity();
        PublishExistingAssignments(manager, registry, carryingCapacity, ecb, pendingPublications);
        var transportPending = manager.GetBuffer<DroneTransportPendingReservationElement>(_candidates);
        PublishTransportAssignments(manager, registry, carryingCapacity, ecb, transportPending);
    }

    private static void PublishExistingAssignments(EntityManager manager, Entity registry, int carryingCapacity,
        EntityCommandBuffer ecb, DynamicBuffer<DroneTaskPendingPublicationElement> pendingPublications)
    {
        for (int i = 0; i < pendingPublications.Length; i++)
        {
            var pending = pendingPublications[i];
            if (pending.PublicationQueued) continue;
            if (pending.Quantity <= 0) continue;
            var candidate = pending.Candidate;
            candidate.Quantity = pending.Quantity;
            // 후보 선택 당시와 최종 공개 사이의 상태 차이를 검사한다. 현재 수량과 상태로 공개 가능 여부를 마지막으로 확인한다.
            int quantity = DroneSchedulingUtility.CandidateQuantity(manager, candidate, registry, carryingCapacity,
                pending.CommittedQuantity);
            bool supply = pending.CommittedQuantity > 0;
            if (supply && quantity < pending.CommittedQuantity)
            {
                // 현장 합계는 이미 증가했지만 개별 배정은 아직 공개되지 않았다. 감소분을 되돌려 유령 예약을 남기지 않는다.
                ConstructionSupplyReservationUtility.Release(manager, candidate.Destination, candidate.ItemType,
                    pending.CommittedQuantity - quantity);
                pending.CommittedQuantity = quantity;
            }

            if (quantity <= 0)
            {
                pending.Quantity = 0;
                pendingPublications[i] = pending;
                continue;
            }

            if (candidate.Assignment != Entity.Null)
            {
                if (!PublishRetarget(manager, candidate, quantity, ecb))
                {
                    // 적재 배정 revision/연결이 바뀌어 공개가 실패하면 후보에만 남아 있는 현장 예약도 함께 해제한다.
                    ConstructionSupplyReservationUtility.Release(manager, candidate.Destination, candidate.ItemType,
                        pending.CommittedQuantity);
                    pending.CommittedQuantity = 0;
                    pending.Quantity = 0;
                }
                else
                {
                    pending.Quantity = quantity;
                    pending.PublicationQueued = true;
                }
                pendingPublications[i] = pending;
                continue;
            }

            bool demolition = manager.GetComponentData<DroneLogisticsTask>(candidate.Task).Kind == DroneLogisticsTaskKindEnum.BuildingDemolition;
            Entity assignment = RecordNewAssignment(manager, candidate, quantity,
                demolition ? DroneActionKindEnum.DemolishBuilding :
                supply ? DroneActionKindEnum.CollectFromStorage : DroneActionKindEnum.RecoverWorldItem, ecb);
            if (!demolition)
            {
                ecb.AddComponent(assignment, new ConstructionSupplyReservation
                {
                    Site = supply ? candidate.Destination : Entity.Null,
                    ItemType = candidate.ItemType,
                    AssignmentRevision = 1,
                    RemainingQuantity = supply ? quantity : 0
                });
            }
            // PublicationQueued는 ECB 기록 완료 표시다. Playback 전 반복 실행과 다음 틱 미공개 예약 롤백을 구분한다.
            pending.Quantity = quantity;
            pending.PublicationQueued = true;
            pendingPublications[i] = pending;
        }
    }

    private static void PublishTransportAssignments(EntityManager manager, Entity registry, int carryingCapacity,
        EntityCommandBuffer ecb, DynamicBuffer<DroneTransportPendingReservationElement> pendingReservations)
    {
        for (int i = 0; i < pendingReservations.Length; i++)
        {
            var pending = pendingReservations[i];
            if (pending.PublicationQueued)
            {
                continue;
            }
            if (pending.Reservation.RemainingQuantity <= 0)
            {
                continue;
            }
            var candidate = pending.Candidate;
            candidate.Quantity = pending.Reservation.RemainingQuantity;
            int quantity = DroneSchedulingUtility.CandidateQuantity(manager, candidate, registry, carryingCapacity);
            var reservation = DroneTransportReservationUtility.Take(ref pending);
            if (quantity <= 0)
            {
                ReleaseTransportReservation(manager, ref reservation);
                pendingReservations[i] = pending;
                continue;
            }
            if (quantity < reservation.RemainingQuantity)
            {
                // quantity는 승인 상한 안의 양수다. 감량분만 반환하고 최초 목표량/실적은 바꾸지 않는다.
                var progress = manager.GetComponentData<DroneTransportProgress>(reservation.Task);
                DroneTransportReservationUtility.TryReduce(ref progress, ref reservation, quantity);
                manager.SetComponentData(reservation.Task, progress);
            }

            Entity assignment = RecordNewAssignment(manager, candidate, quantity, DroneActionKindEnum.CollectFromStorage, ecb);
            ecb.AddComponent(assignment, reservation);
            pending.PublicationQueued = true;
            pendingReservations[i] = pending;
        }
    }

    private static void ReleaseTransportReservation(EntityManager manager, ref DroneTransportReservation reservation)
    {
        if (!manager.HasComponent<DroneTransportProgress>(reservation.Task))
        {
            reservation.RemainingQuantity = 0;
            return;
        }
        var progress = manager.GetComponentData<DroneTransportProgress>(reservation.Task);
        DroneTransportReservationUtility.Release(ref progress, ref reservation);
        manager.SetComponentData(reservation.Task, progress);
    }

    private static Entity RecordNewAssignment(EntityManager manager, in DroneTaskCandidateDecisionElement candidate,
        int quantity, DroneActionKindEnum nextAction, EntityCommandBuffer ecb)
    {
        // 배정/유형별 확보 기록/수행자 연결은 같은 ECB에서만 공개한다. 그 전에는 pending이 인계 근거다.
        Entity assignment = ecb.CreateEntity();
        ecb.AddComponent(assignment, new DroneTaskAssignment
        {
            Worker = candidate.Worker,
            Task = candidate.Task,
            OriginalTaskCreationSequence = manager.GetComponentData<DroneLogisticsTask>(candidate.Task).CreationSequence,
            Source = candidate.Source,
            Destination = candidate.Destination,
            ItemType = candidate.ItemType,
            AssignedQuantity = quantity,
            Revision = 1,
            State = nextAction == DroneActionKindEnum.DemolishBuilding
                ? DroneTaskAssignmentStateEnum.MovingToDestination : DroneTaskAssignmentStateEnum.MovingToSource,
            NextAction = nextAction
        });
        ecb.SetComponent(candidate.Worker, new DroneWorkerAssignment { Assignment = assignment });
        return assignment;
    }

    private static bool PublishRetarget(EntityManager manager, in DroneTaskCandidateDecisionElement candidate,
        int quantity, EntityCommandBuffer ecb)
    {
        if (!DroneSchedulingUtility.IsRetargetingWorker(manager, candidate.Assignment, out var assignment, out _)) return false;
        if (assignment.Revision != candidate.AssignmentRevision || assignment.Revision == uint.MaxValue) return false;
        bool supply = candidate.NextAction == DroneActionKindEnum.SupplyConstructionSite;
        ulong originalOrder = DroneSchedulingUtility.AssignmentCreationSequence(manager, assignment);
        // 같은 배정 엔티티를 유지해 기존 결과의 참조와 적재 출처를 보존한다. revision 증가로 이전 경로/행동 신호만 무효화한다.
        // 적재품의 최초 작업 순서는 유지하고 새 행동 번호는 0에서 시작한다. 공통 용량을 이미 실린 수량에 소급 적용하지 않는다.
        assignment.Revision++;
        assignment.Task = candidate.Task;
        assignment.OriginalTaskCreationSequence = originalOrder;
        assignment.Source = Entity.Null;
        assignment.Destination = candidate.Destination;
        assignment.AssignedQuantity = quantity;
        assignment.LastAppliedActionSequence = 0;
        assignment.NextAction = candidate.NextAction;
        assignment.DropPosition = candidate.DropPosition;
        assignment.State = DroneTaskAssignmentStateEnum.MovingToDestination;
        ecb.SetComponent(candidate.Assignment, assignment);
        if (manager.HasComponent<DroneTransportReservation>(candidate.Assignment))
        {
            var previousReservation = manager.GetComponentData<DroneTransportReservation>(candidate.Assignment);
            if (previousReservation.RemainingQuantity == 0)
            {
                // 정산된 원래 목표의 기록을 새 배정과 함께 분리한다. 새 보관/방출에 이전 root의 유효성을 강제하지 않는다.
                ecb.RemoveComponent<DroneTransportReservation>(candidate.Assignment);
            }
        }
        var reservation = new ConstructionSupplyReservation
        {
            Site = supply ? candidate.Destination : Entity.Null,
            ItemType = assignment.ItemType,
            AssignmentRevision = assignment.Revision,
            RemainingQuantity = supply ? quantity : 0
        };
        if (manager.HasComponent<ConstructionSupplyReservation>(candidate.Assignment))
            ecb.SetComponent(candidate.Assignment, reservation);
        else ecb.AddComponent(candidate.Assignment, reservation);
        ecb.SetComponent(candidate.Worker, new DroneWorkerAssignment { Assignment = candidate.Assignment });
        return true;
    }
}
