using Unity.Entities;

/// <summary>도메인별 인스턴스의 Fatal 범위를 확인하고 이번 틱의 하위 시스템 실행을 결정한다.</summary>
public abstract partial class FatalGuardedSimulationGroup : ComponentSystemGroup
{
    private EntityQuery _fatalErrorQuery;

    protected override void OnCreate()
    {
        base.OnCreate();
        _fatalErrorQuery = GetEntityQuery(ComponentType.ReadOnly<SimulationFatalError>());
    }

    protected override void OnUpdate()
    {
        // RemainingDomains만 이번 틱을 중단한다. EndBuilding의 다음 틱 차단은 같은 계약을 따른다.
        if (SimulationFailureUtility.ShouldAbortCurrentTick(_fatalErrorQuery))
        {
            return;
        }

        base.OnUpdate();
    }
}
