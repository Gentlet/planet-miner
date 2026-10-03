using Unity.Entities;

/// <summary>게시된 레시피 버퍼에서 입력 순서상 첫 일치를 찾는다. 별도 캐시를 소유하지 않는다.</summary>
public static class RecipeConfigLookupUtility
{
    public static bool TryGetRecipeIndex(DynamicBuffer<RecipeConfigElement> recipes, int recipeId, out int recipeIndex)
    {
        for (int i = 0; i < recipes.Length; i++)
        {
            if (recipes[i].Id == recipeId)
            {
                recipeIndex = i;
                return true;
            }
        }

        recipeIndex = -1;
        return false;
    }

    public static bool TryFindRecipeIndexByPrimaryOutput(
        DynamicBuffer<RecipeConfigElement> recipes,
        DynamicBuffer<RecipeOutputElement> outputs,
        ItemTypeEnum outputType,
        out int recipeIndex)
    {
        for (int i = 0; i < recipes.Length; i++)
        {
            if (recipes[i].TryGetPrimaryOutput(outputs, out var primary) && primary.ItemType == outputType)
            {
                recipeIndex = i;
                return true;
            }
        }

        recipeIndex = -1;
        return false;
    }
}