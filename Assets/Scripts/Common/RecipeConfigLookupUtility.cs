using Unity.Entities;

/// <summary>
/// 역할·목적: 게시된 레시피의 ID/주생산품 첫 일치 조회와 재료 전체의 소비 가능 여부 검사를 제공한다.
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

    /// <summary>
    /// 품목이 유일한 모든 재료 요구를 충족하는지 검사한다. 보관 버퍼와 삭제 요청은 변경하지 않는다.
    /// Decision의 착수 판단과 Execution의 최종 소비 확인이 같은 조건을 사용한다.
    /// </summary>
    public static bool HasRequiredIngredients(
        in RecipeConfigElement recipe,
        DynamicBuffer<RecipeIngredientElement> ingredients,
        DynamicBuffer<StoredItemElement> storedItems,
        in ComponentLookup<DestroyItemRequest> destroyRequests)
    {
        for (int i = 0; i < recipe.IngredientCount; i++)
        {
            var ingredient = ingredients[recipe.IngredientStart + i];
            if (!recipe.TryFindIngredient(ingredients, ingredient.ItemType, out int requiredAmount))
            {
                return false;
            }

            int availableAmount = 0;
            for (int s = 0; s < storedItems.Length; s++)
            {
                var storedItem = storedItems[s];
                if (storedItem.ItemType != ingredient.ItemType)
                {
                    continue;
                }

                if (CanConsumeStoredItem(storedItem.ItemEntity, destroyRequests))
                {
                    availableAmount++;
                }
            }

            if (availableAmount < requiredAmount)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>소유 버퍼의 실물에 비활성 삭제 요청이 있어야 새 소비 대상으로 사용할 수 있다.</summary>
    public static bool CanConsumeStoredItem(Entity item, in ComponentLookup<DestroyItemRequest> destroyRequests)
    {
        if (!destroyRequests.HasComponent(item))
        {
            return false;
        }

        return !destroyRequests.IsComponentEnabled(item);
    }
}
