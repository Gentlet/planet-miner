using Unity.Entities;
using UnityEngine;

public partial class PowerConfigLoadSystem : SystemBase
{
    private const string powerConfigResourcePath = "Config/PowerConfig";

    protected override void OnCreate()
    {
        PowerConfigParseResult result = LoadConfig();

        if (result == null)
        {
            Enabled = false;
            return;
        }

        Entity configEntity = EntityManager.CreateEntity(
            typeof(PowerConfig),
            typeof(CoalGeneratorConfig),
            typeof(PowerPoleConfigElement),
            typeof(PowerGeneratorConfigElement),
            typeof(PowerConsumerConfigElement));

        PublishConfig(configEntity, result);
        Enabled = false;
    }

    protected override void OnUpdate()
    {
    }

    private static PowerConfigParseResult LoadConfig()
    {
        TextAsset configAsset = Resources.Load<TextAsset>(powerConfigResourcePath);

        if (configAsset == null)
        {
            Debug.LogError($"Power config file not found. Path : Resources/{powerConfigResourcePath}");
            return null;
        }

        return PowerConfigParser.Parse(
            configAsset.text,
            powerConfigResourcePath);
    }

    private void PublishConfig(Entity configEntity, PowerConfigParseResult result)
    {
        EntityManager.SetComponentData(configEntity, result.CoalGenerator);

        DynamicBuffer<PowerPoleConfigElement> powerPoles =
            EntityManager.GetBuffer<PowerPoleConfigElement>(configEntity);
        DynamicBuffer<PowerGeneratorConfigElement> generators =
            EntityManager.GetBuffer<PowerGeneratorConfigElement>(configEntity);
        DynamicBuffer<PowerConsumerConfigElement> consumers =
            EntityManager.GetBuffer<PowerConsumerConfigElement>(configEntity);

        for (int i = 0; i < result.PowerPoles.Count; i++)
            powerPoles.Add(result.PowerPoles[i]);

        for (int i = 0; i < result.Generators.Count; i++)
            generators.Add(result.Generators[i]);

        for (int i = 0; i < result.Consumers.Count; i++)
            consumers.Add(result.Consumers[i]);
    }
}
