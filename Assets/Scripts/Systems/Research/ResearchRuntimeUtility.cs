using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

public static class ResearchRuntimeUtility
{
    public static bool TryGetDefinition(
        this DynamicBuffer<ResearchDefinitionElement> definitions,
        FixedString64Bytes researchId,
        out ResearchDefinitionElement definition)
    {
        for (int i = 0; i < definitions.Length; i++)
        {
            if (!definitions[i].stableId.Equals(researchId))
                continue;

            definition = definitions[i];
            return true;
        }

        definition = default;
        return false;
    }

    public static bool TryGetDefinition(
        this NativeArray<ResearchDefinitionElement> definitions,
        FixedString64Bytes researchId,
        out ResearchDefinitionElement definition)
    {
        for (int i = 0; i < definitions.Length; i++)
        {
            if (!definitions[i].stableId.Equals(researchId))
                continue;

            definition = definitions[i];
            return true;
        }

        definition = default;
        return false;
    }

    public static int FindProgressIndex(
        this DynamicBuffer<ResearchProgressElement> progress,
        FixedString64Bytes researchId)
    {
        for (int i = 0; i < progress.Length; i++)
        {
            if (progress[i].researchId.Equals(researchId))
                return i;
        }

        return -1;
    }

    public static bool IsCompleted(
        this DynamicBuffer<ResearchProgressElement> progress,
        FixedString64Bytes researchId)
    {
        int index = progress.FindProgressIndex(researchId);
        return index >= 0 && progress[index].completed;
    }

    public static bool ArePrerequisitesCompleted(
        this DynamicBuffer<ResearchPrerequisiteElement> prerequisites,
        DynamicBuffer<ResearchProgressElement> progress,
        FixedString64Bytes researchId)
    {
        for (int i = 0; i < prerequisites.Length; i++)
        {
            ResearchPrerequisiteElement prerequisite = prerequisites[i];

            if (!prerequisite.researchId.Equals(researchId))
                continue;

            if (!progress.IsCompleted(prerequisite.prerequisiteId))
                return false;
        }

        return true;
    }

    public static bool HasIngredient(
        this DynamicBuffer<ResearchIngredientElement> ingredients,
        FixedString64Bytes researchId,
        ItemTypeEnum itemType)
    {
        for (int i = 0; i < ingredients.Length; i++)
        {
            ResearchIngredientElement ingredient = ingredients[i];

            if (ingredient.researchId.Equals(researchId) &&
                ingredient.itemType == itemType)
                return true;
        }

        return false;
    }

    public static int GetIngredientAmount(
        this DynamicBuffer<ResearchIngredientElement> ingredients,
        FixedString64Bytes researchId,
        ItemTypeEnum itemType)
    {
        for (int i = 0; i < ingredients.Length; i++)
        {
            ResearchIngredientElement ingredient = ingredients[i];

            if (ingredient.researchId.Equals(researchId) &&
                ingredient.itemType == itemType)
                return ingredient.amount;
        }

        return 0;
    }

    public static int GetIngredientAmount(
        this NativeArray<ResearchIngredientElement> ingredients,
        FixedString64Bytes researchId,
        ItemTypeEnum itemType)
    {
        for (int i = 0; i < ingredients.Length; i++)
        {
            ResearchIngredientElement ingredient = ingredients[i];

            if (ingredient.researchId.Equals(researchId) &&
                ingredient.itemType == itemType)
                return ingredient.amount;
        }

        return 0;
    }

    public static bool IsBuildingUnlocked(
        this DynamicBuffer<BuildingUnlockElement> unlocks,
        BuildingTypeEnum buildingType)
    {
        for (int i = 0; i < unlocks.Length; i++)
        {
            if (unlocks[i].buildingType == buildingType)
                return true;
        }

        return false;
    }

    public static bool IsRecipeUnlocked(
        this DynamicBuffer<RecipeUnlockElement> unlocks,
        int recipeId)
    {
        for (int i = 0; i < unlocks.Length; i++)
        {
            if (unlocks[i].recipeId == recipeId)
                return true;
        }

        return false;
    }

    public static bool IsRecipeUnlocked(
        this NativeArray<RecipeUnlockElement> unlocks,
        int recipeId)
    {
        for (int i = 0; i < unlocks.Length; i++)
        {
            if (unlocks[i].recipeId == recipeId)
                return true;
        }

        return false;
    }

    public static float GetStatMultiplier(
        this DynamicBuffer<ResearchStatModifierElement> modifiers,
        ResearchStatModifierTypeEnum type)
    {
        float bonus = 0f;

        for (int i = 0; i < modifiers.Length; i++)
        {
            if (modifiers[i].type == type)
                bonus += modifiers[i].percentBonus;
        }

        return math.max(0f, 1f + bonus);
    }

    public static float GetStatMultiplier(
        this NativeArray<ResearchStatModifierElement> modifiers,
        ResearchStatModifierTypeEnum type)
    {
        float bonus = 0f;

        for (int i = 0; i < modifiers.Length; i++)
        {
            if (modifiers[i].type == type)
                bonus += modifiers[i].percentBonus;
        }

        return math.max(0f, 1f + bonus);
    }
}
