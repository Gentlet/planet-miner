using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public static class DirectionExtension
{
    public static DirectionEnum NextDirection(this DirectionEnum dir, DirectionEnum next)
    {
        return (DirectionEnum)(((int)dir + (int)next) % (int)DirectionEnum.Count);
    }
    public static DirectionEnum NextDirection(this DirectionEnum dir)
    {
        return (DirectionEnum)(((int)dir + 1) % (int)DirectionEnum.Count);
    }
    public static float3 Tofloat3(this DirectionEnum dir)
    {
        switch (dir)
        {
            case DirectionEnum.Up:
                return new float3(0, 1, 0);
            case DirectionEnum.Down:
                return new float3(0, -1, 0);
            case DirectionEnum.Left:
                return new float3(-1, 0, 0);
            case DirectionEnum.Right:
                return new float3(1, 0, 0);
            default:
                return new float3(0, 0, 0);
        }
    }
    public static Vector2 ToVector2(this DirectionEnum dir)
    {
        switch (dir)
        {
            case DirectionEnum.Up:
                return new Vector2(0, 1);
            case DirectionEnum.Down:
                return new Vector2(0, -1);
            case DirectionEnum.Left:
                return new Vector2(-1, 0);
            case DirectionEnum.Right:
                return new Vector2(1, 0);
            default:
                return new Vector2(0, 0);
        }
    }
    public static int2 ToInt2(this DirectionEnum dir)
    {
        switch (dir)
        {
            case DirectionEnum.Up:
                return new int2(0, 1);
            case DirectionEnum.Down:
                return new int2(0, -1);
            case DirectionEnum.Left:
                return new int2(-1, 0);
            case DirectionEnum.Right:
                return new int2(1, 0);
            default:
                return int2.zero;
        }
    }
    
    public static int ToDegrees(this DirectionEnum dir)
    {
        switch (dir)
        {
            case DirectionEnum.Up:
                return 0;
            case DirectionEnum.Down:
                return 180;
            case DirectionEnum.Left:
                return 90;
            case DirectionEnum.Right:
                return -90;
            default:
                return 0;
        }
    }
}

public static class ResourceTypeExtension
{
    public static ItemTypeEnum ToItemType(this ResourceTypeEnum resourceType)
    {
        switch (resourceType)
        {
            case ResourceTypeEnum.Iron_Ore:
                return ItemTypeEnum.Iron_Ore;
            case ResourceTypeEnum.Copper_Ore:
                return ItemTypeEnum.Copper_Ore;
            case ResourceTypeEnum.Coal:
                return ItemTypeEnum.Coal;
            case ResourceTypeEnum.Stone:
                return ItemTypeEnum.Stone;
            default:
                return ItemTypeEnum.None;
        }
    }
}

public static class VectorExtension
{
    public static Vector2 Floor(this Vector2 v)
    {
        return new Vector2(Mathf.Floor(v.x), Mathf.Floor(v.y));
    }
    public static int2 ToInt2(this Vector2 v)
    {
        return new int2((int)v.x, (int)v.y);
    }
    public static int2 ToInt2(this Vector3 v)
    {
        return new int2((int)v.x, (int)v.y);
    }
    public static int2 ToGridCell(this Vector3 v)
    {
        return new int2(
            Mathf.FloorToInt(v.x + 0.5f),
            Mathf.FloorToInt(v.y + 0.5f));
    }
   
}

public static class Int2Extension
{
    public static Vector2 ToVector2(this int2 i)
    {
        return new Vector2(i.x, i.y);
    }
}

public static class Float3Extension
{
    public static float2 ToFloat2(this float3 v)
    {
        return new float2(v.x, v.y);
    }

    public static int2 ToGridCell(this float3 v)
    {
        return new int2(
            (int)math.floor(v.x + 0.5f),
            (int)math.floor(v.y + 0.5f));
    }
    public static bool IsInsideCell(this float3 v, int2 cell)
    {
        const float cellHalfSize = 0.5f;
        const float epsilon = 0.0001f;

        return math.abs(v.x - cell.x) <= cellHalfSize + epsilon &&
               math.abs(v.y - cell.y) <= cellHalfSize + epsilon;
    }
}

public static class ItemTypeExtension
{
    public static bool IsValid(this ItemTypeEnum type)
    {
        return type > ItemTypeEnum.None && type < ItemTypeEnum.Count;
    }
}

public static class CrafterStateExtension
{
    public static bool CanReceiveItems(this CrafterStateEnum state)
    {
        return state != CrafterStateEnum.NoRecipe;
    }
}

public static class CrafterRecipeExtension
{
    public static float GetCraftTime(this CrafterRecipeElement recipe, float speed)
    {
        if (speed <= 0f)
            return float.PositiveInfinity;

        return recipe.craftTime / speed;
    }
}


public static class CrafterRecipeBufferExtension
{
    public static bool TryFindRecipe(
        this DynamicBuffer<CrafterRecipeElement> recipes,
        ItemTypeEnum outputItemType,
        out CrafterRecipeElement recipe)
    {
        for (int i = 0; i < recipes.Length; i++)
        {
            if (recipes[i].outputItemType == outputItemType)
            {
                recipe = recipes[i];
                return true;
            }
        }

        recipe = default;
        return false;
    }

    public static bool HasIngredient(
        this DynamicBuffer<CrafterRecipeIngredientElement> ingredients,
        int recipeId,
        ItemTypeEnum itemType)
    {
        for (int i = 0; i < ingredients.Length; i++)
        {
            CrafterRecipeIngredientElement ingredient = ingredients[i];

            if (ingredient.recipeId == recipeId && ingredient.itemType == itemType)
                return true;
        }

        return false;
    }
}

public static class CrafterDepositedItemBufferExtension
{
    public static bool HasIngredients(
        this DynamicBuffer<CrafterDepositedItemElement> depositedItems,
        DynamicBuffer<CrafterRecipeIngredientElement> ingredients,
        int recipeId)
    {
        for (int i = 0; i < ingredients.Length; i++)
        {
            CrafterRecipeIngredientElement ingredient = ingredients[i];

            if (ingredient.recipeId != recipeId)
                continue;
            if (depositedItems.CountItems(ingredient.itemType) < ingredient.amount)
                return false;
        }

        return true;
    }

    public static bool HasExceptionItem(
        this DynamicBuffer<CrafterDepositedItemElement> depositedItems,
        DynamicBuffer<CrafterRecipeIngredientElement> ingredients,
        int recipeId)
    {
        for (int i = 0; i < depositedItems.Length; i++)
        {
            if (!ingredients.HasIngredient(recipeId, depositedItems[i].type))
                return true;
        }

        return false;
    }

    public static int CountItems(
        this DynamicBuffer<CrafterDepositedItemElement> depositedItems,
        ItemTypeEnum itemType)
    {
        int count = 0;

        for (int i = 0; i < depositedItems.Length; i++)
        {
            if (depositedItems[i].type == itemType)
                count++;
        }

        return count;
    }
}

public static class ItemStorageLimitBufferExtension
{
    public static int GetStorageLimit(
        this DynamicBuffer<ItemStorageLimitElement> storageLimits,
        ItemTypeEnum itemType)
    {
        for (int i = 0; i < storageLimits.Length; i++)
        {
            if (storageLimits[i].itemType == itemType)
                return storageLimits[i].maxAmount;
        }

        return 0;
    }
}
