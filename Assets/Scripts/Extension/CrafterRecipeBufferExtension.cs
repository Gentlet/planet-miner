using Unity.Collections;
using Unity.Entities;

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

    public static bool TryFindRecipe(
        this NativeArray<CrafterRecipeElement> recipes,
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

    public static bool HasIngredient(
        this NativeArray<CrafterRecipeIngredientElement> ingredients,
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
