using Unity.Entities;

/// <summary>
/// 역할·목적: Unity SimulationSystemGroup 안에서 Command→Building→Drone→Commit→Synchronization을 실행한다.
/// 입력·생성자: PrefabDatabaseInitializationSystem의 Ready와 초기화/실행 경계의 SimulationFatalError.
/// 실행 조건: 틱 시작에 Ready가 있고 중단 오류가 없어야 전체 그룹을 한 번 실행한다.
/// 출력·정리: Command/Building/Simulation 종료 ECB를 재생한다. 중간 오류도 현재 틱을 마치고 다음 틱부터 차단한다.
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
