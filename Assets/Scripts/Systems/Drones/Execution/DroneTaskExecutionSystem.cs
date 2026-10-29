using Unity.Collections;
using Unity.Entities;

/// <summary>
/// 역할·목적: Execution에서 Decision의 작업 생성과 경로 Create/Retain/Remove 의도를 실행한다.
/// 입력: 공통 후보 엔티티의 생성/경로 의도와 현재 대상/관측. 기존 root 중복은 Decision이 보장한다.
/// 출력·소유권: World 작업 순번을 발급하고 작업·경로 요청 생성/삭제를 EndSimulation ECB에 기록한다.
/// 적용 검사만 수행하며 작업 필요 여부나 경로 사용 여부를 다시 탐색하지 않고 실제 이동/행동도 수행하지 않는다.
/// 정리·가시화: 소비한 의도 버퍼는 비우고 생성/삭제는 EndSimulation에서 확정한다.
/// 생성된 작업과 경로 요청은 다음 Decision부터 이용한다. 유지되는 요청의 경로 판단도 같은 EndSimulation에 공개한다.
/// </summary>
[UpdateInGroup(typeof(DroneExecutionGroup))]
public partial struct DroneTaskExecutionSystem : ISystem
{
    private Entity _sequenceEntity;
    private Entity _candidates;
    private EntityQuery _capacity;
    private EntityQuery _taskRequests;

    public void OnCreate(ref SystemState state)
    {
        _sequenceEntity = DroneTaskCreationUtility.GetOrCreateSequence(state.EntityManager);
        _candidates = DroneSchedulingUtility.GetOrCreateCandidates(state.EntityManager);
        _taskRequests = state.GetEntityQuery(typeof(DroneTaskRequest), typeof(DroneTaskRequestDecision));
        _capacity = state.GetEntityQuery(ComponentType.ReadOnly<DroneCapacityState>());
    }

    public void OnUpdate(ref SystemState state)
    {
        state.CompleteDependency();
        var manager = state.EntityManager;
        int carryingCapacity = DroneSchedulingUtility.ReadCarryingCapacity(_capacity);
        var ecb = state.World.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>().CreateCommandBuffer();
        ExecuteCreationDecisions(manager, ecb);
        ExecuteRouteDecisions(manager, ecb, carryingCapacity);
    }

    private void ExecuteRouteDecisions(EntityManager manager, EntityCommandBuffer ecb, int carryingCapacity)
    {
        var decisions = manager.GetBuffer<DroneRouteDecisionElement>(_candidates);
        // Decision이 명시한 대상만 처리한다. 낡은 생성 의도는 버리고 다음 Decision이 다시 판단하게 한다.
        for (int i = 0; i < decisions.Length; i++)
        {
            var decision = decisions[i];
            if (decision.Kind == DroneRouteDecisionKindEnum.Create)
            {
                if (!DroneSchedulingUtility.RouteIsCurrent(manager, decision.Request, carryingCapacity)) continue;
                Entity request = ecb.CreateEntity();
                ecb.AddComponent(request, decision.Request);
                ecb.AddComponent(request, new DroneRouteEvaluationResult { EvaluationRevision = decision.Request.EvaluationRevision });
                ecb.AddComponent<DroneRouteEvaluationDecision>(request);
            }
            else if (decision.Kind == DroneRouteDecisionKindEnum.Remove)
            {
                if (decision.ExistingRequest == Entity.Null) continue;
                if (!manager.HasComponent<DroneRouteEvaluationRequest>(decision.ExistingRequest)) continue;
                ecb.DestroyEntity(decision.ExistingRequest);
            }
            else if (decision.Kind == DroneRouteDecisionKindEnum.Retain)
            {
                if (manager.HasComponent<DroneRouteEvaluationDecision>(decision.ExistingRequest))
                {
                    var evaluation = manager.GetComponentData<DroneRouteEvaluationDecision>(decision.ExistingRequest);
                    if (evaluation.Ready)
                    {
                        // 삭제 의도와 같은 ECB에서 분기하므로 삭제 뒤 결과 SetComponent를 기록하지 않는다.
                        ecb.SetComponent(decision.ExistingRequest, evaluation.Result);
                    }
                }
            }
        }
        decisions.Clear();
    }

    private void ExecuteCreationDecisions(EntityManager manager, EntityCommandBuffer ecb)
    {
        var decisions = manager.GetBuffer<DroneTaskCreationDecisionElement>(_candidates);
        var sequence = manager.GetComponentData<DroneTaskSequence>(_sequenceEntity);
        // ECB에 새로 기록한 작업은 아직 쿼리에 없으므로 로컬 기록으로 같은 틱 중복 생성도 막는다.
        using var recorded = new NativeList<RecordedCreation>(Allocator.Temp);
        ExecuteCreationRequests(manager, ecb, ref sequence, recorded);
        for (int i = 0; i < decisions.Length; i++)
        {
            var decision = decisions[i];
            if (!IsCreationTargetValid(manager, decision))
            {
                continue;
            }

            if (HasRecordedCreation(recorded, decision))
            {
                continue;
            }

            // 배정이 아니라 품목별 공급 root 또는 실물별 회수 root를 기록하고 실제 생성에만 순번을 사용한다.
            Entity task = RecordCreation(ecb, decision, ref sequence);
            recorded.Add(new RecordedCreation { Creation = decision, Task = task });
        }

        manager.SetComponentData(_sequenceEntity, sequence);
        decisions.Clear();
    }

    private struct RecordedCreation
    {
        public DroneTaskCreationDecisionElement Creation;
        public Entity Request;
        public Entity Task;
    }

    private static Entity RecordCreation(EntityCommandBuffer ecb, in DroneTaskCreationDecisionElement creation, ref DroneTaskSequence sequence)
    {
        switch (creation.Kind)
        {
            case DroneLogisticsTaskKindEnum.ConstructionSupply:
                return DroneTaskCreationUtility.CreateConstructionSupplyTask(ecb, creation.Target, creation.ItemType, ref sequence);
            case DroneLogisticsTaskKindEnum.WorldItemRecovery:
                return DroneTaskCreationUtility.CreateRecoveryTask(ecb, creation.Target, creation.ItemType, creation.RecoveryReason, ref sequence);
            default:
                return DroneTaskCreationUtility.CreateStorageTransportTask(ecb, creation.Target, creation.ItemType,
                    creation.Kind, creation.Quantity, ref sequence);
        }
    }

    private void ExecuteCreationRequests(EntityManager manager, EntityCommandBuffer ecb, ref DroneTaskSequence sequence, NativeList<RecordedCreation> recorded)
    {
        using var requests = DroneTaskRequestDecisionUtility.ReadRequests(manager, _taskRequests);
        foreach (var record in requests)
        {
            var request = record.Request;
            if (request.Operation == DroneTaskRequestOperationEnum.Cancel)
            {
                continue;
            }

            var decision = manager.GetComponentData<DroneTaskRequestDecision>(record.Entity);
            var status = decision.Status;
            Entity task = decision.Task;
            var creation = new DroneTaskCreationDecisionElement
            {
                Kind = request.Kind,
                Target = request.Target,
                ItemType = request.ItemType,
                RecoveryReason = decision.RecoveryReason,
                Quantity = request.Quantity
            };
            bool accepted = status == DroneTaskRequestStatusEnum.Created || status == DroneTaskRequestStatusEnum.Existing;
            if (accepted && !IsCreationTargetValid(manager, creation))
            {
                status = DroneTaskRequestStatusEnum.Rejected;
                accepted = false;
            }

            if (accepted && status == DroneTaskRequestStatusEnum.Created)
            {
                task = RecordCreation(ecb, creation, ref sequence);
                recorded.Add(new RecordedCreation { Creation = creation, Request = record.Entity, Task = task });
            }
            else if (accepted && task == Entity.Null)
            {
                foreach (var prior in recorded)
                {
                    if (prior.Request == decision.CreatingRequest)
                    {
                        task = prior.Task;
                        break;
                    }
                }

                if (task == Entity.Null)
                {
                    status = DroneTaskRequestStatusEnum.Rejected;
                    accepted = false;
                }
            }

            manager.SetComponentData(record.Entity, new DroneTaskRequestExecution { CreationRecorded = accepted });
            // 새 Task의 deferred Entity는 생성과 같은 ECB의 결과에만 기록한다.
            DroneTaskRequestUtility.RecordResult(ecb, record.Entity, request.Operation, status, task);
        }
    }

    private static bool IsCreationTargetValid(EntityManager manager, in DroneTaskCreationDecisionElement decision)
    {
        // 명령을 적용할 대상의 생존·종류만 확인한다. 요구량/회수 필요성 탐색은 Decision으로 되돌리지 않는다.
        if (decision.ItemType == ItemTypeEnum.None)
        {
            return false;
        }

        if (decision.Kind == DroneLogisticsTaskKindEnum.ConstructionSupply)
        {
            return DroneSchedulingUtility.IsSite(manager, decision.Target);
        }

        if (DroneSchedulingUtility.IsTransportKind(decision.Kind))
        {
            return DroneSchedulingUtility.IsStorage(manager, decision.Target);
        }

        if (decision.Kind == DroneLogisticsTaskKindEnum.WorldItemRecovery)
        {
            return DroneSchedulingUtility.IsWorldItem(manager, decision.Target, decision.ItemType);
        }

        return false;
    }

    private static bool HasRecordedCreation(NativeList<RecordedCreation> recorded,
        in DroneTaskCreationDecisionElement decision)
    {
        for (int i = 0; i < recorded.Length; i++)
        {
            var other = recorded[i].Creation;
            if (other.Kind == decision.Kind && other.Target == decision.Target &&
                (decision.Kind == DroneLogisticsTaskKindEnum.WorldItemRecovery || other.ItemType == decision.ItemType))
            {
                return true;
            }
        }

        return false;
    }
}
