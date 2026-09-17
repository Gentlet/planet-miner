using Unity.Entities;

/// <summary>
/// StateApplyGroup의 맨 마지막(OrderLast = true)에 실행되는 EntityCommandBufferSystem.
/// 
/// [책임]
/// - StateApplyGroup 내의 여러 상태 반영 시스템(ItemLifecycleApplySystem 등)에서 요청된
///   모든 구조적 변경(Entity 생성/파괴 등)을 모아 한 번에 Playback합니다.
/// - 여러 시스템이 각자 Playback을 호출하여 발생하던 다중 Sync Point를 1회로 압축합니다.
/// - SynchronizationGroup(Phase 6) 진입 전에 모든 엔티티 상태가 월드에 완전히 반영되도록 보장합니다.
/// </summary>
[UpdateInGroup(typeof(StateApplyGroup), OrderLast = true)]
public partial class EndStateApplyEntityCommandBufferSystem : EntityCommandBufferSystem
{
}
