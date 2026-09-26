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

/// <summary>
/// Splitter/Merger가 이번 프레임 전달 후보로 선택한 아이템과 경로.
/// - DecisionGroup에서 후보가 있을 때 활성화.
/// - ReservationGroup은 공유 목적지 승인 여부를 별도 데이터로 결정.
/// - ExecutionGroup은 승인된 후보만 실제 이동.
/// - 고빈도 Frame Decision이며 IRequestComponent가 아님.
/// - 실제 배출 포트 인덱스는 TargetBelt의 위치와 ForwardDirection으로 실시간 역산.
/// </summary>
public struct RoutingTransferDecision : IComponentData, IEnableableComponent
{
    public Entity Item;
    public Entity SourceBelt;
    public Entity TargetBelt;

    public RoutingTransferDecision(
        Entity item,
        Entity sourceBelt,
        Entity targetBelt)
    {
        Item = item;
        SourceBelt = sourceBelt;
        TargetBelt = targetBelt;
    }
}

