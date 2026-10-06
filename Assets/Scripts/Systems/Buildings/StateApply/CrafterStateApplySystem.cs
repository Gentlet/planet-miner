using Unity.Burst;
using Unity.Entities;

/// <summary>
/// 역할·목적: StateApply에서 제작 Decision의 다음 상태를 영속 CrafterState.Status로 확정한다.
/// 입력·생성자: CrafterDecisionSystem이 제작기 엔티티에 기록한 활성 CrafterStateDecision.NextStatus.
/// 출력·소유권: Status만 갱신하며 진행도/활성 제작 여부/재료 소비는 Command와 Execution의 책임을 유지한다.
/// 정리·가시화: 적용 뒤 결정을 즉시 비활성화하고 다음 Decision/입고 판단이 확정 상태를 읽는다. ECB 구조 변경은 없다.
/// </summary>
[UpdateInGroup(typeof(BuildingStateApplyGroup))]
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
/// IJobEntity가 Execute 시그니처를 기준으로 Query를 자동 생성.
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

        // Consume-on-Apply: 동일 상태 전이 결정이 다음 프레임에 다시 적용되지 않도록 즉시 비활성화.
        decisionEnabled.ValueRW = false;
    }
}
