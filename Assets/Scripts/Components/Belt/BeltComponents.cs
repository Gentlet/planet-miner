using Unity.Entities;

/// <summary>
/// [부착 대상: 벨트 건물 엔티티 (Belt Building Entity)]
/// 벨트 건물 엔티티의 고유 속성 컴포넌트.
/// 벨트 엔티티는 [GridPosition, Direction, BeltComponent] 조합으로 구성.
/// </summary>
public struct BeltComponent : IComponentData
{
    /// <summary>
    /// 벨트의 기본 이동 속도 (초당 타일 진행 거리, 기본값: 2.0f tiles/sec).
    /// </summary>
    public float Speed;

    public BeltComponent(float speed = 2.0f)
    {
        Speed = speed;
    }
}

/// <summary>
/// [부착 대상: 아이템 엔티티 (Item Entity)]
/// 벨트 위에서 이동 중인 아이템의 위치 진행 상태(State)를 나타내는 Persistent Enableable 컴포넌트.
/// - 벨트 위에 위치할 때만 활성화(Enabled)되며, 바닥에 떨어지거나 창고에 수납되면 비활성화.
/// - DecisionGroup에서는 오직 읽기([ReadOnly])만 수행되며, ExecutionGroup에서만 실제 Progress가 전진(Write).
/// </summary>
public struct BeltMovementState : IComponentData, IEnableableComponent
{
    /// <summary>
    /// 현재 벨트 타일 내 진행률 (0.0f = 타일 입구, 1.0f = 타일 출구/다음 벨트 인계 지점).
    /// </summary>
    public float Progress;

    public BeltMovementState(float progress = 0.0f)
    {
        Progress = progress;
    }
}

/// <summary>
/// [부착 대상: 아이템 엔티티 (Item Entity)]
/// 벨트 위 아이템의 프레임 단위 이동 의사결정 산출물(Decision)을 저장하는 Persistent Enableable 컴포넌트.
/// - DecisionGroup(BeltMovementDecisionSystem)에서 계산하여 기록(Write)하고,
///   ExecutionGroup(BeltMovementExecutionSystem)에서 실제 이동 반영 후 소비.
/// - BeltMovementState와 Decision 데이터를 분리하여 동일 Job 내 상태/결정 책임 분리.
/// </summary>
public struct BeltMovementDecision : IComponentData, IEnableableComponent
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

