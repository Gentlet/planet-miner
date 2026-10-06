using Unity.Entities;

/// <summary>
/// 역할·목적: 첫 번째 phase로 외부 요청을 검증하고 사용자/Mono 입력을 게임 상태에 반영한다.
/// 입력·이용: 배치/취소/철거/레시피/청크 요청을 도메인 Command가 소비하며 이후 Decision은 확정된 상태를 읽는다.
/// 출력·가시화: 값 변경은 해당 Command가 소유하고 구조 변경은 EndCommand에서 재생한다.
/// 정리: 요청 소비/대기 정책은 각 Command가 결정한다. 실제 철거와 생산 실물 수명주기는 후속 StateApply가 맡는다.
/// </summary>
[UpdateInGroup(typeof(GameSimulationGroup))]
public partial class CommandGroup : ComponentSystemGroup
{
}
