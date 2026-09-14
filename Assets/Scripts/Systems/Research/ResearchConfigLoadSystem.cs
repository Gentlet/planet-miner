using System.Collections.Generic;
using Unity.Entities;

public partial class ResearchConfigLoadSystem : SystemBase
{
    private const string ConfigResourcePath = "Config/ResearchConfig";

    protected override void OnCreate()
    {
        RequireForUpdate<CrafterConfig>();
    }

    protected override void OnUpdate()
    {
        if (!ConfigResourceLoader.TryLoadJson(
                "Research",
                ConfigResourcePath,
                out string json))
        {
            Enabled = false;
            return;
        }

        HashSet<int> validRecipeIds = GetValidRecipeIds();
        ResearchConfigParseResult result = ResearchConfigParser.Parse(
            json,
            ConfigResourcePath,
            validRecipeIds);

        if (result == null)
        {
            Enabled = false;
            return;
        }

        Entity configEntity = EntityManager.CreateEntity(
            typeof(ResearchConfig),
            typeof(ResearchState),
            typeof(ResearchDefinitionElement),
            typeof(ResearchPrerequisiteElement),
            typeof(ResearchIngredientElement),
            typeof(ResearchRewardElement),
            typeof(ResearchProgressElement),
            typeof(BuildingUnlockElement),
            typeof(RecipeUnlockElement),
            typeof(ResearchStatModifierElement));

        PublishConfig(configEntity, result);
        Enabled = false;
    }

    private HashSet<int> GetValidRecipeIds()
    {
        DynamicBuffer<CrafterRecipeElement> recipes = SystemAPI
            .GetSingletonBuffer<CrafterRecipeElement>(true);
        HashSet<int> recipeIds = new();

        for (int i = 0; i < recipes.Length; i++)
            recipeIds.Add(recipes[i].id);

        return recipeIds;
    }

    private void PublishConfig(
        Entity configEntity,
        ResearchConfigParseResult result)
    {
        DynamicBuffer<ResearchDefinitionElement> definitions =
            EntityManager.GetBuffer<ResearchDefinitionElement>(configEntity);
        DynamicBuffer<ResearchPrerequisiteElement> prerequisites =
            EntityManager.GetBuffer<ResearchPrerequisiteElement>(configEntity);
        DynamicBuffer<ResearchIngredientElement> ingredients =
            EntityManager.GetBuffer<ResearchIngredientElement>(configEntity);
        DynamicBuffer<ResearchRewardElement> rewards =
            EntityManager.GetBuffer<ResearchRewardElement>(configEntity);
        DynamicBuffer<ResearchProgressElement> progress =
            EntityManager.GetBuffer<ResearchProgressElement>(configEntity);
        DynamicBuffer<BuildingUnlockElement> buildingUnlocks =
            EntityManager.GetBuffer<BuildingUnlockElement>(configEntity);
        DynamicBuffer<RecipeUnlockElement> recipeUnlocks =
            EntityManager.GetBuffer<RecipeUnlockElement>(configEntity);

        for (int i = 0; i < result.Definitions.Count; i++)
        {
            ResearchDefinitionElement definition = result.Definitions[i];
            definitions.Add(definition);
            progress.Add(new ResearchProgressElement
            {
                researchId = definition.stableId,
                progress = 0f,
                completed = false
            });
        }

        for (int i = 0; i < result.Prerequisites.Count; i++)
            prerequisites.Add(result.Prerequisites[i]);

        for (int i = 0; i < result.Ingredients.Count; i++)
            ingredients.Add(result.Ingredients[i]);

        for (int i = 0; i < result.Rewards.Count; i++)
            rewards.Add(result.Rewards[i]);

        for (int i = 0; i < result.InitialBuildingUnlocks.Count; i++)
            buildingUnlocks.Add(result.InitialBuildingUnlocks[i]);

        for (int i = 0; i < result.InitialRecipeUnlocks.Count; i++)
            recipeUnlocks.Add(result.InitialRecipeUnlocks[i]);
    }
}
