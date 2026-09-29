using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 채굴기(Miner)의 채굴 의사결정 컴포넌트 (Enableable).
/// 
/// [책임]
/// - Phase 2 Decision 단계에서 채굴 조건(하부 자원 실존, 내부 버퍼 공간 여유)을 판정하여 상태를 설정.
/// - 채굴 조건이 만족되면 CanMine = true로 활성화되며, 대상 자원 엔티티를 기록.
/// - 외부 벨트로의 배출은 채굴기에 부착된 BuildingItemOutputDecision 및 출고 시스템이 전담.
/// </summary>
public struct MinerDecision : IComponentData, IEnableableComponent
{
    public bool CanMine;          // 이번 프레임에 채굴 진행 가능 여부
    public Entity TargetResource; // 채굴 대상 자원 엔티티

    public MinerDecision(bool canMine, Entity targetResource)
    {
        CanMine = canMine;
        TargetResource = targetResource;
    }
}
