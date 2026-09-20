using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 채굴기(Miner)의 물리적/시간적 진행 상태 컴포넌트 (Unmanaged / Blittable).
/// 
/// [책임]
/// - 채굴기의 고유 채굴 속도(MiningSpeed)와 현재 프레임까지 누적된 진행도(Progress, 0.0f ~ 1.0f)를 소유합니다.
/// - 단일 원본(Source of Truth)으로 관리되며, Phase 4 Execution 단계에서 안전하게 누적 갱신됩니다.
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

/// <summary>
/// 채굴기(Miner)의 채굴 및 배출 의사결정 컴포넌트 (Enableable).
/// 
/// [책임]
/// - Phase 2 Decision 단계에서 채굴 조건(하부 자원 실존, 출력 대상 벨트 준비 등)을 판정하여 상태를 설정합니다.
/// - 채굴 조건이 만족되면 CanMine = true로 활성화되며, 대상 자원과 배출 목표 좌표를 기록합니다.
/// - 상태와 의사결정을 물리적으로 분리하여 병렬 Job 데이터 레이스를 방지합니다.
/// </summary>
public struct MinerDecision : IComponentData, IEnableableComponent
{
    public bool CanMine;                 // 이번 프레임에 채굴 진행 가능 여부
    public Entity TargetResource;        // 채굴 대상 자원 엔티티
    public int2 TargetBeltPosition;      // 채굴 완료 시 광물이 배출될 목표 벨트 타일 좌표

    public MinerDecision(bool canMine, Entity targetResource, int2 targetBeltPosition)
    {
        CanMine = canMine;
        TargetResource = targetResource;
        TargetBeltPosition = targetBeltPosition;
    }
}
