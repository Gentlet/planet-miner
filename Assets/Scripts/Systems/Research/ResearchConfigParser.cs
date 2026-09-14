using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

internal sealed class ResearchConfigParseResult
{
    public readonly List<ResearchDefinitionElement> Definitions = new();
    public readonly List<ResearchPrerequisiteElement> Prerequisites = new();
    public readonly List<ResearchIngredientElement> Ingredients = new();
    public readonly List<ResearchRewardElement> Rewards = new();
    public readonly List<BuildingUnlockElement> InitialBuildingUnlocks = new();
    public readonly List<RecipeUnlockElement> InitialRecipeUnlocks = new();
}

internal static class ResearchConfigParser
{
    public static ResearchConfigParseResult Parse(
        string json,
        string resourcePath,
        HashSet<int> validRecipeIds)
    {
        ResearchConfigFile file;

        try
        {
            file = JsonUtility.FromJson<ResearchConfigFile>(json);
        }
        catch (Exception exception)
        {
            Debug.LogError(
                $"Failed to parse research config. Path : Resources/{resourcePath}, Error : {exception.Message}");
            return null;
        }

        if (file == null || file.researches == null || file.researches.Count == 0)
            return LogError(resourcePath, "research list is empty");

        if (validRecipeIds == null || validRecipeIds.Count == 0)
            return LogError(resourcePath, "crafter recipe definitions are unavailable");

        Dictionary<string, ResearchConfigData> researchById = new(
            StringComparer.Ordinal);

        for (int i = 0; i < file.researches.Count; i++)
        {
            ResearchConfigData research = file.researches[i];

            if (research == null)
                return LogEntryError(resourcePath, i, "entry is null");

            if (!TryValidateFixedString64(research.stableId))
                return LogEntryError(resourcePath, i, "stableId is empty or too long");

            if (!researchById.TryAdd(research.stableId, research))
                return LogEntryError(
                    resourcePath,
                    i,
                    $"stableId '{research.stableId}' is duplicated");
        }

        if (!ValidatePrerequisiteGraph(researchById, resourcePath))
            return null;

        ResearchConfigParseResult result = new();

        if (!ParseInitialUnlocks(
                file,
                validRecipeIds,
                result,
                resourcePath))
            return null;

        HashSet<BuildingTypeEnum> rewardedBuildings = new();
        HashSet<int> rewardedRecipes = new();

        for (int i = 0; i < result.InitialBuildingUnlocks.Count; i++)
            rewardedBuildings.Add(
                result.InitialBuildingUnlocks[i].buildingType);

        for (int i = 0; i < result.InitialRecipeUnlocks.Count; i++)
            rewardedRecipes.Add(result.InitialRecipeUnlocks[i].recipeId);

        for (int i = 0; i < file.researches.Count; i++)
        {
            if (!ParseResearch(
                    file.researches[i],
                    i,
                    researchById,
                    validRecipeIds,
                    rewardedBuildings,
                    rewardedRecipes,
                    result,
                    resourcePath))
                return null;
        }

        return result;
    }

    private static bool ParseInitialUnlocks(
        ResearchConfigFile file,
        HashSet<int> validRecipeIds,
        ResearchConfigParseResult result,
        string resourcePath)
    {
        if (file.initiallyUnlockedBuildings == null)
            return LogBooleanError(resourcePath, "initial building unlock list is missing");

        if (file.initiallyUnlockedRecipeIds == null)
            return LogBooleanError(resourcePath, "initial recipe unlock list is missing");

        HashSet<BuildingTypeEnum> buildingTypes = new();

        for (int i = 0; i < file.initiallyUnlockedBuildings.Count; i++)
        {
            string value = file.initiallyUnlockedBuildings[i];

            if (!TryParseBuildingType(value, out BuildingTypeEnum buildingType))
                return LogBooleanError(
                    resourcePath,
                    $"invalid initial building type '{value}'");

            if (!buildingTypes.Add(buildingType))
                return LogBooleanError(
                    resourcePath,
                    $"duplicated initial building type '{buildingType}'");

            result.InitialBuildingUnlocks.Add(new BuildingUnlockElement
            {
                buildingType = buildingType
            });
        }

        if (!buildingTypes.Contains(BuildingTypeEnum.ResearchBuilding))
            return LogBooleanError(resourcePath, "ResearchBuilding must be initially unlocked");

        HashSet<int> recipeIds = new();

        for (int i = 0; i < file.initiallyUnlockedRecipeIds.Count; i++)
        {
            int recipeId = file.initiallyUnlockedRecipeIds[i];

            if (!validRecipeIds.Contains(recipeId))
                return LogBooleanError(
                    resourcePath,
                    $"invalid initial recipe id '{recipeId}'");

            if (!recipeIds.Add(recipeId))
                return LogBooleanError(
                    resourcePath,
                    $"duplicated initial recipe id '{recipeId}'");

            result.InitialRecipeUnlocks.Add(new RecipeUnlockElement
            {
                recipeId = recipeId
            });
        }

        return true;
    }

    private static bool ParseResearch(
        ResearchConfigData data,
        int index,
        Dictionary<string, ResearchConfigData> researchById,
        HashSet<int> validRecipeIds,
        HashSet<BuildingTypeEnum> rewardedBuildings,
        HashSet<int> rewardedRecipes,
        ResearchConfigParseResult result,
        string resourcePath)
    {
        if (!TryCreateFixedStrings(
                data,
                out FixedString64Bytes stableId,
                out FixedString128Bytes displayName,
                out FixedString512Bytes description))
            return LogBooleanEntryError(
                resourcePath,
                index,
                "display name or description is empty or too long");

        if (data.cycleDuration <= 0f ||
            data.progressPerCycle <= 0f ||
            data.requiredProgress <= 0f)
            return LogBooleanEntryError(
                resourcePath,
                index,
                "cycle and progress values must be greater than zero");

        result.Definitions.Add(new ResearchDefinitionElement
        {
            stableId = stableId,
            displayName = displayName,
            description = description,
            cycleDuration = data.cycleDuration,
            progressPerCycle = data.progressPerCycle,
            requiredProgress = data.requiredProgress
        });

        if (!ParsePrerequisites(
                data,
                stableId,
                researchById,
                result,
                resourcePath,
                index))
            return false;

        if (!ParseIngredients(data, stableId, result, resourcePath, index))
            return false;

        return ParseRewards(
            data,
            stableId,
            validRecipeIds,
            rewardedBuildings,
            rewardedRecipes,
            result,
            resourcePath,
            index);
    }

    private static bool ParsePrerequisites(
        ResearchConfigData data,
        FixedString64Bytes researchId,
        Dictionary<string, ResearchConfigData> researchById,
        ResearchConfigParseResult result,
        string resourcePath,
        int index)
    {
        if (data.prerequisiteIds == null)
            return LogBooleanEntryError(resourcePath, index, "prerequisite list is missing");

        HashSet<string> prerequisiteIds = new(StringComparer.Ordinal);

        for (int i = 0; i < data.prerequisiteIds.Count; i++)
        {
            string prerequisiteId = data.prerequisiteIds[i];

            if (!researchById.ContainsKey(prerequisiteId))
                return LogBooleanEntryError(
                    resourcePath,
                    index,
                    $"prerequisite '{prerequisiteId}' does not exist");

            if (!prerequisiteIds.Add(prerequisiteId))
                return LogBooleanEntryError(
                    resourcePath,
                    index,
                    $"prerequisite '{prerequisiteId}' is duplicated");

            result.Prerequisites.Add(new ResearchPrerequisiteElement
            {
                researchId = researchId,
                prerequisiteId = new FixedString64Bytes(prerequisiteId)
            });
        }

        return true;
    }

    private static bool ParseIngredients(
        ResearchConfigData data,
        FixedString64Bytes researchId,
        ResearchConfigParseResult result,
        string resourcePath,
        int index)
    {
        if (data.ingredients == null || data.ingredients.Count == 0)
            return LogBooleanEntryError(resourcePath, index, "ingredient list is empty");

        HashSet<ItemTypeEnum> itemTypes = new();

        for (int i = 0; i < data.ingredients.Count; i++)
        {
            ResearchIngredientConfigData ingredient = data.ingredients[i];

            if (ingredient == null)
                return LogBooleanEntryError(resourcePath, index, "ingredient is null");

            if (!TryParseItemType(ingredient.itemType, out ItemTypeEnum itemType))
                return LogBooleanEntryError(
                    resourcePath,
                    index,
                    $"invalid ingredient item type '{ingredient.itemType}'");

            if (ingredient.amount <= 0)
                return LogBooleanEntryError(
                    resourcePath,
                    index,
                    "ingredient amount must be greater than zero");

            if (!itemTypes.Add(itemType))
                return LogBooleanEntryError(
                    resourcePath,
                    index,
                    $"ingredient '{itemType}' is duplicated");

            result.Ingredients.Add(new ResearchIngredientElement
            {
                researchId = researchId,
                itemType = itemType,
                amount = ingredient.amount
            });
        }

        return true;
    }

    private static bool ParseRewards(
        ResearchConfigData data,
        FixedString64Bytes researchId,
        HashSet<int> validRecipeIds,
        HashSet<BuildingTypeEnum> rewardedBuildings,
        HashSet<int> rewardedRecipes,
        ResearchConfigParseResult result,
        string resourcePath,
        int index)
    {
        if (data.rewards == null || data.rewards.Count == 0)
            return LogBooleanEntryError(resourcePath, index, "reward list is empty");

        HashSet<ResearchStatModifierTypeEnum> localStats = new();

        for (int i = 0; i < data.rewards.Count; i++)
        {
            ResearchRewardConfigData reward = data.rewards[i];

            if (reward == null ||
                !Enum.TryParse(reward.type, true, out ResearchRewardTypeEnum rewardType) ||
                rewardType >= ResearchRewardTypeEnum.Count)
                return LogBooleanEntryError(
                    resourcePath,
                    index,
                    $"invalid reward type '{reward?.type}'");

            ResearchRewardElement element = new()
            {
                researchId = researchId,
                type = rewardType
            };

            switch (rewardType)
            {
                case ResearchRewardTypeEnum.BuildingUnlock:
                    if (!TryParseBuildingType(
                            reward.buildingType,
                            out element.buildingType))
                        return LogBooleanEntryError(
                            resourcePath,
                            index,
                            $"invalid building reward target '{reward.buildingType}'");

                    if (!rewardedBuildings.Add(element.buildingType))
                        return LogBooleanEntryError(
                            resourcePath,
                            index,
                            $"building reward target '{element.buildingType}' is duplicated");
                    break;

                case ResearchRewardTypeEnum.RecipeUnlock:
                    if (!validRecipeIds.Contains(reward.recipeId))
                        return LogBooleanEntryError(
                            resourcePath,
                            index,
                            $"invalid recipe reward target '{reward.recipeId}'");

                    if (!rewardedRecipes.Add(reward.recipeId))
                        return LogBooleanEntryError(
                            resourcePath,
                            index,
                            $"recipe reward target '{reward.recipeId}' is duplicated");

                    element.recipeId = reward.recipeId;
                    break;

                case ResearchRewardTypeEnum.StatModifier:
                    if (!Enum.TryParse(
                            reward.statType,
                            true,
                            out element.statType) ||
                        element.statType >= ResearchStatModifierTypeEnum.Count)
                        return LogBooleanEntryError(
                            resourcePath,
                            index,
                            $"invalid stat reward target '{reward.statType}'");

                    if (reward.percentBonus <= 0f)
                        return LogBooleanEntryError(
                            resourcePath,
                            index,
                            "stat bonus must be greater than zero");

                    if (!localStats.Add(element.statType))
                        return LogBooleanEntryError(
                            resourcePath,
                            index,
                            $"stat reward target '{element.statType}' is duplicated in one research");

                    element.percentBonus = reward.percentBonus;
                    break;
            }

            result.Rewards.Add(element);
        }

        return true;
    }

    private static bool ValidatePrerequisiteGraph(
        Dictionary<string, ResearchConfigData> researchById,
        string resourcePath)
    {
        Dictionary<string, byte> visitState = new(StringComparer.Ordinal);

        foreach (string researchId in researchById.Keys)
        {
            if (!VisitResearch(
                    researchId,
                    researchById,
                    visitState,
                    out string invalidReference))
            {
                Debug.LogError(
                    $"Invalid research prerequisite graph. Path : Resources/{resourcePath}, Research : {invalidReference}");
                return false;
            }
        }

        return true;
    }

    private static bool VisitResearch(
        string researchId,
        Dictionary<string, ResearchConfigData> researchById,
        Dictionary<string, byte> visitState,
        out string invalidReference)
    {
        invalidReference = researchId;

        if (visitState.TryGetValue(researchId, out byte state))
            return state == 2;

        if (!researchById.TryGetValue(researchId, out ResearchConfigData research))
            return false;

        visitState[researchId] = 1;

        if (research.prerequisiteIds == null)
            return false;

        for (int i = 0; i < research.prerequisiteIds.Count; i++)
        {
            string prerequisiteId = research.prerequisiteIds[i];

            if (visitState.TryGetValue(prerequisiteId, out byte prerequisiteState) &&
                prerequisiteState == 1)
            {
                invalidReference = prerequisiteId;
                return false;
            }

            if (!VisitResearch(
                    prerequisiteId,
                    researchById,
                    visitState,
                    out invalidReference))
                return false;
        }

        visitState[researchId] = 2;
        return true;
    }

    private static bool TryCreateFixedStrings(
        ResearchConfigData data,
        out FixedString64Bytes stableId,
        out FixedString128Bytes displayName,
        out FixedString512Bytes description)
    {
        stableId = default;
        displayName = default;
        description = default;

        if (!TryValidateFixedString64(data.stableId) ||
            string.IsNullOrWhiteSpace(data.displayName) ||
            string.IsNullOrWhiteSpace(data.description))
            return false;

        try
        {
            stableId = new FixedString64Bytes(data.stableId);
            displayName = new FixedString128Bytes(data.displayName);
            description = new FixedString512Bytes(data.description);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool TryValidateFixedString64(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        try
        {
            _ = new FixedString64Bytes(value);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool TryParseBuildingType(
        string value,
        out BuildingTypeEnum buildingType)
    {
        buildingType = default;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        return Enum.TryParse(value, true, out buildingType) &&
               buildingType < BuildingTypeEnum.Count &&
               buildingType != BuildingTypeEnum.MainFacility;
    }

    private static bool TryParseItemType(
        string value,
        out ItemTypeEnum itemType)
    {
        itemType = default;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        return Enum.TryParse(value, true, out itemType) && itemType.IsValid();
    }

    private static ResearchConfigParseResult LogError(
        string resourcePath,
        string reason)
    {
        Debug.LogError(
            $"Invalid research config. Path : Resources/{resourcePath}, Reason : {reason}");
        return null;
    }

    private static ResearchConfigParseResult LogEntryError(
        string resourcePath,
        int index,
        string reason)
    {
        Debug.LogError(
            $"Invalid research config entry. Path : Resources/{resourcePath}, Index : {index}, Reason : {reason}");
        return null;
    }

    private static bool LogBooleanError(string resourcePath, string reason)
    {
        LogError(resourcePath, reason);
        return false;
    }

    private static bool LogBooleanEntryError(
        string resourcePath,
        int index,
        string reason)
    {
        LogEntryError(resourcePath, index, reason);
        return false;
    }
}
