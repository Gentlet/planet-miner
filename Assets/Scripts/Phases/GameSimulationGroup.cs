using Unity.Entities;

/// <summary>
/// 역할·목적: Unity SimulationSystemGroup 안에서 게임의 여섯 phase를 순서대로 실행하는 최상위 그룹.
/// 입력·생성자: PrefabDatabaseInitializationSystem의 Ready와 초기화/실행 경계의 SimulationFatalError.
/// 실행 조건: Ready가 있고 중단 오류가 없어야 Command→Decision→Reservation→Execution→StateApply→Synchronization을 실행한다.
/// 출력·정리: 그룹 자체가 도메인 상태/요청을 쓰거나 지우지는 않는다. 각 소유 시스템과 두 ECB 재생 경계를 유지한다.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial class GameSimulationGroup : ComponentSystemGroup
{
    private EntityQuery _readyQuery;
    private EntityQuery _fatalErrorQuery;

    protected override void OnCreate()
    {
        base.OnCreate();
        _readyQuery = GetEntityQuery(ComponentType.ReadOnly<PrefabDatabaseReady>());
        _fatalErrorQuery = GetEntityQuery(ComponentType.ReadOnly<SimulationFatalError>());
    }

    protected override void OnUpdate()
    {
        // 준비 전 또는 중단 오류 후에는 일부 phase만 진행하지 않고 게임 그룹 전체를 건너뛴다.
        if (_readyQuery.IsEmptyIgnoreFilter)
        {
            return;
        }

        if (!_fatalErrorQuery.IsEmptyIgnoreFilter)
        {
            return;
        }

        base.OnUpdate();
    }
}
