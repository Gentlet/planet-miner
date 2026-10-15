using Unity.Entities;

/// <summary>
/// 건물·벨트·생산·아이템 수명주기를 한 번 실행하고 EndBuilding에서 결과를 확정한다.
/// 드론은 확정된 ECS 상태를 읽는다. 공사 완공도 이 그룹에서 판단하며 이번 틱 드론 결과는 다음 틱에 읽는다.
/// </summary>
[UpdateInGroup(typeof(GameSimulationGroup))]
[UpdateAfter(typeof(PowerSimulationGroup))]
public partial class BuildingSimulationGroup : ComponentSystemGroup
{
    private EntityQuery _fatalErrorQuery;

    protected override void OnCreate()
    {
        base.OnCreate();
        _fatalErrorQuery = GetEntityQuery(ComponentType.ReadOnly<SimulationFatalError>());
    }

    protected override void OnUpdate()
    {
        // 공통 Fatal의 중단 범위를 따른다. EndBuilding Fatal의 기존 다음 틱 차단은 유지한다.
        if (SimulationFailureUtility.ShouldAbortCurrentTick(_fatalErrorQuery))
        {
            return;
        }
        base.OnUpdate();
    }
}

