using Unity.Entities;

/// <summary>
/// 역할·목적: 분배기의 기준 입력 벨트·전방 방향·출력 순환 커서를 유지하는 영속 상태다.
/// 부착 엔티티: BuildingType=Splitter인 완공 건물이다.
/// 생성: BuildingLifecycleUtility가 InputBelt=Null·배치 방향·커서 0으로 붙인다.
/// 이용: SplitterDecisionSystem(Decision)이 후보를 계산하며 RoutingApplySystem(StateApply)이 승인된 실제 인계 후 기준과 OutputCursor를 갱신한다.
/// 제거: 전달 후 상태를 유지하며 건물 삭제 시 함께 제거한다. OutputCursor는 forward→right→left 순환 기준이다.
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
/// 역할·목적: 합류기의 기준 출력 벨트·전방 방향·입력 순환 커서를 유지하는 영속 상태다.
/// 부착 엔티티: BuildingType=Merger인 완공 건물이다.
/// 생성: BuildingLifecycleUtility가 OutputBelt=Null·배치 방향·커서 0으로 붙인다.
/// 이용: MergerDecisionSystem(Decision)이 후보를 계산하며 RoutingApplySystem(StateApply)이 승인된 실제 인계 후 기준과 InputCursor를 갱신한다.
/// 제거: 전달 후 상태를 유지하며 건물 삭제 시 함께 제거한다. InputCursor는 back→left→right 순환 기준이다.
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
