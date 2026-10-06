using Unity.Entities;

/// <summary>
/// 역할·목적: StateApply 마지막에서 실행/반영 단계의 구조 변경을 한 재생 경계로 확정한다.
/// 입력·생성자: Execution의 고갈 자원/드론 작업·경로 기록과 StateApply의 실물/건물 수명주기·배정/결과·렌더 ECB.
/// 출력·이용: 등록된 Producer Job 완료 후 엔티티 생성/삭제·버퍼/태그 변경을 재생하여 Synchronization이 최종 실체를 읽는다.
/// 정리·가시화: 소비한 ECB를 해제한다. 새 드론 작업/행동 결과는 재생 뒤 이용 가능하며 다음 Decision에서 새 작업을 판단한다.
/// </summary>
[UpdateInGroup(typeof(StateApplyGroup), OrderLast = true)]
public partial class EndStateApplyEntityCommandBufferSystem : EntityCommandBufferSystem
{
}
