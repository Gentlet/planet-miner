using Unity.Entities;

public static class ResearchRewardUtility
{
    public static float EffectiveSpeed(float baseSpeed, double bonus)
    {
        return (float)(baseSpeed * (1d + bonus));
    }

    public static void AddBonus(ref ResearchStatBonuses bonuses, in ResearchRewardElement reward)
    {
        switch (reward.StatKey)
        {
            case ResearchStatKeyEnum.MiningSpeed: bonuses.MiningSpeed += reward.Bonus; break;
            case ResearchStatKeyEnum.CraftingSpeed: bonuses.CraftingSpeed += reward.Bonus; break;
            case ResearchStatKeyEnum.BeltSpeed: bonuses.BeltSpeed += reward.Bonus; break;
            case ResearchStatKeyEnum.ResearchSpeed: bonuses.ResearchSpeed += reward.Bonus; break;
        }
    }

    public static bool UnlocksRecipe(in ResearchDefinitionElement definition,
        DynamicBuffer<ResearchRewardElement> rewards, int recipeId)
    {
        for (int i = 0; i < definition.RewardCount; i++)
        {
            var reward = rewards[definition.RewardStart + i];
            if (reward.Type == ResearchRewardTypeEnum.RecipeUnlock && reward.RecipeId == recipeId)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>검증된 완료 원본에서 최종 효과를 재구성한다. 재초기화가 보너스를 누적하거나 진척도/정의를 덮어쓰지 않는다.</summary>
    public static void InitializeFromProgress(EntityManager manager, Entity registry)
    {
        using var buildingQuery = manager.CreateEntityQuery(typeof(BuildingConfig));
        using var recipeQuery = manager.CreateEntityQuery(typeof(RecipeRegistry));
        Entity buildingsEntity = buildingQuery.GetSingletonEntity();
        Entity recipesEntity = recipeQuery.GetSingletonEntity();
        var definitions = manager.GetBuffer<ResearchDefinitionElement>(registry, true);
        var rewards = manager.GetBuffer<ResearchRewardElement>(registry, true);
        var progress = manager.GetBuffer<ResearchProgressElement>(registry, true);
        var initialRecipes = manager.GetBuffer<ResearchInitialRecipeUnlockElement>(registry, true);
        var buildings = manager.GetBuffer<BuildingConfigElement>(buildingsEntity);
        var recipes = manager.GetBuffer<RecipeConfigElement>(recipesEntity);
        ResearchStatBonuses bonuses = default;

        for (int i = 0; i < recipes.Length; i++)
        {
            var recipe = recipes[i];
            recipe.IsUnlocked = false;
            foreach (var initial in initialRecipes)
            {
                recipe.IsUnlocked |= initial.RecipeId == recipe.Id;
            }
            recipes[i] = recipe;
        }
        foreach (var definition in definitions)
        {
            bool completed = false;
            foreach (var entry in progress)
            {
                if (entry.ResearchId == definition.StableId)
                {
                    completed = entry.IsCompleted;
                    break;
                }
            }
            if (!completed)
            {
                continue;
            }
            for (int i = 0; i < definition.RewardCount; i++)
            {
                var reward = rewards[definition.RewardStart + i];
                if (reward.Type == ResearchRewardTypeEnum.StatModifier)
                {
                    AddBonus(ref bonuses, reward);
                }
            }
            for (int i = 0; i < buildings.Length; i++)
            {
                var building = buildings[i];
                if (building.RequiredResearch == definition.StableId)
                {
                    building.IsUnlocked = true;
                    buildings[i] = building;
                }
            }
            for (int i = 0; i < recipes.Length; i++)
            {
                var recipe = recipes[i];
                recipe.IsUnlocked |= UnlocksRecipe(definition, rewards, recipe.Id);
                recipes[i] = recipe;
            }
        }
        manager.SetComponentData(buildingsEntity, bonuses);
    }
}
