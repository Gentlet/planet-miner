using System;
using Unity.Entities;

public enum CrafterStateEnum : byte
{
    NoRecipe,
    Idle,
    Crafting,
    WaitingForOutput,
    WaitingForExceptionItem
}

[Flags]
public enum CrafterRecipeConditionFlags : byte
{
    None = 0
}

public struct Crafter : IComponentData
{
    public float speed;
    public ItemTypeEnum selectedItemType;
    public float progress;
    public CrafterStateEnum state;
}

public struct DepositedItem : IComponentData
{
    public Entity owner;
}

public struct CrafterRecipeChangeRequest : IComponentData
{
    public Entity crafterEntity;
    public ItemTypeEnum selectedItemType;
}

public struct CrafterConfig : IComponentData
{
}

public struct CrafterDepositedItemElement : IBufferElementData
{
    public Entity itemEntity;
    public ItemTypeEnum type;
}

public struct CrafterRecipeElement : IBufferElementData
{
    public int id;
    public ItemTypeEnum outputItemType;
    public float craftTime;
    public CrafterRecipeConditionFlags conditionFlags;
}

public struct CrafterRecipeIngredientElement : IBufferElementData
{
    public int recipeId;
    public ItemTypeEnum itemType;
    public int amount;
}

public struct ItemStorageLimitElement : IBufferElementData
{
    public ItemTypeEnum itemType;
    public int maxAmount;
}
