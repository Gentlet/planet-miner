using Unity.Entities;

/// <summary>
/// CommandGroup(Phase 1)의 맨 마지막(OrderLast = true)에 실행되는 EntityCommandBufferSystem.
/// 
/// [책임]
/// - CommandGroup 내의 명령 처리 시스템(BuildingPlacementCommandSystem, ResourceGenerationCommandSystem, CrafterRecipeCommandSystem 등)에서
///   발행된 구조적 변경(공사 현장 생성, 자원 노드 생성, 요청 엔티티 삭제 등)을 CommandGroup 종료 시점에 즉시 Playback.
/// - Phase 1에서 생성된 엔티티가 동일 프레임의 Phase 2(Decision) ~ Phase 4(Execution)에서 즉시 물리적 실체로 상호작용할 수 있도록 보장.
/// - Phase 1 시스템이 Phase 5 끝의 EndStateApplyEntityCommandBufferSystem을 역참조하던 모순을 해소하여 Phase 간 독립성을 확립.
/// </summary>
[UpdateInGroup(typeof(CommandGroup), OrderLast = true)]
public partial class EndCommandEntityCommandBufferSystem : EntityCommandBufferSystem
{
}
