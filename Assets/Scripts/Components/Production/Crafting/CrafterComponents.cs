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
    WaitingForByproductOutput // 이전 레시피 잔여물/부산품 배출 대기 중 (입고 및 제작 차단)
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
