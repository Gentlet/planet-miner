using Unity.Entities;

/// <summary>
/// 건물 결과 확정 뒤 드론 판단·예약·실행·반영을 한 번 처리한다.
/// 같은 드론 단계의 새 수집품·새 공간으로 계획을 확대하지 않는다. 새 작업·배정·결과는 최종 ECB에서 공개한다.
/// </summary>
[UpdateInGroup(typeof(GameSimulationGroup))]
[UpdateAfter(typeof(BuildingSimulationGroup))]
public partial class DroneSimulationGroup : ComponentSystemGroup
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

