using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

[UpdateBefore(typeof(ConstructionSiteCreationSystem))]
public partial class ConstructionConfigLoadSystem : SystemBase
{
    private const string ConfigResourcePath = "Config/ConstructionConfig";

    protected override void OnCreate()
    {
        TextAsset configAsset = Resources.Load<TextAsset>(ConfigResourcePath);

        if (configAsset == null)
        {
            Debug.LogError(
                $"Construction config file not found. Path : Resources/{ConfigResourcePath}");
            Enabled = false;
            return;
        }

        List<ConstructionMaterialConfigElement> materials =
            ConstructionConfigParser.Parse(configAsset.text, ConfigResourcePath);

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
