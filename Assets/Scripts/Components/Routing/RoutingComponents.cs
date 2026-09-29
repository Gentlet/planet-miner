using Unity.Entities;

/// <summary>
/// Splitter의 영속 라우팅 상태.
/// - InputBelt: 현재 기준 입력 벨트.
/// - ForwardDirection: 기준 입력이 가리키는 전방 방향.
/// - OutputCursor: forward -> right -> left 순환 인덱스.
/// - StateApply에서만 갱신하고 Decision에서는 읽기 전용으로 사용.
/// </summary>
public struct SplitterRoutingState : IComponentData
{
    public Entity InputBelt;
    public DirectionEnum ForwardDirection;
    public byte OutputCursor;

    public SplitterRoutingState(
        Entity inputBelt,
        DirectionEnum forwardDirection,
        byte outputCursor = 0)
    {
        InputBelt = inputBelt;
        ForwardDirection = forwardDirection;
        OutputCursor = outputCursor;
    }
}

/// <summary>
/// Merger의 영속 라우팅 상태.
/// - OutputBelt: 현재 기준 출력 벨트.
/// - ForwardDirection: 기준 출력 벨트가 가리키는 전방 방향.
/// - InputCursor: back -> left -> right 순환 인덱스.
/// - StateApply에서만 갱신하고 Decision에서는 읽기 전용으로 사용.
/// </summary>
public struct MergerRoutingState : IComponentData
{
    public Entity OutputBelt;
    public DirectionEnum ForwardDirection;
    public byte InputCursor;

    public MergerRoutingState(
        Entity outputBelt,
        DirectionEnum forwardDirection,
        byte inputCursor = 0)
    {
        OutputBelt = outputBelt;
        ForwardDirection = forwardDirection;
        InputCursor = inputCursor;
    }
}
