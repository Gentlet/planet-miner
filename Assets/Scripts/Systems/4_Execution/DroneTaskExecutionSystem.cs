using Unity.Collections;
using Unity.Entities;

/// <summary>
/// 역할·목적: Execution에서 Decision의 작업 생성과 경로 Create/Retain/Remove 의도를 실행한다.
/// 입력: 공통 후보 엔티티의 생성/경로 의도, 기존 작업과 의도가 참조하는 현재 대상·관측.
/// 출력·소유권: World 작업 순번을 발급하고 작업·경로 요청 생성/삭제를 EndStateApply ECB에 기록한다.
/// 적용 검사만 수행하며 작업 필요 여부나 경로 사용 여부를 다시 탐색하지 않고 실제 이동/행동도 수행하지 않는다.
/// 정리·가시화: 소비한 의도 버퍼는 비우고 생성/삭제는 EndStateApply에서 확정한다.
/// 생성된 작업과 경로 요청은 다음 시뮬레이션의 Decision 및 외부 경로 평가자가 이용한다.
/// </summary>
[UpdateInGroup(typeof(ExecutionGroup))]
public partial struct DroneTaskExecutionSystem : ISystem
{
    private Entity _sequenceEntity;
    private Entity _candidates;
    private EntityQuery _existingTasks;
    private EntityQuery _capacity;

    public void OnCreate(ref SystemState state)
    {
        _sequenceEntity = DroneTaskCreationUtility.GetOrCreateSequence(state.EntityManager);
        _candidates = DroneSchedulingUtility.GetOrCreateCandidates(state.EntityManager);
        _existingTasks = state.GetEntityQuery(ComponentType.ReadOnly<DroneLogisticsTask>());
        _capacity = state.GetEntityQuery(ComponentType.ReadOnly<DroneCapacityState>());
    }

    public void OnUpdate(ref SystemState state)
    {
        state.CompleteDependency();
        var manager = state.EntityManager;
        int carryingCapacity = DroneSchedulingUtility.ReadCarryingCapacity(_capacity);
        var ecb = state.World.GetOrCreateSystemManaged<EndStateApplyEntityCommandBufferSystem>().CreateCommandBuffer();
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
            }
            else if (decision.Kind == DroneRouteDecisionKindEnum.Remove)
            {
                if (decision.ExistingRequest == Entity.Null) continue;
                if (!manager.HasComponent<DroneRouteEvaluationRequest>(decision.ExistingRequest)) continue;
                ecb.DestroyEntity(decision.ExistingRequest);
            }
            // Retain은 실제 ECS/ECB 변경 없이 유지한다.
        }
        decisions.Clear();
    }

    private void ExecuteCreationDecisions(EntityManager manager, EntityCommandBuffer ecb)
    {
        var decisions = manager.GetBuffer<DroneTaskCreationDecisionElement>(_candidates);
        var sequence = manager.GetComponentData<DroneTaskSequence>(_sequenceEntity);
        using var tasks = _existingTasks.ToComponentDataArray<DroneLogisticsTask>(Allocator.Temp);
        // ECB에 새로 기록한 작업은 아직 쿼리에 없으므로 로컬 기록으로 같은 틱 중복 생성도 막는다.
        using var recorded = new NativeList<DroneTaskCreationDecisionElement>(Allocator.Temp);
        for (int i = 0; i < decisions.Length; i++)
        {
            var decision = decisions[i];
            if (!IsCreationTargetValid(manager, decision))
            {
                continue;
            }

            if (HasExistingCreation(tasks, decision) || HasRecordedCreation(recorded, decision))
            {
                continue;
            }

            // 배정이 아니라 품목별 공급 root 또는 실물별 회수 root를 기록하고 실제 생성에만 순번을 사용한다.
            if (decision.Kind == DroneLogisticsTaskKindEnum.ConstructionSupply)
            {
                DroneTaskCreationUtility.CreateConstructionSupplyTask(ecb, decision.Target, decision.ItemType, ref sequence);
            }
            else
            {
                DroneTaskCreationUtility.CreateRecoveryTask(ecb, decision.Target, decision.ItemType,
                    decision.RecoveryReason, ref sequence);
            }

            recorded.Add(decision);
        }

        manager.SetComponentData(_sequenceEntity, sequence);
        decisions.Clear();
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

        if (decision.Kind == DroneLogisticsTaskKindEnum.WorldItemRecovery)
        {
            return DroneSchedulingUtility.IsWorldItem(manager, decision.Target, decision.ItemType);
        }

        return false;
    }

    private static bool HasExistingCreation(NativeArray<DroneLogisticsTask> tasks,
        in DroneTaskCreationDecisionElement decision)
    {
        for (int i = 0; i < tasks.Length; i++)
        {
            var task = tasks[i];
            if (task.Kind != decision.Kind || task.Target != decision.Target)
            {
                continue;
            }

            if (decision.Kind == DroneLogisticsTaskKindEnum.ConstructionSupply && task.ItemType == decision.ItemType)
            {
                return true;
            }

            if (decision.Kind == DroneLogisticsTaskKindEnum.WorldItemRecovery && task.State == DroneLogisticsTaskStateEnum.Open)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasRecordedCreation(NativeList<DroneTaskCreationDecisionElement> recorded,
        in DroneTaskCreationDecisionElement decision)
    {
        for (int i = 0; i < recorded.Length; i++)
        {
            var other = recorded[i];
            if (other.Kind == decision.Kind && other.Target == decision.Target &&
                (decision.Kind == DroneLogisticsTaskKindEnum.WorldItemRecovery || other.ItemType == decision.ItemType))
            {
                return true;
            }
        }

        return false;
    }
}
