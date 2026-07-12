using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

public partial class CrafterConfigLoadSystem : SystemBase
{
    private const string recipeConfigResourcePath = "Config/CrafterRecipeConfig";
    private const string storageLimitConfigResourcePath = "Config/StorageLimitConfig";

    [Serializable]
    private class CrafterRecipeConfigFile
    {
        public List<CrafterRecipeConfigData> recipes = new List<CrafterRecipeConfigData>();
    }

    [Serializable]
    private class CrafterRecipeConfigData
    {
        public int id;
        public string outputItemType;
        public float craftTime;
        public List<string> conditions = new List<string>();
        public List<CrafterRecipeIngredientConfigData> ingredients = new List<CrafterRecipeIngredientConfigData>();
    }

    [Serializable]
    private class CrafterRecipeIngredientConfigData
    {
        public string itemType;
        public int amount;
    }

    [Serializable]
    private class ItemStorageLimitConfigFile
    {
        public List<ItemStorageLimitConfigData> limits = new List<ItemStorageLimitConfigData>();
    }

    [Serializable]
    private class ItemStorageLimitConfigData
    {
        public string itemType;
        public int maxAmount;
    }

    protected override void OnCreate()
    {
        Entity configEntity = EntityManager.CreateEntity(
            typeof(CrafterConfig),
            typeof(CrafterRecipeElement),
            typeof(CrafterRecipeIngredientElement),
            typeof(ItemStorageLimitElement));

        DynamicBuffer<CrafterRecipeElement> recipes = EntityManager.GetBuffer<CrafterRecipeElement>(configEntity);
        DynamicBuffer<CrafterRecipeIngredientElement> ingredients = EntityManager.GetBuffer<CrafterRecipeIngredientElement>(configEntity);
        DynamicBuffer<ItemStorageLimitElement> storageLimits = EntityManager.GetBuffer<ItemStorageLimitElement>(configEntity);

        LoadRecipes(recipes, ingredients);
        LoadStorageLimits(storageLimits);

        Enabled = false;
    }

    protected override void OnUpdate()
    {
    }

    private void LoadRecipes(
        DynamicBuffer<CrafterRecipeElement> recipes,
        DynamicBuffer<CrafterRecipeIngredientElement> ingredients)
    {
        TextAsset configAsset = Resources.Load<TextAsset>(recipeConfigResourcePath);

        if (configAsset == null)
        {
            Debug.LogError($"Crafter recipe config file not found. Path : Resources/{recipeConfigResourcePath}");
            return;
        }

        CrafterRecipeConfigFile configFile;

        try
        {
            configFile = JsonUtility.FromJson<CrafterRecipeConfigFile>(configAsset.text);
        }
        catch (Exception exception)
        {
            Debug.LogError($"Failed to parse crafter recipe config file. Path : Resources/{recipeConfigResourcePath}, Error : {exception.Message}");
            return;
        }

        if (configFile == null || configFile.recipes == null)
            return;

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

            List<CrafterRecipeIngredientElement> loadedIngredients = LoadIngredients(recipeData);

            if (loadedIngredients.Count == 0)
                continue;

            int recipeId = recipeData.id > 0 ? recipeData.id : i + 1;

            if (!usedRecipeIds.Add(recipeId))
            {
                Debug.LogError($"Duplicated crafter recipe id. Id : {recipeId}");
                continue;
            }

            recipes.Add(new CrafterRecipeElement
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
                ingredients.Add(ingredient);
            }
        }
    }

    private List<CrafterRecipeIngredientElement> LoadIngredients(CrafterRecipeConfigData recipeData)
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

    private void LoadStorageLimits(DynamicBuffer<ItemStorageLimitElement> storageLimits)
    {
        TextAsset configAsset = Resources.Load<TextAsset>(storageLimitConfigResourcePath);

        if (configAsset == null)
        {
            Debug.LogError($"Item storage limit config file not found. Path : Resources/{storageLimitConfigResourcePath}");
            return;
        }

        ItemStorageLimitConfigFile configFile;

        try
        {
            configFile = JsonUtility.FromJson<ItemStorageLimitConfigFile>(configAsset.text);
        }
        catch (Exception exception)
        {
            Debug.LogError($"Failed to parse item storage limit config file. Path : Resources/{storageLimitConfigResourcePath}, Error : {exception.Message}");
            return;
        }

        if (configFile == null || configFile.limits == null)
            return;

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

        foreach (KeyValuePair<ItemTypeEnum, int> pair in limitByItemType)
        {
            storageLimits.Add(new ItemStorageLimitElement
            {
                itemType = pair.Key,
                maxAmount = pair.Value
            });
        }
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
