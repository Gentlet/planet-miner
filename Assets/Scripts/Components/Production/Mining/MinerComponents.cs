using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 채굴기(Miner)의 물리적/시간적 진행 상태 컴포넌트 (Unmanaged / Blittable).
/// 
/// [책임]
/// - 채굴기의 고유 채굴 속도(MiningSpeed)와 미처리 채굴 작업량(Progress)을 소유.
/// - 실행 가능한 틱마다 작업량을 누적하고 최대 한 번 1을 차감한다. 초과분은 1 이상 남을 수 있다.
/// - 단일 원본(Source of Truth)으로 관리되며, Phase 4 Execution 단계에서 안전하게 누적 갱신.
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
