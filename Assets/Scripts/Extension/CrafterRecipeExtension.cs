public static class CrafterRecipeExtension
{
    public static float GetCraftTime(this CrafterRecipeElement recipe, float speed)
    {
        if (speed <= 0f)
            return float.PositiveInfinity;

        return recipe.craftTime / speed;
    }
}
