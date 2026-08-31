using Unity.Entities;

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
        if (!ConfigResourceLoader.TryLoadJson(
                "Starting item",
                startingItemConfigResourcePath,
                out string json))
            return null;

        return StartingItemConfigParser.Parse(
            json,
            startingItemConfigResourcePath);
    }
}
