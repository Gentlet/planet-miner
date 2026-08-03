public static class CrafterStateExtension
{
    public static bool CanReceiveItems(this CrafterStateEnum state)
    {
        return state != CrafterStateEnum.NoRecipe;
    }
}
