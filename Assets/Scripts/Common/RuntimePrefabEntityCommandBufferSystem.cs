using Unity.Entities;

/// <summary>각 종료 시스템의 ECB를 재생한 직후 같은 경계에서 런타임 프리팹의 장면 소유권을 분리한다.</summary>
public abstract partial class RuntimePrefabEntityCommandBufferSystem : EntityCommandBufferSystem
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
