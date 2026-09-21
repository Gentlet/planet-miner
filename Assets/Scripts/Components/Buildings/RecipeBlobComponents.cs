using System;
using Unity.Entities;

/// <summary>
/// 레시피 개별 입력 재료 (Blittable).
/// </summary>
public struct RecipeIngredientBlob
{
    public ItemTypeEnum ItemType;
    public int Amount;

    public RecipeIngredientBlob(ItemTypeEnum itemType, int amount)
    {
        ItemType = itemType;
        Amount = amount;
    }
}

/// <summary>
/// 레시피 개별 출력물 (주 생산품 또는 부산품, Blittable).
/// </summary>
public struct RecipeOutputBlob
{
    public ItemTypeEnum ItemType;
    public int Amount;
    public bool IsByproduct; // false: 주 생산품(Primary), true: 부산품(Byproduct)

    public RecipeOutputBlob(ItemTypeEnum itemType, int amount, bool isByproduct = false)
    {
        ItemType = itemType;
        Amount = amount;
        IsByproduct = isByproduct;
    }
}

/// <summary>
/// 개별 제작 레시피 데이터 (BlobArray 가변 목록 지원, Immutable).
/// </summary>
public struct RecipeBlob
{
    public int Id;
    public float CraftTime;
    public byte ConditionFlags; // 예약 필드 (확장성용)

    public BlobArray<RecipeIngredientBlob> Ingredients;
    public BlobArray<RecipeOutputBlob> Outputs;

    /// <summary>
    /// 해당 레시피의 주 생산품(Primary Output)을 반환합니다.
    /// </summary>
    public bool TryGetPrimaryOutput(out RecipeOutputBlob primaryOutput)
    {
        for (int i = 0; i < Outputs.Length; i++)
        {
            if (!Outputs[i].IsByproduct)
            {
                primaryOutput = Outputs[i];
                return true;
            }
        }

        if (Outputs.Length > 0)
        {
            primaryOutput = Outputs[0];
            return true;
        }

        primaryOutput = default;
        return false;
    }

    /// <summary>
    /// 특정 아이템 타입이 해당 레시피의 재료인지 검사하고 필요 수량을 반환합니다.
    /// </summary>
    public bool TryFindIngredient(ItemTypeEnum itemType, out int requiredAmount)
    {
        for (int i = 0; i < Ingredients.Length; i++)
        {
            if (Ingredients[i].ItemType == itemType)
            {
                requiredAmount = Ingredients[i].Amount;
                return true;
            }
        }

        requiredAmount = 0;
        return false;
    }
}

/// <summary>
/// 전역 레시피 레지스트리 루트 Blob.
/// </summary>
public struct RecipeRegistryBlob
{
    public BlobArray<RecipeBlob> Recipes;

    /// <summary>
    /// Recipe ID로 레시피 인덱스를 검색합니다.
    /// </summary>
    public bool TryGetRecipeIndex(int recipeId, out int recipeIndex)
    {
        for (int i = 0; i < Recipes.Length; i++)
        {
            if (Recipes[i].Id == recipeId)
            {
                recipeIndex = i;
                return true;
            }
        }

        recipeIndex = -1;
        return false;
    }

    /// <summary>
    /// 주 생산품(Primary Output) 아이템 타입으로 레시피 인덱스를 검색합니다.
    /// </summary>
    public bool TryFindRecipeIndexByPrimaryOutput(ItemTypeEnum outputType, out int recipeIndex)
    {
        for (int i = 0; i < Recipes.Length; i++)
        {
            if (Recipes[i].TryGetPrimaryOutput(out var primary) && primary.ItemType == outputType)
            {
                recipeIndex = i;
                return true;
            }
        }

        recipeIndex = -1;
        return false;
    }
}

/// <summary>
/// 전역 레시피 레지스트리 싱글톤 컴포넌트.
/// </summary>
public struct RecipeRegistry : IComponentData
{
    public BlobAssetReference<RecipeRegistryBlob> Value;

    public RecipeRegistry(BlobAssetReference<RecipeRegistryBlob> value)
    {
        Value = value;
    }
}

/// <summary>
/// 제작기(Crafter) 가동 상태 열거형.
/// </summary>
public enum CrafterStatusEnum : byte
{
    NoRecipe,           // 선택된 레시피 없음
    Idle,               // 대기 중 (재료 준비 완료 또는 시작 대기)
    Crafting,           // 제작 진행 중 (Progress 누적 중)
    WaitingForInput,    // 재료 부족으로 대기
    WaitingForOutput    // 완성품/부산품 출력 슬롯 만석으로 정체 (Backpressure)
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

/// <summary>
/// 제작기 내부 Storage 슬롯 레이아웃 설정 메타데이터.
/// 재료 입력 슬롯과 완성품/부산품 출력 슬롯을 논리적으로 분할합니다.
/// </summary>
public struct CrafterSlotConfig : IComponentData
{
    public int InputSlotStart;   // 입력 슬롯 시작 번호 (기본: 0)
    public int InputSlotCount;   // 입력 슬롯 개수 (기본: 4, 슬롯 0~3)
    public int OutputSlotStart;  // 출력 슬롯 시작 번호 (기본: 4)
    public int OutputSlotCount;  // 출력 슬롯 개수 (기본: 2, 주완성품 4, 부산품 5)

    public static CrafterSlotConfig Default => new CrafterSlotConfig
    {
        InputSlotStart = 0,
        InputSlotCount = 4,
        OutputSlotStart = 4,
        OutputSlotCount = 2
    };

    public bool IsInputSlot(int slotIndex)
    {
        return slotIndex >= InputSlotStart && slotIndex < InputSlotStart + InputSlotCount;
    }

    public bool IsOutputSlot(int slotIndex)
    {
        return slotIndex >= OutputSlotStart && slotIndex < OutputSlotStart + OutputSlotCount;
    }
}
