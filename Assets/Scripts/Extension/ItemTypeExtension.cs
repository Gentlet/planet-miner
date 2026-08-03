public static class ItemTypeExtension
{
    public static bool IsValid(this ItemTypeEnum type)
    {
        return type > ItemTypeEnum.None && type < ItemTypeEnum.Count;
    }
}
