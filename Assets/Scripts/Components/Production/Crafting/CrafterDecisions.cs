using Unity.Entities;

/// <summary>
/// 역할·목적: 제작 착수·진행·생산 결과 기록 자격과 레시피 참조를 전달하는 이번 틱 실행 판단이다.
/// 부착 엔티티: CrafterState를 가진 완공 제작기 건물이다.
/// 생성: BuildingLifecycleUtility가 비활성으로 준비하고 CrafterDecisionSystem(Decision)이 현재 재료·출력 조건으로 값과 enable 상태를 작성한다.
/// 이용: CrafterExecutionSystem(Execution)이 활성 결정을 읽어 재료 선소비·진행·ProductResult를 기록한다. 표시 상태 전이는 CrafterStateDecision과 분리한다.
/// 제거: 다음 Decision이 값을 재작성/비활성화한다. Execution이 읽었다고 컴포넌트를 삭제하지 않으며 건물 삭제 시 함께 제거한다.
/// </summary>
public struct CrafterDecision : IComponentData, IEnableableComponent
{
    public bool CanCraft;           // 제작 활성화 여부 (CanStartCraft || CanAdvance)
    public bool CanStartCraft;      // 재료 완비되어 이번 프레임에 재료 선소비 및 제작 착수 가능
    public bool CanAdvance;         // 진행도 누적 가능
    public bool CanProduceOutput;   // 1.0f 완료 후 출력 버퍼에 여유가 있어 배출 가능
    public int RecipeId;
    public int RecipeIndex;         // 전역 RecipeConfigElement 버퍼 내의 인덱스

    public CrafterDecision(bool canCraft, int recipeId, int recipeIndex = -1)
    {
        CanCraft = canCraft;
        CanStartCraft = canCraft;
        CanAdvance = canCraft;
        CanProduceOutput = false;
        RecipeId = recipeId;
        RecipeIndex = recipeIndex;
    }
}

/// <summary>
/// 역할·목적: 제작기의 영속 Status 전이 판단을 실제 상태 반영과 분리하여 전달한다.
/// 부착 엔티티: CrafterState가 있는 제작기 건물이다.
/// 생성: BuildingLifecycleUtility가 비활성으로 준비하고 CrafterDecisionSystem(Decision)이 NextStatus를 작성·활성화한다.
/// 이용: CrafterStateApplySystem(StateApply)이 NextStatus를 CrafterState.Status에 반영한다.
/// 제거: StateApply가 소비 후 즉시 비활성화하며 컴포넌트는 건물 삭제까지 유지한다.
/// </summary>
public struct CrafterStateDecision : IComponentData, IEnableableComponent
{
    public CrafterStatusEnum NextStatus;

    public CrafterStateDecision(CrafterStatusEnum nextStatus)
    {
        NextStatus = nextStatus;
    }
}
