using Unity.Entities;

/// <summary>
/// 역할·목적: Command 마지막에서 명령의 구조 변경을 한 재생 경계로 확정한다.
/// 입력·생성자: 배치·취소·철거 승인·청크/자원 생성·레시피 요청 소비의 Command ECB와 등록된 Producer Job 핸들.
/// 출력·이용: 기록된 엔티티/상태 생성과 삭제를 재생하여 같은 틱 Decision부터 실체를 조회할 수 있게 한다.
/// 정리·가시화: Producer 완료를 기다린 뒤 버퍼를 재생/해제한다. 공간 인덱스 자체의 갱신은 Synchronization까지 지연된다.
/// </summary>
[UpdateInGroup(typeof(CommandGroup), OrderLast = true)]
public partial class EndCommandEntityCommandBufferSystem : EntityCommandBufferSystem
{
}
