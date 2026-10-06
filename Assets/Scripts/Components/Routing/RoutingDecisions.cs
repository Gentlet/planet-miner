using Unity.Entities;

/// <summary>
/// 역할·목적: 분배기/합류기가 이번 틱 전달할 실물과 출발·목적 벨트를 전달하는 enableable 후보다. 일회성 Request가 아니다.
/// 부착 엔티티: SplitterRoutingState 또는 MergerRoutingState를 가진 라우팅 건물이다.
/// 생성: BuildingLifecycleUtility가 비활성으로 준비하고 SplitterDecisionSystem/MergerDecisionSystem(Decision)이 전달 후보를 작성·활성화한다.
/// 이용: BeltDestinationReservationSystem(Reservation)이 목적지 경합을 중재하고 RoutingApplySystem(StateApply)이 승인된 실물의 격자·진행도·시각 위치·순환 커서를 반영한다.
/// 제거: RoutingApply가 소비 후 비활성화하며 컴포넌트는 건물 삭제까지 유지한다. 실제 반영은 Execution이 아닌 StateApply다.
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
