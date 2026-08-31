using System.Collections.Generic;
using Unity.Entities;

[UpdateBefore(typeof(BuildingSpawnSystem))]
public partial class BuildingRuntimeConfigLoadSystem : SystemBase
{
    private const string ConfigResourcePath = "Config/BuildingRuntimeConfig";

    protected override void OnCreate()
    {
        if (!ConfigResourceLoader.TryLoadJson(
                "Building runtime",
                ConfigResourcePath,
                out string json))
        {
            Enabled = false;
            return;
        }

        List<BuildingRuntimeConfigElement> configs =
            BuildingRuntimeConfigParser.Parse(json, ConfigResourcePath);

        if (configs == null)
        {
            Enabled = false;
            return;
        }

        Entity configEntity = EntityManager.CreateEntity(
            typeof(BuildingRuntimeConfig),
            typeof(BuildingRuntimeConfigElement));
        DynamicBuffer<BuildingRuntimeConfigElement> buffer =
            EntityManager.GetBuffer<BuildingRuntimeConfigElement>(
                configEntity);

        for (int i = 0; i < configs.Count; i++)
            buffer.Add(configs[i]);

        Enabled = false;
    }

    protected override void OnUpdate()
    {
    }
}
