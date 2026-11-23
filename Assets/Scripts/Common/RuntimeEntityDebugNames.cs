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
    internal static void SetBuilding(EntityCommandBuffer ecb, Entity entity, in BuildingConfigElement config)
    {
        FixedString64Bytes name = default;
        name.CopyFromTruncated(config.DisplayName);
        if (!config.Placement.BootstrapOnly)
        {
            name.Append((FixedString32Bytes)" T");
            name.Append(config.Tier);
        }
        ecb.SetName(entity, name);
    }

    [Conditional("UNITY_EDITOR")]
    internal static void SetConstructionSite(EntityCommandBuffer ecb, Entity entity, in FixedString128Bytes displayName)
    {
        var name = (FixedString64Bytes)"Site / ";
        var label = new FixedString32Bytes();
        label.CopyFromTruncated(displayName);
        name.Append(label);
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
