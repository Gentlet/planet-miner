using Unity.Entities;

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
        if (!ConfigResourceLoader.TryLoadJson(
                "Crafter recipe",
                recipeConfigResourcePath,
                out string json))
            return;

        CrafterRecipeParseResult result = CrafterConfigParser.ParseRecipes(
            json,
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
        if (!ConfigResourceLoader.TryLoadJson(
                "Item storage limit",
                storageLimitConfigResourcePath,
                out string json))
            return;

        ItemStorageLimitParseResult result = CrafterConfigParser.ParseStorageLimits(
            json,
            storageLimitConfigResourcePath);

        if (result == null)
            return;

        for (int i = 0; i < result.StorageLimits.Count; i++)
            storageLimits.Add(result.StorageLimits[i]);
    }
}
