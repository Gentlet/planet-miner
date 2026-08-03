using System;
using System.Collections.Generic;
using UnityEngine;

internal sealed class CrafterRecipeParseResult
{
    public readonly List<CrafterRecipeElement> Recipes = new List<CrafterRecipeElement>();
    public readonly List<CrafterRecipeIngredientElement> Ingredients = new List<CrafterRecipeIngredientElement>();
}

internal sealed class ItemStorageLimitParseResult
{
    public readonly List<ItemStorageLimitElement> StorageLimits = new List<ItemStorageLimitElement>();
}

internal static class CrafterConfigParser
{
    public static CrafterRecipeParseResult ParseRecipes(string json, string resourcePath)
    {
        CrafterRecipeConfigFile configFile;

        try
        {
            configFile = JsonUtility.FromJson<CrafterRecipeConfigFile>(json);
        }
        catch (Exception exception)
        {
            Debug.LogError($"Failed to parse crafter recipe config file. Path : Resources/{resourcePath}, Error : {exception.Message}");
            return null;
        }

        if (configFile == null || configFile.recipes == null)
            return null;

        CrafterRecipeParseResult result = new CrafterRecipeParseResult();
        HashSet<int> usedRecipeIds = new HashSet<int>();

        for (int i = 0; i < configFile.recipes.Count; i++)
        {
            CrafterRecipeConfigData recipeData = configFile.recipes[i];

            if (recipeData == null)
                continue;
            if (!TryParseItemType(recipeData.outputItemType, out ItemTypeEnum outputItemType))
                continue;
            if (recipeData.craftTime <= 0f)
                continue;

            List<CrafterRecipeIngredientElement> loadedIngredients = ParseIngredients(recipeData);

            if (loadedIngredients.Count == 0)
                continue;

            int recipeId = recipeData.id > 0 ? recipeData.id : i + 1;

            if (!usedRecipeIds.Add(recipeId))
            {
                Debug.LogError($"Duplicated crafter recipe id. Id : {recipeId}");
                continue;
            }

            result.Recipes.Add(new CrafterRecipeElement
            {
                id = recipeId,
                outputItemType = outputItemType,
                craftTime = recipeData.craftTime,
                conditionFlags = ParseConditionFlags(recipeData.conditions)
            });

            for (int ingredientIndex = 0; ingredientIndex < loadedIngredients.Count; ingredientIndex++)
            {
                CrafterRecipeIngredientElement ingredient = loadedIngredients[ingredientIndex];
                ingredient.recipeId = recipeId;
                result.Ingredients.Add(ingredient);
            }
        }

        return result;
    }

    public static ItemStorageLimitParseResult ParseStorageLimits(string json, string resourcePath)
    {
        ItemStorageLimitConfigFile configFile;

        try
        {
            configFile = JsonUtility.FromJson<ItemStorageLimitConfigFile>(json);
        }
        catch (Exception exception)
        {
            Debug.LogError($"Failed to parse item storage limit config file. Path : Resources/{resourcePath}, Error : {exception.Message}");
            return null;
        }

        if (configFile == null || configFile.limits == null)
            return null;

        Dictionary<ItemTypeEnum, int> limitByItemType = new Dictionary<ItemTypeEnum, int>();

        for (int i = 0; i < configFile.limits.Count; i++)
        {
            ItemStorageLimitConfigData limitData = configFile.limits[i];

            if (limitData == null)
                continue;
            if (!TryParseItemType(limitData.itemType, out ItemTypeEnum itemType))
                continue;
            if (limitData.maxAmount <= 0)
                continue;

            limitByItemType[itemType] = limitData.maxAmount;
        }

        ItemStorageLimitParseResult result = new ItemStorageLimitParseResult();

        foreach (KeyValuePair<ItemTypeEnum, int> pair in limitByItemType)
        {
            result.StorageLimits.Add(new ItemStorageLimitElement
            {
                itemType = pair.Key,
                maxAmount = pair.Value
            });
        }

        return result;
    }

    private static List<CrafterRecipeIngredientElement> ParseIngredients(CrafterRecipeConfigData recipeData)
    {
        List<CrafterRecipeIngredientElement> loadedIngredients = new List<CrafterRecipeIngredientElement>();

        if (recipeData.ingredients == null)
            return loadedIngredients;

        for (int i = 0; i < recipeData.ingredients.Count; i++)
        {
            CrafterRecipeIngredientConfigData ingredientData = recipeData.ingredients[i];

            if (ingredientData == null)
                continue;
            if (!TryParseItemType(ingredientData.itemType, out ItemTypeEnum itemType))
                continue;
            if (ingredientData.amount <= 0)
                continue;

            loadedIngredients.Add(new CrafterRecipeIngredientElement
            {
                itemType = itemType,
                amount = ingredientData.amount
            });
        }

        return loadedIngredients;
    }

    private static bool TryParseItemType(string value, out ItemTypeEnum itemType)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            Enum.TryParse(value, true, out itemType) &&
            itemType.IsValid())
            return true;

        itemType = ItemTypeEnum.None;
        Debug.LogError($"Invalid item type in crafter config. Type : {value}");
        return false;
    }

    private static CrafterRecipeConditionFlags ParseConditionFlags(List<string> conditions)
    {
        if (conditions == null)
            return CrafterRecipeConditionFlags.None;

        CrafterRecipeConditionFlags result = CrafterRecipeConditionFlags.None;

        for (int i = 0; i < conditions.Count; i++)
        {
            string condition = conditions[i];

            if (string.IsNullOrWhiteSpace(condition) ||
                string.Equals(condition, nameof(CrafterRecipeConditionFlags.None), StringComparison.OrdinalIgnoreCase))
                continue;

            if (Enum.TryParse(condition, true, out CrafterRecipeConditionFlags parsed))
            {
                result |= parsed;
                continue;
            }

            Debug.LogWarning($"Unknown crafter recipe condition. Condition : {condition}");
        }

        return result;
    }
}
