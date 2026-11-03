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

/// <summary>선택 Command는 EndCommand에 활성 ID를 공개하고 완료 Apply는 EndBuilding에 비운다. 빈 ID는 미선택이다.</summary>
public struct ResearchState : IComponentData
{
    public FixedString32Bytes ActiveResearchId;
}

/// <summary>BuildingConfig 엔티티의 완료 연구 보너스 합계다. 초기화가 재구성하고 완료 Apply가 EndBuilding에 갱신한다. 기본 속도와 소비 전력은 보존한다.</summary>
public struct ResearchStatBonuses : IComponentData
{
    public double MiningSpeed;
    public double CraftingSpeed;
    public double BeltSpeed;
    public double ResearchSpeed;
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

/// <summary>새 Registry는 0/미완료로 시작하고 유효한 사전 등록 진척도는 보존한다. ResearchProgressApply는 기여 합계/완료와 보상을 EndBuilding에 공개하며 double로 초과 합계도 보존한다.</summary>
[InternalBufferCapacity(0)]
public struct ResearchProgressElement : IBufferElementData
{
    public FixedString32Bytes ResearchId;
    public double Progress;
    public bool IsCompleted;
}

/// <summary>Execution이 선소비/전력 시간으로 진행하는 로컬 원본이다. 선택은 EndCommand, 전역 완료는 EndBuilding에 초기화한다. 빈 ID는 주기 없음이다.</summary>
public struct ResearchCycleState : IComponentData
{
    public FixedString32Bytes ResearchId;
    public float ElapsedTime;
}

public enum ResearchCycleResetReasonEnum : byte
{
    None,
    SelectionChanged,
    ResearchCompleted
}

/// <summary>Registry의 마지막 전체 주기 초기화 결과다. Command/완료 Apply가 각 종료 경계에 기록하며 UI가 새 revision만 잠깐 표시한다.</summary>
public struct ResearchCycleResetResult : IComponentData
{
    public uint Revision;
    public ResearchCycleResetReasonEnum Reason;
}
