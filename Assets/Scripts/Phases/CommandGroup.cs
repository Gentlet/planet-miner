using Unity.Entities;

/// <summary>
/// Phase 1: 외부 입력 및 명령 변환 단계.
/// 외부 요청(플레이어 입력, 건설/철거 명령 등)을 ECS 상태로 변환합니다.
/// 이 단계에서는 복잡한 게임 상태 변경을 직접 수행하지 않습니다.
/// </summary>
[UpdateInGroup(typeof(GameSimulationGroup))]
public partial class CommandGroup : ComponentSystemGroup
{
}
