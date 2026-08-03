public static class ResourceTypeExtension
{
    public static ItemTypeEnum ToItemType(this ResourceTypeEnum resourceType)
    {
        switch (resourceType)
        {
            case ResourceTypeEnum.Iron_Ore:
                return ItemTypeEnum.Iron_Ore;
            case ResourceTypeEnum.Copper_Ore:
                return ItemTypeEnum.Copper_Ore;
            case ResourceTypeEnum.Coal:
                return ItemTypeEnum.Coal;
            case ResourceTypeEnum.Stone:
                return ItemTypeEnum.Stone;
            default:
                return ItemTypeEnum.None;
        }
    }
}
