using Unity.Entities;
using UnityEngine;

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
        TextAsset configAsset = Resources.Load<TextAsset>(droneConfigResourcePath);

        if (configAsset == null)
        {
            Debug.LogError(
                $"Drone config file not found. Path : Resources/{droneConfigResourcePath}");
            return null;
        }

        return DroneConfigParser.Parse(
            configAsset.text,
            droneConfigResourcePath);
    }
}
