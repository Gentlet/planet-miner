using System;
using Unity.Entities;

/// <summary>필수 초기화가 끝난 뒤 생성/지급을 한 번 접수한다. 실제 공개는 기존 EndBuilding 소유자에게 맡긴다.</summary>
[UpdateInGroup(typeof(InitializationSystemGroup), OrderLast = true)]
[UpdateAfter(typeof(PrefabDatabaseInitializationSystem))]
[UpdateAfter(typeof(StartingItemConfigInitializationSystem))]
[UpdateAfter(typeof(DroneConfigInitializationSystem))]
public partial class GameBootstrapInitializationSystem : SystemBase
{
    protected override void OnUpdate()
    {
        if (SimulationFailureUtility.HasFatalError(EntityManager))
        {
            Enabled = false;
            return;
        }

        if (!SystemAPI.HasSingleton<PrefabDatabaseReady>() ||
            !SystemAPI.HasSingleton<BuildingConfig>() || !SystemAPI.HasSingleton<ItemRegistry>() ||
            !SystemAPI.HasSingleton<RecipeRegistry>() || !SystemAPI.HasSingleton<ResourceGenerationSettings>() ||
            !SystemAPI.HasSingleton<StartingItemConfig>() || !SystemAPI.HasSingleton<DroneCapacityState>() ||
            !SystemAPI.HasSingleton<DroneBatteryConfig>() || !SystemAPI.HasSingleton<DroneMovementConfig>() ||
            !SystemAPI.HasSingleton<DroneChargingConfig>())
        {
            return;
        }

        try
        {
            using var requests = EntityManager.CreateEntityQuery(typeof(GameBootstrapRequest));
            if (requests.IsEmptyIgnoreFilter)
            {
                using var records = EntityManager.CreateEntityQuery(typeof(GameBootstrapState));
                PlacementStamp stamp = records.IsEmptyIgnoreFilter
                    ? BuildingPlacementRequestUtility.ReserveSpawnStamp(EntityManager) : default;
                Entity request = EntityManager.CreateEntity(typeof(GameBootstrapRequest));
                EntityManager.SetComponentData(request, new GameBootstrapRequest { Stamp = stamp });
            }
        }
        catch (Exception exception)
        {
            SimulationFailureUtility.RecordInitializationFailure(EntityManager,
                $"[GameBootstrapInitializationSystem] {exception.Message}", "Game bootstrap initialization failed.");
        }

        Enabled = false;
    }
}
