using Unity.Collections;
using Unity.Entities;

public static class StoredItemBufferExtension
{
    public static bool HasIngredients(
        this DynamicBuffer<StoredItemElement> storedItems,
        DynamicBuffer<CrafterRecipeIngredientElement> ingredients,
        int recipeId)
    {
        for (int i = 0; i < ingredients.Length; i++)
        {
            CrafterRecipeIngredientElement ingredient = ingredients[i];

            if (ingredient.recipeId != recipeId)
                continue;
            if (storedItems.CountItems(ingredient.itemType) < ingredient.amount)
                return false;
        }

        return true;
    }

    public static bool HasIngredients(
        this DynamicBuffer<StoredItemElement> storedItems,
        NativeArray<CrafterRecipeIngredientElement> ingredients,
        int recipeId)
    {
        for (int i = 0; i < ingredients.Length; i++)
        {
            CrafterRecipeIngredientElement ingredient = ingredients[i];

            if (ingredient.recipeId != recipeId)
                continue;
            if (storedItems.CountItems(ingredient.itemType) < ingredient.amount)
                return false;
        }

        return true;
    }

    public static bool HasExceptionItem(
        this DynamicBuffer<StoredItemElement> storedItems,
        DynamicBuffer<CrafterRecipeIngredientElement> ingredients,
        int recipeId)
    {
        for (int i = 0; i < storedItems.Length; i++)
        {
            if (!ingredients.HasIngredient(recipeId, storedItems[i].type))
                return true;
        }

        return false;
    }

    public static bool HasExceptionItem(
        this DynamicBuffer<StoredItemElement> storedItems,
        NativeArray<CrafterRecipeIngredientElement> ingredients,
        int recipeId)
    {
        for (int i = 0; i < storedItems.Length; i++)
        {
            if (!ingredients.HasIngredient(recipeId, storedItems[i].type))
                return true;
        }

        return false;
    }

    public static int CountItems(
        this DynamicBuffer<StoredItemElement> storedItems,
        ItemTypeEnum itemType)
    {
        int count = 0;

        for (int i = 0; i < storedItems.Length; i++)
        {
            if (storedItems[i].type == itemType)
                count++;
        }

        return count;
    }

    public static int GetUsedSlotCount(
        this DynamicBuffer<StoredItemElement> storedItems,
        DynamicBuffer<ItemStorageLimitElement> storageLimits)
    {
        int usedSlotCount = 0;

        for (ItemTypeEnum itemType = ItemTypeEnum.Iron_Ore;
             itemType < ItemTypeEnum.Count;
             itemType++)
        {
            int itemCount = storedItems.CountItems(itemType);

            if (itemCount == 0)
                continue;

            int stackLimit = storageLimits.GetStorageLimit(itemType);
            usedSlotCount += stackLimit > 0
                ? (itemCount + stackLimit - 1) / stackLimit
                : itemCount;
        }

        return usedSlotCount;
    }

    public static int GetUsedSlotCount(
        this DynamicBuffer<StoredItemElement> storedItems,
        NativeArray<ItemStorageLimitElement> storageLimits)
    {
        int usedSlotCount = 0;

        for (ItemTypeEnum itemType = ItemTypeEnum.Iron_Ore;
             itemType < ItemTypeEnum.Count;
             itemType++)
        {
            int itemCount = storedItems.CountItems(itemType);

            if (itemCount == 0)
                continue;

            int stackLimit = storageLimits.GetStorageLimit(itemType);
            usedSlotCount += stackLimit > 0
                ? (itemCount + stackLimit - 1) / stackLimit
                : itemCount;
        }

        return usedSlotCount;
    }
}
