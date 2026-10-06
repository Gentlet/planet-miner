using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: 채굴기의 하부 자원·출력 여유를 검사하여 이번 틱 채굴 자격과 대상 자원을 전달한다.
/// 부착 엔티티: MinerState와 ProductItemElement/ProductResult를 가진 채굴기 건물이다.
/// 생성: BuildingLifecycleUtility가 비활성으로 준비하고 MinerDecisionSystem(Decision)이 조건에 따라 값과 enable 상태를 설정한다.
/// 이용: MinerExecutionSystem(Execution)이 활성 결정을 읽어 채굴 진행·자원 차감·ProductResult를 기록한다. 출고는 별도 BuildingItemOutputDecision 경로다.
/// 제거: 매 틱 Decision이 다시 작성/비활성화하며 실행 후 컴포넌트를 삭제하지 않는다. 건물 삭제 시 함께 제거한다.
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
