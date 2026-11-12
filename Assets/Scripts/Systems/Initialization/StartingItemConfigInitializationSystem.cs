using System;
using Unity.Entities;

/// <summary>초기 지급 정의를 한 번 검증/게시한다. 프리팹 준비나 실제 지급은 수행하지 않는다.</summary>
[UpdateInGroup(typeof(InitializationSystemGroup))]
[UpdateAfter(typeof(ItemConfigInitSystem))]
public partial class StartingItemConfigInitializationSystem : SystemBase
{
    protected override void OnUpdate()
    {
        Initialize(EntityManager);
        Enabled = false;
    }

    public static Entity Initialize(EntityManager manager, string jsonOverride = null,
        string resourcePath = StartingItemConfigLoader.DefaultResourcePath)
    {
        if (SimulationFailureUtility.HasFatalError(manager))
        {
            return Entity.Null;
        }

        try
        {
            using var query = manager.CreateEntityQuery(typeof(StartingItemConfig));
            int count = query.CalculateEntityCount();
            if (count > 1)
            {
                throw new InvalidOperationException("Starting item configuration has duplicate singleton state.");
            }

            if (count == 1)
            {
                Entity registered = query.GetSingletonEntity();
                if (!manager.HasBuffer<StartingItemConfigElement>(registered))
                {
                    throw new InvalidOperationException("The pre-registered starting item configuration requires its definition buffer.");
                }

                StartingItemConfigLoader.ValidatePublishedConfig(manager.GetBuffer<StartingItemConfigElement>(registered, true));
                return registered;
            }

            var itemIds = ItemConfigInitSystem.ReadRegisteredItemIds(manager);
            var definitions = jsonOverride == null
                ? StartingItemConfigLoader.LoadFromResources(resourcePath, itemIds)
                : StartingItemConfigLoader.ParseJson(jsonOverride, itemIds);
            return StartingItemConfigLoader.PublishConfig(manager, definitions);
        }
        catch (Exception exception)
        {
            SimulationFailureUtility.RecordInitializationFailure(manager,
                $"[StartingItemConfigInitializationSystem] {exception.Message}",
                "Starting item configuration initialization failed.");
            return Entity.Null;
        }
    }
}
