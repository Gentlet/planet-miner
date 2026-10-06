using Unity.Entities;

/// <summary>
/// 역할·목적: StateApply 마지막에 ConstructionSupplyReservationSystem의 공개 대기 기록을 재검사하고 수행자가 이용할 배정을 공개한다.
/// 입력: 후보 관리 엔티티의 DroneTaskPendingPublicationElement와 최신 작업·수행자·적재·현장·경로·공통 적재량을 읽는다.
/// 생성·이용: 신규 배정 엔티티 또는 기존 적재 배정에 DroneTaskAssignment/ConstructionSupplyReservation을 기록하고,
/// 수행자 엔티티의 DroneWorkerAssignment를 연결한다. 수행자가 보는 새 배정은 EndSimulation 재생 후부터 유효하다.
/// 소유권·정리: Reservation이 먼저 올린 현장 합계 중 무효/축소된 미공개 예약을 되돌리고 PublicationQueued를 기록한다.
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

            // 배정·개별 예약·수행자 연결을 같은 ECB에 기록해 재생 전에는 수행자가 일부 구성만 읽지 못하게 한다.
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
                State = DroneTaskAssignmentStateEnum.MovingToSource,
                NextAction = supply ? DroneActionKindEnum.CollectFromStorage : DroneActionKindEnum.RecoverWorldItem
            });
            ecb.AddComponent(assignment, new ConstructionSupplyReservation
            {
                Site = supply ? candidate.Destination : Entity.Null,
                ItemType = candidate.ItemType,
                AssignmentRevision = 1,
                RemainingQuantity = supply ? quantity : 0
            });
            ecb.SetComponent(candidate.Worker, new DroneWorkerAssignment { Assignment = assignment });
            // PublicationQueued는 ECB 기록 완료 표시다. Playback 전 반복 실행과 다음 틱 미공개 예약 롤백을 구분한다.
            pending.Quantity = quantity;
            pending.PublicationQueued = true;
            pendingPublications[i] = pending;
        }
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
