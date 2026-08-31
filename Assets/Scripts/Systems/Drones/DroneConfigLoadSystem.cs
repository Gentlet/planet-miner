using Unity.Entities;

public partial class DroneConfigLoadSystem : SystemBase
{
    private const string droneConfigResourcePath = "Config/DroneConfig";

    protected override void OnCreate()
    {
        DroneConfig? config = LoadConfig();

        if (!config.HasValue)
        {
            Enabled = false;
            return;
        }

        Entity configEntity = EntityManager.CreateEntity();
        EntityManager.AddComponentData(configEntity, config.Value);
        Enabled = false;
    }

    protected override void OnUpdate()
    {
    }

    private static DroneConfig? LoadConfig()
    {
        if (!ConfigResourceLoader.TryLoadJson(
                "Drone",
                droneConfigResourcePath,
                out string json))
            return null;

        return DroneConfigParser.Parse(
            json,
            droneConfigResourcePath);
    }
}
