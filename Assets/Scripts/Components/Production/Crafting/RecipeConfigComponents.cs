using Unity.Entities;

/// <summary>
/// 역할·목적: 레시피가 제작 한 번에 선소비할 품목·개별 실물 수량을 정의한다.
/// 부착 엔티티: RecipeRegistry가 있는 World 단일 설정 엔티티의 재료 버퍼다.
/// 생성: RecipeInitSystem(Initialization)이 RecipeConfigLoader로 로드/검증하여 한 번 게시한다.
/// 이용: CrafterRecipeCommandSystem(Command)이 전용 슬롯을 계산하고 CrafterDecisionSystem(Decision)이 재료 충족을 확인하며 CrafterExecutionSystem(Execution)이 제작 착수 시 해당 수량을 선소비한다.
/// 제거: 제작으로 설정 항목을 소비하지 않는다. 게시 후 World 수명 동안 읽기 전용이며 버퍼 해제는 World가 관리한다.
/// </summary>
[InternalBufferCapacity(0)]
public struct RecipeIngredientElement : IBufferElementData
{
    public ItemTypeEnum ItemType;
    public int Amount;

    public RecipeIngredientElement(ItemTypeEnum itemType, int amount)
    {
        ItemType = itemType;
        Amount = amount;
    }
}

/// <summary>
/// 역할·목적: 제작 완료 시 생성할 주생산품·부산물의 품목·실물 수량을 정의한다.
/// 부착 엔티티: RecipeRegistry가 있는 World 단일 설정 엔티티의 출력 버퍼다.
/// 생성: RecipeInitSystem(Initialization)이 RecipeConfigLoader로 레시피별 출력 연속 구간을 한 번 게시한다.
/// 이용: CrafterDecisionSystem(Decision)이 출력 여유를 검사하고 CrafterExecutionSystem(Execution)이 ProductResult를 기록한다. IsByproduct=false는 주생산품 식별이다.
/// 제거: 제작 완료로 설정을 소비하지 않는다. 게시 후 World 수명 동안 읽기 전용이며 버퍼 해제는 World가 관리한다.
/// </summary>
[InternalBufferCapacity(0)]
public struct RecipeOutputElement : IBufferElementData
{
    public ItemTypeEnum ItemType;
    public int Amount;
    public bool IsByproduct;

    public RecipeOutputElement(ItemTypeEnum itemType, int amount, bool isByproduct = false)
    {
        ItemType = itemType;
        Amount = amount;
        IsByproduct = isByproduct;
    }
}

/// <summary>
/// 역할·목적: 레시피 ID·제작 시간·조건 플래그와 같은 설정 엔티티의 재료/출력 연속 버퍼 범위를 정의한다.
/// 부착 엔티티: RecipeRegistry가 있는 World 단일 설정 엔티티의 레시피 버퍼다.
/// 생성: RecipeInitSystem(Initialization)이 RecipeConfigLoader로 검증된 범위 시작/개수를 한 번 게시한다.
/// 이용: CrafterRecipeCommandSystem(Command)이 레시피 선택/슬롯에 읽고 CrafterDecisionSystem(Decision)·CrafterExecutionSystem(Execution)이 재료/시간/출력 조건과 생산 결과에 사용한다.
/// 제거: 레시피 선택 변경으로 전역 설정을 수정하거나 소비하지 않는다. 게시 후 읽기 전용이며 설정 엔티티/World 수명을 따른다.
/// </summary>
[InternalBufferCapacity(0)]
public struct RecipeConfigElement : IBufferElementData
{
    public int Id;
    public float CraftTime;
    public byte ConditionFlags; // 현재 설정 로더는 0으로 게시하며 실행 조건을 해석하는 Consumer는 아직 없다.
    public int IngredientStart;
    public int IngredientCount;
    public int OutputStart;
    public int OutputCount;

    public bool TryGetPrimaryOutput(DynamicBuffer<RecipeOutputElement> outputs, out RecipeOutputElement primaryOutput)
    {
        for (int i = 0; i < OutputCount; i++)
        {
            var output = outputs[OutputStart + i];
            if (!output.IsByproduct)
            {
                primaryOutput = output;
                return true;
            }
        }

        if (OutputCount > 0)
        {
            primaryOutput = outputs[OutputStart];
            return true;
        }

        primaryOutput = default;
        return false;
    }

    /// <summary>품목이 유일하고 수량이 양수일 때만 요구량을 반환한다. 중복을 첫 행의 비용으로 취급하지 않는다.</summary>
    public bool TryFindIngredient(DynamicBuffer<RecipeIngredientElement> ingredients, ItemTypeEnum itemType, out int requiredAmount)
    {
        requiredAmount = 0;
        bool found = false;
        for (int i = 0; i < IngredientCount; i++)
        {
            var ingredient = ingredients[IngredientStart + i];
            if (ingredient.ItemType != itemType)
            {
                continue;
            }

            if (found || ingredient.Amount <= 0)
            {
                requiredAmount = 0;
                return false;
            }

            requiredAmount = ingredient.Amount;
            found = true;
        }

        return found;
    }
}

/// <summary>
/// 역할·목적: 레시피·재료·출력 설정 버퍼의 World 단일 소유자를 식별한다.
/// 부착 엔티티: RecipeConfigElement/RecipeIngredientElement/RecipeOutputElement를 함께 가진 별도 설정 엔티티다.
/// 생성: RecipeInitSystem(Initialization)이 한 번 게시한다. 사전 등록도 InitializeRecipeRegistry 경계로 수행하며 기존 설정 재게시를 거부한다.
/// 이용: CrafterRecipeCommandSystem(Command), CrafterDecisionSystem(Decision), CrafterExecutionSystem(Execution)이 버퍼와 함께 읽는다.
/// 제거: 초기화 시스템 종료나 제작/레시피 변경으로 소비하지 않는다. 설정과 버퍼 수명은 World에 속하며 실행 중 교체/삭제를 지원하지 않는다.
/// </summary>
public struct RecipeRegistry : IComponentData
{
}
