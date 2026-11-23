using System.Diagnostics;
using Unity.Collections;
using Unity.Entities;

// Unity Editor는 고유 이름을 누적하므로 같은 종류의 엔티티는 이름을 공유한다.
// 좌표와 작업 순번은 Inspector에서 원본 컴포넌트로 확인한다.
internal static class RuntimeEntityDebugNames
{
    [Conditional("UNITY_EDITOR")]
    internal static void SetConfig(EntityManager manager, Entity entity, FixedString64Bytes name)
    {
#if UNITY_EDITOR
        manager.SetName(entity, name);
        manager.AddComponent<ConfigDataDebugTag>(entity);
#endif
    }

    [Conditional("UNITY_EDITOR")]
    internal static void SetBuilding(EntityCommandBuffer ecb, Entity entity, BuildingTypeEnum type, int tier)
    {
        FixedString64Bytes name = GetBuildingName(type);
        if (type != BuildingTypeEnum.MainFacility)
        {
            name.Append((FixedString32Bytes)" T");
            name.Append(tier);
        }
        ecb.SetName(entity, name);
    }

    [Conditional("UNITY_EDITOR")]
    internal static void SetConstructionSite(EntityCommandBuffer ecb, Entity entity, BuildingTypeEnum targetType)
    {
        var name = (FixedString64Bytes)"Site / ";
        name.Append(GetBuildingName(targetType));
        ecb.SetName(entity, name);
    }

    [Conditional("UNITY_EDITOR")]
    internal static void SetItem(EntityCommandBuffer ecb, Entity entity, ItemTypeEnum type)
    {
        var name = (FixedString64Bytes)"Item / ";
        name.Append(type.ToFixedString());
        ecb.SetName(entity, name);
    }

    [Conditional("UNITY_EDITOR")]
    internal static void SetResource(EntityCommandBuffer ecb, Entity entity, ItemTypeEnum type)
    {
        var name = (FixedString64Bytes)"Resource / ";
        name.Append(type.ToFixedString());
        ecb.SetName(entity, name);
    }

    [Conditional("UNITY_EDITOR")]
    internal static void SetDrone(EntityCommandBuffer ecb, Entity entity)
    {
        ecb.SetName(entity, (FixedString64Bytes)"Drone / Worker");
    }

    [Conditional("UNITY_EDITOR")]
    internal static void SetDroneTask(EntityCommandBuffer ecb, Entity entity, DroneLogisticsTaskKindEnum kind)
    {
        var name = (FixedString64Bytes)"Task / ";
        name.Append(GetDroneTaskName(kind));
        ecb.SetName(entity, name);
    }

    private static FixedString64Bytes GetBuildingName(BuildingTypeEnum type)
    {
        switch (type)
        {
            case BuildingTypeEnum.Storage: return (FixedString64Bytes)"Storage";
            case BuildingTypeEnum.Miner: return (FixedString64Bytes)"Miner";
            case BuildingTypeEnum.Crafter: return (FixedString64Bytes)"Crafter";
            case BuildingTypeEnum.PowerPole: return (FixedString64Bytes)"Power Pole";
            case BuildingTypeEnum.CoalGenerator: return (FixedString64Bytes)"Coal Generator";
            case BuildingTypeEnum.ResearchBuilding: return (FixedString64Bytes)"Research Building";
            case BuildingTypeEnum.MainFacility: return (FixedString64Bytes)"Main Facility";
            case BuildingTypeEnum.DroneStation: return (FixedString64Bytes)"Drone Station";
            case BuildingTypeEnum.Splitter: return (FixedString64Bytes)"Splitter";
            case BuildingTypeEnum.Merger: return (FixedString64Bytes)"Merger";
            case BuildingTypeEnum.Belt: return (FixedString64Bytes)"Belt";
            default: return (FixedString64Bytes)"Building";
        }
    }

    private static FixedString32Bytes GetDroneTaskName(DroneLogisticsTaskKindEnum kind)
    {
        switch (kind)
        {
            case DroneLogisticsTaskKindEnum.ConstructionSupply: return (FixedString32Bytes)"ConstructionSupply";
            case DroneLogisticsTaskKindEnum.WorldItemRecovery: return (FixedString32Bytes)"WorldItemRecovery";
            case DroneLogisticsTaskKindEnum.StorageImport: return (FixedString32Bytes)"StorageImport";
            case DroneLogisticsTaskKindEnum.StorageExport: return (FixedString32Bytes)"StorageExport";
            case DroneLogisticsTaskKindEnum.BuildingDemolition: return (FixedString32Bytes)"BuildingDemolition";
            default: return (FixedString32Bytes)"Unknown";
        }
    }
}

#if UNITY_EDITOR
internal struct ConfigDataDebugTag : IComponentData
{
}
#endif
