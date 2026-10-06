using Unity.Entities;

/// <summary>
/// 역할·목적: 레시피 없음·재료 대기·제작 중·출력 대기 등 제작기의 표시/작동 상태를 구분한다.
/// 부착 엔티티: 열거형 자체는 부착하지 않고 CrafterState.Status 및 CrafterStateDecision.NextStatus에 포함한다.
/// 생성·이용: CrafterRecipeCommandSystem(Command)과 CrafterDecisionSystem(Decision)이 전이를 정하고 CrafterStateApplySystem(StateApply)이 결정된 상태를 반영한다.
/// 제거: 상태는 덮어쓰며 제작기 엔티티 또는 판단 컴포넌트의 수명을 따른다.
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
/// 역할·목적: 선택/적용 레시피·실제 진행도·속도·제작 착수 여부·현재 상태를 소유한다.
/// 부착 엔티티: BuildingType=Crafter인 완공 제작기 건물이다.
/// 생성: BuildingLifecycleUtility가 레시피 없음·진행도 0·설정 속도로 붙인다.
/// 이용: CrafterRecipeCommandSystem(Command)이 레시피 변경/잔여 입력을 정리하고 CrafterDecisionSystem(Decision)이 실행/상태 전이를 판단한다.
/// CrafterExecutionSystem(Execution)은 재료 선소비·진행·생산 완료를, CrafterStateApplySystem(StateApply)은 NextStatus 반영을 담당한다.
/// 제거: 레시피 변경·완료로 관련 값을 초기화하지만 컴포넌트는 건물 삭제까지 유지한다. IsCraftingActive는 재료를 이미 선소비한 작업이 진행 중임을 뜻한다.
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
