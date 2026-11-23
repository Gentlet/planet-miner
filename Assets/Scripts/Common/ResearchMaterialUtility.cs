using Unity.Entities;

public static class ResearchMaterialUtility
{
    public const int BufferedCycleCount = 2;

    public static int MaximumBufferedQuantity(int amount)
    {
        return amount * BufferedCycleCount;
    }

    /// <summary>모든 연구 정의의 최대 재료 요구를 고정 슬롯으로 구성한다.</summary>
    internal static void ConfigureInputSlots(DynamicBuffer<ResearchIngredientElement> ingredients,
        DynamicBuffer<ItemConfigElement> items, DynamicBuffer<BuildingInputSlotElement> slots)
    {
        slots.Clear();
        foreach (var item in items)
        {
            if (item.ItemType == ItemTypeEnum.None)
            {
                continue;
            }
            int maximum = 0;
            foreach (var ingredient in ingredients)
            {
                if (ingredient.ItemType == item.ItemType && ingredient.Amount > maximum)
                {
                    maximum = ingredient.Amount;
                }
            }
            if (maximum == 0)
            {
                continue;
            }
            long count = BuildingInputSlotUtility.RequiredSlotCount((long)maximum * BufferedCycleCount, item.MaxStack);
            for (int i = 0; i < count; i++)
            {
                slots.Add(new BuildingInputSlotElement(item.ItemType));
            }
        }
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
