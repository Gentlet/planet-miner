using Unity.Entities;
using UnityEngine;

public partial class StartingItemConfigLoadSystem : SystemBase
{
    private const string startingItemConfigResourcePath = "Config/StartingItemConfig";

    protected override void OnCreate()
    {
        StartingItemConfigElement[] config = LoadConfig();

        if (config == null)
        {
            Enabled = false;
            return;
        }

        Entity configEntity = EntityManager.CreateEntity(
            typeof(StartingItemConfigElement));
        DynamicBuffer<StartingItemConfigElement> itemConfigs = EntityManager
            .GetBuffer<StartingItemConfigElement>(configEntity);

        for (int i = 0; i < config.Length; i++)
            itemConfigs.Add(config[i]);
        Enabled = false;
    }

    protected override void OnUpdate()
    {
    }

    private static StartingItemConfigElement[] LoadConfig()
    {
        TextAsset configAsset = Resources.Load<TextAsset>(
            startingItemConfigResourcePath);

        if (configAsset == null)
        {
            Debug.LogError(
                $"Starting item config file not found. Path : Resources/{startingItemConfigResourcePath}");
            return null;
        }

        return StartingItemConfigParser.Parse(
            configAsset.text,
            startingItemConfigResourcePath);
    }
}
