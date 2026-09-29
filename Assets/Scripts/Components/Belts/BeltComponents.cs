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
