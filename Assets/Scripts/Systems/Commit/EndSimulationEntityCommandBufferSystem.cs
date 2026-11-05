using Unity.Entities;

/// <summary>
/// 드론 작업·경로·배정·행동 결과 및 종료 정리의 ECB를 최종 재생한다.
/// Producer Job을 완료한 뒤 공개하므로 새 작업·배정은 다음 틱 판단/행동부터 사용한다.
/// 새 실물/연결 자식의 장면 소유권도 같은 경계에서 해제한다. 이 재생 뒤 Synchronization이 최종 월드를 읽는다.
/// </summary>
[UpdateInGroup(typeof(SimulationCommitGroup), OrderLast = true)]
public partial class EndSimulationEntityCommandBufferSystem : EntityCommandBufferSystem
{
    private EntityQuery _pendingPrefabInstances;

    protected override void OnCreate()
    {
        base.OnCreate();
        _pendingPrefabInstances = GetEntityQuery(new EntityQueryDesc
        {
            All = new[] { ComponentType.ReadOnly<PendingPrefabSceneDetach>() },
            Options = EntityQueryOptions.IncludeDisabledEntities
        });
    }

    protected override void OnUpdate()
    {
        base.OnUpdate();
        RuntimePrefabLifecycleUtility.DetachSceneOwnership(EntityManager, _pendingPrefabInstances);
    }
}

