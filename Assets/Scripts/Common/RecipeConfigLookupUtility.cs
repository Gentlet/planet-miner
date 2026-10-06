using Unity.Entities;

/// <summary>
/// 역할·목적: 게시된 레시피/출력 버퍼에서 ID 또는 주생산품이 일치하는 첫 레시피 인덱스를 찾는다.
/// 입력·출력: 원본 버퍼 순서를 유지하며 누락이면 false와 -1을 반환한다. 부산물을 주생산품으로 선택하지 않는다.
/// 이용·수명: 제작 레시피 요청/실행 경계에서 사용하는 읽기 계산이며 별도 캐시나 ECS 상태를 만들지 않는다.
/// </summary>
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
