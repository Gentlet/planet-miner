using Unity.Entities;

/// <summary>
/// 제작기의 실행 의사결정 컴포넌트 (Decision Component, Phase 2 산출물).
/// CrafterExecutionSystem이 소비할 제작/진행/출력 실행 정보만 보관.
/// </summary>
public struct CrafterDecision : IComponentData, IEnableableComponent
{
    public bool CanCraft;           // 제작 활성화 여부 (CanStartCraft || CanAdvance)
    public bool CanStartCraft;      // 재료 완비되어 이번 프레임에 재료 선소비 및 제작 착수 가능
    public bool CanAdvance;         // 진행도 누적 가능
    public bool CanProduceOutput;   // 1.0f 완료 후 출력 버퍼에 여유가 있어 배출 가능
    public int RecipeId;
    public int RecipeIndex;         // 전역 RecipeRegistryBlob 내의 인덱스

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
/// 제작기의 Persistent State(Status) 전이 의사결정 컴포넌트.
/// DecisionGroup에서 활성화되고 StateApplyGroup의 CrafterStateApplySystem에서 소비 후 비활성화.
/// </summary>
public struct CrafterStateDecision : IComponentData, IEnableableComponent
{
    public CrafterStatusEnum NextStatus;

    public CrafterStateDecision(CrafterStatusEnum nextStatus)
    {
        NextStatus = nextStatus;
    }
}
