using Unity.Entities;

/// <summary>
/// 역할·목적: Command 마지막에서 명령의 구조 변경을 한 재생 경계로 확정한다.
/// 입력·생성자: 배치/취소/철거 승인/청크/자원 생성/레시피/연구 선택 요청 처리의 Command ECB와 등록된 Producer Job 핸들.
/// 출력·이용: 기록된 엔티티/상태/결과와 삭제를 재생하여 같은 틱 Preparation부터 공개 원본을 읽게 한다.
/// 정리·가시화: Producer 완료와 재생 뒤 새 실물/연결 자식의 장면 소유권을 해제한다. 공간 인덱스 갱신은 Synchronization까지 지연된다.
/// </summary>
[UpdateInGroup(typeof(CommandGroup), OrderLast = true)]
public partial class EndCommandEntityCommandBufferSystem : EntityCommandBufferSystem
{
    private EntityQuery _pendingPrefabInstances;

    protected override void OnCreate()
    {
        base.OnCreate();
        _pendingPrefabInstances = RuntimePrefabLifecycleUtility.CreatePendingSceneDetachQuery(this);
    }

    protected override void OnUpdate()
    {
        base.OnUpdate();
        RuntimePrefabLifecycleUtility.DetachSceneOwnership(EntityManager, _pendingPrefabInstances);
    }
}
