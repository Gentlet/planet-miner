using Unity.Entities;

/// <summary>
/// [부착 대상: 벨트 건물 엔티티 (Belt Building Entity)]
/// 벨트 건물 엔티티의 고유 속성 컴포넌트.
/// 벨트 엔티티는 [GridPosition, Direction, BeltComponent] 조합으로 구성됩니다.
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
/// 벨트 위에서 이동 중인 아이템의 이동 상태를 나타내는 Persistent Enableable 컴포넌트.
/// - 벨트 위에 위치할 때만 활성화(Enabled)되며, 바닥에 떨어지거나 창고에 수납되면 비활성화됩니다.
/// - 대량 아이템 이동 시 1회성 Request Entity 폭증을 방지하기 위해 이 컴포넌트의 값을 갱신하여 통신합니다.
/// </summary>
public struct BeltMovementState : IComponentData, IEnableableComponent
{
    /// <summary>
    /// 현재 벨트 타일 내 진행률 (0.0f = 타일 입구, 1.0f = 타일 출구/다음 벨트 인계 지점).
    /// </summary>
    public float Progress;

    /// <summary>
    /// 이번 프레임에 전진할 확정 거리.
    /// DecisionGroup(BeltMovementDecisionSystem)에서 계산하여 기록하고,
    /// ExecutionGroup(BeltMovementExecutionSystem)에서 실제 Progress를 전진시킨 후 소비됩니다.
    /// </summary>
    public float PlannedMovement;

    /// <summary>
    /// 앞선 아이템과의 간격 부족이나 벨트 끝 정체(Backpressure)로 인해 전진하지 못한 상태인지 여부.
    /// </summary>
    public bool IsBlocked;

    public BeltMovementState(float progress = 0.0f)
    {
        Progress = progress;
        PlannedMovement = 0.0f;
        IsBlocked = false;
    }
}
