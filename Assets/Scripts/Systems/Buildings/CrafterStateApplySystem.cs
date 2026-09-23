using Unity.Burst;
using Unity.Entities;

/// <summary>
/// CrafterDecisionSystem이 계산한 상태 전이 결정을 CrafterState에 최종 반영하는 State Owner 시스템.
///
/// [책임]
/// - StateApplyGroup(Phase 5)에서 실행됩니다.
/// - 활성화된 CrafterStateDecision.NextStatus를 CrafterState.Status에 반영합니다.
/// - Consume-on-Apply 원칙에 따라 반영 후 CrafterStateDecision을 즉시 비활성화합니다.
///
/// [Architecture V2]
/// - Decision: CrafterState Read Only -> CrafterStateDecision.NextStatus 산출 및 활성화
/// - StateApply: CrafterStateDecision.NextStatus -> CrafterState.Status 확정 후 비활성화
/// </summary>
[UpdateInGroup(typeof(StateApplyGroup))]
[BurstCompile]
public partial struct CrafterStateApplySystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var job = new CrafterStateApplyJob();
        state.Dependency = job.ScheduleParallel(state.Dependency);
    }
}

/// <summary>
/// 활성화된 CrafterStateDecision을 Persistent State에 반영하고 즉시 소비하는 병렬 Burst Job.
/// IJobEntity가 Execute 시그니처를 기준으로 Query를 자동 생성합니다.
/// </summary>
[BurstCompile]
public partial struct CrafterStateApplyJob : IJobEntity
{
    public void Execute(
        ref CrafterState state,
        ref CrafterStateDecision decision,
        EnabledRefRW<CrafterStateDecision> decisionEnabled)
    {
        state.Status = decision.NextStatus;

        // Consume-on-Apply: 동일 상태 전이 결정이 다음 프레임에 다시 적용되지 않도록 즉시 비활성화합니다.
        decisionEnabled.ValueRW = false;
    }
}
