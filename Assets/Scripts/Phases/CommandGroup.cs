using Unity.Entities;

/// <summary>
/// Phase 1: 외부 입력 및 명령 변환 단계.
/// 외부 요청(플레이어 입력, 건설/철거 명령 등)을 ECS 상태로 변환.
/// 복잡한 게임 상태 변경은 후속 Phase에서 처리.
/// </summary>
[UpdateInGroup(typeof(GameSimulationGroup))]
public partial class CommandGroup : ComponentSystemGroup
{
}
