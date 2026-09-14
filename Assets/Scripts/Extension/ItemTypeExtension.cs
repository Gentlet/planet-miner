public static class ItemTypeExtension
{
    public static string GetDisplayName(this ItemTypeEnum itemType)
    {
        return itemType switch
        {
            ItemTypeEnum.Iron_Ore => "철 광석",
            ItemTypeEnum.Copper_Ore => "구리 광석",
            ItemTypeEnum.Coal => "석탄",
            ItemTypeEnum.Stone => "돌",
            ItemTypeEnum.Iron => "철",
            ItemTypeEnum.Copper => "구리",
            ItemTypeEnum.Iron_Stick => "철 막대",
            ItemTypeEnum.Copper_Stick => "구리 막대",
            ItemTypeEnum.Drone => "드론",
            _ => "없음"
        };
    }

    public static bool IsValid(this ItemTypeEnum type)
    {
        return type > ItemTypeEnum.None && type < ItemTypeEnum.Count;
    }
}
