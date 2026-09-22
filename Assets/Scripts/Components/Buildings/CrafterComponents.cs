using Unity.Entities;

/// <summary>
/// 제작기(Crafter) 가동 상태 열거형.
/// </summary>
public enum CrafterStatusEnum : byte
{
    NoRecipe,               // 선택된 레시피 없음
    Idle,                   // 대기 중 (재료 준비 완료 또는 시작 대기)
    Crafting,               // 제작 진행 중 (Progress 누적 중)
    WaitingForInput,        // 재료 부족으로 대기
    WaitingForOutput,       // 완성품/부산품 출력 슬롯 만석으로 정체 (Backpressure)
    WaitingForPurgeOutput   // 이전 레시피 잔여물/생산물 배출 대기 중 (입고 및 제작 차단)
}

/// <summary>
/// 제작기(Crafter)의 레시피 변경을 요청하는 1회성 명령 엔티티 컴포넌트.
/// [수명주기]: CommandGroup의 CrafterRecipeCommandSystem에서 처리 후 파괴 (Consume-on-Apply).
/// </summary>
public struct ChangeCrafterRecipeRequest : IRequestComponent
{
    public Entity TargetCrafter;
    public int NewRecipeId;

    public ChangeCrafterRecipeRequest(Entity targetCrafter, int newRecipeId)
    {
        TargetCrafter = targetCrafter;
        NewRecipeId = newRecipeId;
    }
}

/// <summary>
/// 제작기 상태 컴포넌트 (State Component).
/// </summary>
public struct CrafterState : IComponentData
{
    public int SelectedRecipeId; // 현재 지정된 레시피 ID
    public int ActiveRecipeId;   // 현재 동기화되어 가동 중인 레시피 ID (변경 감지용)
    public float Progress;       // 0.0f ~ 1.0f
    public float Speed;          // 제작 기본 속도 배율 (기본 1.0f)
    public CrafterStatusEnum Status;
    public bool IsCraftingActive; // 재료를 선소비하고 제작이 진행 중인 상태인지 여부

    public CrafterState(int selectedRecipeId, float speed = 1.0f)
    {
        SelectedRecipeId = selectedRecipeId;
        ActiveRecipeId = selectedRecipeId;
        Progress = 0.0f;
        Speed = speed;
        Status = selectedRecipeId > 0 ? CrafterStatusEnum.Idle : CrafterStatusEnum.NoRecipe;
        IsCraftingActive = false;
    }
}

/// <summary>
/// 제작기 의사결정 컴포넌트 (Decision Component, Phase 2 산출물).
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
