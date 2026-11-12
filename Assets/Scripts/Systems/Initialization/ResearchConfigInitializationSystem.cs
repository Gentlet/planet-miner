using System;
using Unity.Entities;

/// <summary>
/// 건물/품목/레시피 게시 뒤 연구 정의와 빈 전역 상태를 한 번 게시한다.
/// JSON/리소스와 관리형 그래프 검증은 초기화에서만 수행하며 ECS 버퍼 수명은 World가 소유한다.
/// 사전 등록 원본은 검증 후 보존한다. 실패는 공통 Fatal로 즉시 공개하고 자동 재시도하지 않는다.
/// </summary>
[UpdateInGroup(typeof(InitializationSystemGroup))]
[UpdateAfter(typeof(BuildingConfigInitSystem))]
[UpdateAfter(typeof(ItemConfigInitSystem))]
[UpdateAfter(typeof(RecipeInitSystem))]
public partial class ResearchConfigInitializationSystem : SystemBase
{
    protected override void OnCreate()
    {
        RequireForUpdate<BuildingConfig>();
        RequireForUpdate<ItemRegistry>();
        RequireForUpdate<RecipeRegistry>();
    }

    protected override void OnUpdate()
    {
        Initialize(EntityManager);
        Enabled = false;
    }

    public static Entity Initialize(EntityManager manager, string jsonOverride = null,
        string resourcePath = ResearchConfigLoader.DefaultResourcePath)
    {
        if (SimulationFailureUtility.HasFatalError(manager))
        {
            return Entity.Null;
        }

        try
        {
            using var query = manager.CreateEntityQuery(typeof(ResearchRegistry));
            int count = query.CalculateEntityCount();
            if (count > 1)
            {
                throw new InvalidOperationException("Research configuration has duplicate singleton state.");
            }
            if (count == 1)
            {
                Entity registered = query.GetSingletonEntity();
                ResearchConfigLoader.ValidatePublishedConfig(manager, registered);
                ResearchRewardUtility.InitializeFromProgress(manager, registered);
                return registered;
            }

            var itemIds = ItemConfigInitSystem.ReadRegisteredItemIds(manager);
            var data = jsonOverride == null
                ? ResearchConfigLoader.LoadFromResources(resourcePath, itemIds)
                : ResearchConfigLoader.ParseJson(jsonOverride, itemIds);
            return ResearchConfigLoader.PublishConfig(manager, data);
        }
        catch (Exception exception)
        {
            SimulationFailureUtility.RecordInitializationFailure(manager,
                $"[ResearchConfigInitializationSystem] {exception.Message}",
                "Research configuration initialization failed.");
            return Entity.Null;
        }
    }
}
