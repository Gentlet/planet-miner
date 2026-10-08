using Unity.Collections;
using Unity.Entities;

/// <summary>
/// 역할·목적: 시작 시 프리팹 DB 검증이 완료되어 GameSimulationGroup 실행을 허용하는 World 상태다.
/// 부착 엔티티: 프리팹 DB 엔티티들과 별도인 World 준비 태그 엔티티다.
/// 생성: PrefabDatabaseInitializationSystem(Initialization)이 SubScene 로딩·DB 유일성·타입/필수 프리팹 검증 성공 후 즉시 생성한다.
/// 이용: GameSimulationGroup은 이 태그, 각 필수 설정의 게시 엔티티/버퍼와 Fatal 부재를 확인한다. 이 태그는 DB 준비만 뜻한다.
/// 제거: 틱마다 소비하지 않으며 World 종료까지 유지한다. 초기화 실패 시 태그를 게시하지 않는다.
/// </summary>
public struct PrefabDatabaseReady : IComponentData
{
}

/// <summary>
/// 역할·목적: 복구 없는 시작/런타임 구성 오류를 진단하고 다음 GameSimulationGroup 실행을 중단한다.
/// 부착 엔티티: 오류 메시지를 가진 별도 World 오류 엔티티다. 이미 생성한 실물이나 실패 요청을 대신 소유하지 않는다.
/// 생성: 설정/프리팹 Init은 SimulationFailureUtility로 즉시 게시한다. 실행 중 설정/DB 누락은 호출자의 ECB에 기록한다.
/// 이용: GameSimulationGroup이 존재 여부로 다음 시뮬레이션을 차단한다. 메시지는 진단용이며 현재 틱 전체 rollback을 뜻하지 않는다.
/// 제거: 자동 소비·재시도·복구 경로는 없다. World 재초기화/종료의 수명을 따른다.
/// </summary>
public struct SimulationFatalError : IComponentData
{
    public FixedString128Bytes Message;
}
