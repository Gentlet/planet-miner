using Unity.Collections;

public static class ItemTypeExtensions
{
    public static FixedString32Bytes ToFixedString(this ItemTypeEnum type)
    {
        switch (type)
        {
            case ItemTypeEnum.Iron_Ore: return (FixedString32Bytes)"Iron_Ore";
            case ItemTypeEnum.Copper_Ore: return (FixedString32Bytes)"Copper_Ore";
            case ItemTypeEnum.Coal: return (FixedString32Bytes)"Coal";
            case ItemTypeEnum.Stone: return (FixedString32Bytes)"Stone";
            case ItemTypeEnum.Iron: return (FixedString32Bytes)"Iron";
            case ItemTypeEnum.Copper: return (FixedString32Bytes)"Copper";
            case ItemTypeEnum.Iron_Stick: return (FixedString32Bytes)"Iron_Stick";
            case ItemTypeEnum.Copper_Stick: return (FixedString32Bytes)"Copper_Stick";
            case ItemTypeEnum.Drone: return (FixedString32Bytes)"Drone";
            default: return (FixedString32Bytes)"None";
        }
    }
}
