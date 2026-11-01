using Unity.Collections;
using Unity.Entities;

public enum ResearchRewardTypeEnum : byte
{
    None,
    BuildingUnlock,
    RecipeUnlock,
    StatModifier
}

public enum ResearchStatKeyEnum : byte
{
    None,
    MiningSpeed,
    CraftingSpeed,
    BeltSpeed,
    ResearchSpeed
}

/// <summary>연구 정의와 전역 상태의 World 단일 소유자다. 초기화가 전체 검증 후 즉시 게시하며 World가 해제한다.</summary>
public struct ResearchRegistry : IComponentData
{
}

/// <summary>선택 Command가 EndCommand에 공개하는 활성 연구 ID다. 빈 ID는 미선택이며 초기화는 선택/진행을 실행하지 않는다.</summary>
public struct ResearchState : IComponentData
{
    public FixedString32Bytes ActiveResearchId;
}

/// <summary>Registry의 읽기 전용 정의다. 각 범위는 같은 엔티티의 선행/재료/보상 버퍼를 가리키며 표시 순서를 보존한다.</summary>
[InternalBufferCapacity(0)]
public struct ResearchDefinitionElement : IBufferElementData
{
    public FixedString32Bytes StableId;
    public FixedString128Bytes DisplayName;
    public FixedString512Bytes Description;
    public float CycleDuration;
    public float ProgressPerCycle;
    public float RequiredProgress;
    public int PrerequisiteStart;
    public int PrerequisiteCount;
    public int IngredientStart;
    public int IngredientCount;
    public int RewardStart;
    public int RewardCount;
}

[InternalBufferCapacity(0)]
public struct ResearchPrerequisiteElement : IBufferElementData
{
    public FixedString32Bytes ResearchId;
}

/// <summary>한 주기의 선소비 수량이다. 실제 실물/보관 관계와 구분하며 2주기 수량이 Int32 범위를 넘지 않아야 한다.</summary>
[InternalBufferCapacity(0)]
public struct ResearchIngredientElement : IBufferElementData
{
    public ItemTypeEnum ItemType;
    public int Amount;
}

/// <summary>완료 보상의 읽기 전용 원본이다. 종류별 대상 필드만 사용하며 보너스 0.10은 +10%다.</summary>
[InternalBufferCapacity(0)]
public struct ResearchRewardElement : IBufferElementData
{
    public ResearchRewardTypeEnum Type;
    public BuildingTypeEnum BuildingType;
    public int RecipeId;
    public ResearchStatKeyEnum StatKey;
    public float Bonus;
}

/// <summary>레시피 초기 해금의 읽기 전용 원본이다. 건물 초기 해금은 기존 BuildingConfigElement.IsUnlocked가 소유한다.</summary>
[InternalBufferCapacity(0)]
public struct ResearchInitialRecipeUnlockElement : IBufferElementData
{
    public int RecipeId;
}

/// <summary>전역 진행/완료 소유자가 작성할 ID별 원본이다. 초기화는 0/미완료로 만들고 사전 등록 원본을 초기화하지 않는다.</summary>
[InternalBufferCapacity(0)]
public struct ResearchProgressElement : IBufferElementData
{
    public FixedString32Bytes ResearchId;
    public float Progress;
    public bool IsCompleted;
}

/// <summary>개별 연구건물의 로컬 주기 원본이다. 빈 ID는 주기 없음이며 선택 변경은 EndCommand에 초기화한다. 전역 진척/완료/해금을 복제하지 않는다.</summary>
public struct ResearchCycleState : IComponentData
{
    public FixedString32Bytes ResearchId;
    public float ElapsedTime;
}
