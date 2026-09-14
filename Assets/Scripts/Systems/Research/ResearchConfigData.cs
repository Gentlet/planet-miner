using System;
using System.Collections.Generic;

[Serializable]
internal sealed class ResearchConfigFile
{
    public List<string> initiallyUnlockedBuildings = new();
    public List<int> initiallyUnlockedRecipeIds = new();
    public List<ResearchConfigData> researches = new();
}

[Serializable]
internal sealed class ResearchConfigData
{
    public string stableId;
    public string displayName;
    public string description;
    public List<string> prerequisiteIds = new();
    public List<ResearchIngredientConfigData> ingredients = new();
    public float cycleDuration;
    public float progressPerCycle;
    public float requiredProgress;
    public List<ResearchRewardConfigData> rewards = new();
}

[Serializable]
internal sealed class ResearchIngredientConfigData
{
    public string itemType;
    public int amount;
}

[Serializable]
internal sealed class ResearchRewardConfigData
{
    public string type;
    public string buildingType;
    public int recipeId;
    public string statType;
    public float percentBonus;
}
