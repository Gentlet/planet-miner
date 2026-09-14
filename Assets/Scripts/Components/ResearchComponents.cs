using Unity.Collections;
using Unity.Entities;

public enum ResearchRewardTypeEnum : byte
{
    BuildingUnlock,
    RecipeUnlock,
    StatModifier,
    Count
}

public enum ResearchStatModifierTypeEnum : byte
{
    MiningSpeed,
    CraftingSpeed,
    BeltSpeed,
    ResearchSpeed,
    Count
}

public enum ResearchBuildingStateEnum : byte
{
    NoActiveResearch,
    WaitingForMaterials,
    NoPower,
    Researching,
    Count
}

public enum ResearchCycleResetReasonEnum : byte
{
    None,
    ResearchChanged,
    ResearchCompleted,
    Count
}

public struct ResearchConfig : IComponentData
{
}

public struct ResearchState : IComponentData
{
    public FixedString64Bytes activeResearchId;
}

public struct ResearchDefinitionElement : IBufferElementData
{
    public FixedString64Bytes stableId;
    public FixedString128Bytes displayName;
    public FixedString512Bytes description;
    public float cycleDuration;
    public float progressPerCycle;
    public float requiredProgress;
}

public struct ResearchPrerequisiteElement : IBufferElementData
{
    public FixedString64Bytes researchId;
    public FixedString64Bytes prerequisiteId;
}

public struct ResearchIngredientElement : IBufferElementData
{
    public FixedString64Bytes researchId;
    public ItemTypeEnum itemType;
    public int amount;
}

public struct ResearchRewardElement : IBufferElementData
{
    public FixedString64Bytes researchId;
    public ResearchRewardTypeEnum type;
    public BuildingTypeEnum buildingType;
    public int recipeId;
    public ResearchStatModifierTypeEnum statType;
    public float percentBonus;
}

public struct ResearchProgressElement : IBufferElementData
{
    public FixedString64Bytes researchId;
    public float progress;
    public bool completed;
}

public struct BuildingUnlockElement : IBufferElementData
{
    public BuildingTypeEnum buildingType;
}

public struct RecipeUnlockElement : IBufferElementData
{
    public int recipeId;
}

public struct ResearchStatModifierElement : IBufferElementData
{
    public ResearchStatModifierTypeEnum type;
    public float percentBonus;
}

public struct ResearchSelectionRequest : IComponentData
{
    public FixedString64Bytes researchId;
}

public struct ResearchBuilding : IComponentData
{
    public float speed;
    public FixedString64Bytes cycleResearchId;
    public float progress;
    public bool cycleActive;
    public ResearchBuildingStateEnum state;
    public ResearchCycleResetReasonEnum resetReason;
    public float resetNoticeRemaining;
}
