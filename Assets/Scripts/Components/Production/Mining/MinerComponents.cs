using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 채굴기(Miner)의 물리적/시간적 진행 상태 컴포넌트 (Unmanaged / Blittable).
/// 
/// [책임]
/// - 채굴기의 고유 채굴 속도(MiningSpeed)와 현재 프레임까지 누적된 진행도(Progress, 0.0f ~ 1.0f)를 소유.
/// - 단일 원본(Source of Truth)으로 관리되며, Phase 4 Execution 단계에서 안전하게 누적 갱신.
/// </summary>
public struct MinerState : IComponentData
{
    public float MiningSpeed; // 초당 채굴 진행도 누적 계수 (예: 1.0f -> 1초에 1회 채굴 완료)
    public float Progress;    // 현재 채굴 누적 진행도 (0.0f ~ 1.0f)

    public MinerState(float miningSpeed, float progress = 0.0f)
    {
        MiningSpeed = miningSpeed;
        Progress = progress;
    }
}
