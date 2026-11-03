using Unity.Entities;

public static class ResearchMaterialUtility
{
    public const int BufferedCycleCount = 2;

    public static int MaximumBufferedQuantity(int amount)
    {
        return amount * BufferedCycleCount;
    }

    public static long SlotCountForTwoCycles(int amount, int maximumStack)
    {
        return ((long)amount * BufferedCycleCount + maximumStack - 1) / maximumStack;
    }

    public static bool HasMaterials(in ResearchDefinitionElement definition,
        DynamicBuffer<ResearchIngredientElement> ingredients, DynamicBuffer<StoredItemElement> stored,
        ComponentLookup<DestroyItemRequest> destroyRequests)
    {
        for (int i = 0; i < definition.IngredientCount; i++)
        {
            var ingredient = ingredients[definition.IngredientStart + i];
            int count = 0;
            foreach (var item in stored)
            {
                if (item.ItemType == ingredient.ItemType &&
                    RecipeConfigLookupUtility.CanConsumeStoredItem(item.ItemEntity, destroyRequests))
                {
                    count++;
                }
            }
            if (count < ingredient.Amount)
            {
                return false;
            }
        }
        return true;
    }
}
