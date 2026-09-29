using Unity.Collections;

public static class BuildingTypeExtensions
{
    public static FixedString32Bytes ToFixedString(this BuildingTypeEnum type)
    {
        switch (type)
        {
            case BuildingTypeEnum.Storage: return (FixedString32Bytes)"Storage";
            case BuildingTypeEnum.Miner: return (FixedString32Bytes)"Miner";
            case BuildingTypeEnum.Crafter: return (FixedString32Bytes)"Crafter";
            case BuildingTypeEnum.PowerPole: return (FixedString32Bytes)"PowerPole";
            case BuildingTypeEnum.CoalGenerator: return (FixedString32Bytes)"CoalGenerator";
            case BuildingTypeEnum.ResearchBuilding: return (FixedString32Bytes)"ResearchBuilding";
            case BuildingTypeEnum.MainFacility: return (FixedString32Bytes)"MainFacility";
            case BuildingTypeEnum.DroneStation: return (FixedString32Bytes)"DroneStation";
            case BuildingTypeEnum.Splitter: return (FixedString32Bytes)"Splitter";
            case BuildingTypeEnum.Merger: return (FixedString32Bytes)"Merger";
            case BuildingTypeEnum.Belt: return (FixedString32Bytes)"Belt";
            case BuildingTypeEnum.ConstructionSite: return (FixedString32Bytes)"ConstructionSite";
            default: return (FixedString32Bytes)"None";
        }
    }
}
