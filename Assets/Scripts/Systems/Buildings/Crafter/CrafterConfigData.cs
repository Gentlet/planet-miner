using System;
using System.Collections.Generic;

[Serializable]
internal class CrafterRecipeConfigFile
{
    public List<CrafterRecipeConfigData> recipes = new List<CrafterRecipeConfigData>();
}

[Serializable]
internal class CrafterRecipeConfigData
{
    public int id;
    public string outputItemType;
    public float craftTime;
    public List<string> conditions = new List<string>();
    public List<CrafterRecipeIngredientConfigData> ingredients = new List<CrafterRecipeIngredientConfigData>();
}

[Serializable]
internal class CrafterRecipeIngredientConfigData
{
    public string itemType;
    public int amount;
}

[Serializable]
internal class ItemStorageLimitConfigFile
{
    public List<ItemStorageLimitConfigData> limits = new List<ItemStorageLimitConfigData>();
}

[Serializable]
internal class ItemStorageLimitConfigData
{
    public string itemType;
    public int maxAmount;
}
