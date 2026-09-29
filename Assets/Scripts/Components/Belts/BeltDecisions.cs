using Unity.Entities;

/// <summary>
/// [부착 대상: 아이템 엔티티 (Item Entity)]
/// 벨트 위 아이템의 프레임 단위 이동 의사결정 산출물(Decision)을 저장하는 컴포넌트.
/// - DecisionGroup(BeltMovementDecisionSystem)에서 계산하여 기록(Write)하고,
///   ExecutionGroup(BeltMovementExecutionSystem)에서 실제 이동 반영 후 소비.
/// - 처리 대상 여부는 BeltMovementState의 Enabled 상태로 결정.
/// - BeltMovementState와 Decision 데이터를 분리하여 동일 Job 내 상태/결정 책임 분리.
/// </summary>
public struct BeltMovementDecision : IComponentData
{
    /// <summary>
    /// 이번 프레임에 전진할 확정 진행률 (Progress 스케일 [0.0 ~ 1.0]).
    /// </summary>
    public float PlannedProgress;

    /// <summary>
    /// 앞선 아이템과의 간격 부족이나 벨트 끝 정체(Backpressure)로 인해 전진하지 못한 상태인지 여부.
    /// </summary>
    public bool IsBlocked;

    public BeltMovementDecision(float plannedProgress = 0.0f, bool isBlocked = false)
    {
        PlannedProgress = plannedProgress;
        IsBlocked = isBlocked;
    }
}
