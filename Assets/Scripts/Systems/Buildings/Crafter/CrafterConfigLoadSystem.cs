using Unity.Entities;
using UnityEngine;

public partial class CrafterConfigLoadSystem : SystemBase
{
    private const string recipeConfigResourcePath = "Config/CrafterRecipeConfig";
    private const string storageLimitConfigResourcePath = "Config/StorageLimitConfig";

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

    private static void LoadRecipes(
        DynamicBuffer<CrafterRecipeElement> recipes,
        DynamicBuffer<CrafterRecipeIngredientElement> ingredients)
    {
        TextAsset configAsset = Resources.Load<TextAsset>(recipeConfigResourcePath);

        if (configAsset == null)
        {
            Debug.LogError($"Crafter recipe config file not found. Path : Resources/{recipeConfigResourcePath}");
            return;
        }

        CrafterRecipeParseResult result = CrafterConfigParser.ParseRecipes(
            configAsset.text,
            recipeConfigResourcePath);

        if (result == null)
            return;

        for (int i = 0; i < result.Recipes.Count; i++)
            recipes.Add(result.Recipes[i]);

        for (int i = 0; i < result.Ingredients.Count; i++)
            ingredients.Add(result.Ingredients[i]);
    }

    private static void LoadStorageLimits(DynamicBuffer<ItemStorageLimitElement> storageLimits)
    {
        TextAsset configAsset = Resources.Load<TextAsset>(storageLimitConfigResourcePath);

        if (configAsset == null)
        {
            Debug.LogError($"Item storage limit config file not found. Path : Resources/{storageLimitConfigResourcePath}");
            return;
        }

        ItemStorageLimitParseResult result = CrafterConfigParser.ParseStorageLimits(
            configAsset.text,
            storageLimitConfigResourcePath);

        if (result == null)
            return;

        for (int i = 0; i < result.StorageLimits.Count; i++)
            storageLimits.Add(result.StorageLimits[i]);
    }
}
