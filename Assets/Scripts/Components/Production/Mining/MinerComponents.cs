using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: 채굴 속도와 미처리 누적 채굴 작업량의 영속 원본이다.
/// 부착 엔티티: BuildingType=Miner인 완공 채굴기 건물이다.
/// 생성: BuildingLifecycleUtility가 설정 속도와 초기 Progress=0으로 붙인다.
/// 이용: MinerDecisionSystem(Decision)이 채굴 자격을 판단하고 MinerExecutionSystem(Execution)이 실행 가능한 틱의 작업량을 누적하여 최대 한 번 1을 차감한다.
/// 제거: 채굴 완료는 누적량 일부 소비이며 컴포넌트는 유지한다. 건물 철거/삭제 시 함께 제거한다.
/// 출력·자원 조건으로 실행이 막히면 누적량을 보존하고 초과 Progress는 1 이상 남을 수 있다.
/// </summary>
public struct MinerState : IComponentData
{
    public float MiningSpeed; // 초당 채굴 진행도 누적 계수 (예: 1.0f -> 1초에 1회 채굴 완료)
    public float Progress;    // 미처리 누적 작업량. 출력/자원 조건으로 실행이 막히면 기존 값을 유지한다.

    public MinerState(float miningSpeed, float progress = 0.0f)
    {
        MiningSpeed = miningSpeed;
        Progress = progress;
    }
}
