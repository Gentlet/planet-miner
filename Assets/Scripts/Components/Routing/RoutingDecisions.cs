using Unity.Entities;

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
