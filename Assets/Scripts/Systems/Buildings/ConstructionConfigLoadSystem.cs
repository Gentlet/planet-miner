using System.Collections.Generic;
using Unity.Entities;

[UpdateBefore(typeof(ConstructionSiteCreationSystem))]
public partial class ConstructionConfigLoadSystem : SystemBase
{
    private const string ConfigResourcePath = "Config/ConstructionConfig";

    protected override void OnCreate()
    {
        if (!ConfigResourceLoader.TryLoadJson(
                "Construction",
                ConfigResourcePath,
                out string json))
        {
            Enabled = false;
            return;
        }

        List<ConstructionMaterialConfigElement> materials =
            ConstructionConfigParser.Parse(json, ConfigResourcePath);

        if (materials == null)
        {
            Enabled = false;
            return;
        }

        Entity configEntity = EntityManager.CreateEntity(
            typeof(ConstructionConfig),
            typeof(ConstructionMaterialConfigElement));
        DynamicBuffer<ConstructionMaterialConfigElement> buffer =
            EntityManager.GetBuffer<ConstructionMaterialConfigElement>(configEntity);

        for (int i = 0; i < materials.Count; i++)
            buffer.Add(materials[i]);

        Enabled = false;
    }

    protected override void OnUpdate()
    {
    }
}
