using Unity.Entities;

public static class ResearchMaterialUtility
{
    public const int BufferedCycleCount = 2;

    public static int MaximumBufferedQuantity(int amount)
    {
        return amount * BufferedCycleCount;
    }

    public static bool HasMaterials(in ResearchDefinitionElement definition,
        DynamicBuffer<ResearchIngredientElement> ingredients, DynamicBuffer<StorageSlotElement> stored)
    {
        for (int i = 0; i < definition.IngredientCount; i++)
        {
            var ingredient = ingredients[definition.IngredientStart + i];
            int count = InventorySlotUtility.Count(stored, ingredient.ItemType);
            if (count < ingredient.Amount)
            {
                return false;
            }
        }
        return true;
    }
}
