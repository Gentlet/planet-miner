using Unity.Entities;

/// <summary>
/// Phase 5: 중요 상태 변경 반영 단계.
/// 아이템 소유권(Ownership), 태스크 상태 전이, 예약 상태 변경, 엔티티 생성/삭제 요청 등
/// 여러 도메인의 핵심 정합성을 유지하며 실제 ECS 상태에 적용합니다.
/// </summary>
[UpdateInGroup(typeof(GameSimulationGroup))]
[UpdateAfter(typeof(ExecutionGroup))]
public partial class StateApplyGroup : ComponentSystemGroup
{
}
