using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 그리드 4방향 정의 열거형.
/// </summary>
public enum DirectionEnum : int
{
    Up,
    Right,
    Down,
    Left,
    Count
}

/// <summary>
/// 엔티티의 그리드 상 2D 좌표 컴포넌트 (Unmanaged / Blittable).
/// 건물, 자원 노드, 월드 아이템 등의 기준 좌표로 사용.
/// </summary>
public struct GridPosition : IComponentData
{
    public int2 Value;

    public GridPosition(int2 position)
    {
        Value = position;
    }

    public GridPosition(int x, int y)
    {
        Value = new int2(x, y);
    }

    public static implicit operator int2(GridPosition pos) => pos.Value;
    public static implicit operator GridPosition(int2 pos) => new GridPosition(pos);
}

/// <summary>
/// 엔티티의 배치 방향 컴포넌트.
/// </summary>
public struct Direction : IComponentData
{
    public DirectionEnum dir;

    public Direction(DirectionEnum direction)
    {
        dir = direction;
    }
}
