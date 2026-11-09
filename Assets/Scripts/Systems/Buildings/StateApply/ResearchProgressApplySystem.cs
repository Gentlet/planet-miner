using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

/// <summary>주기 결과를 전역 진척도에 한 번 합산하고 최소 완료/선택 해제/전체 주기 정리를 같은 EndBuilding에 기록한다.</summary>
[UpdateInGroup(typeof(BuildingStateApplyGroup))]
[UpdateBefore(typeof(BuildingLifecycleApplySystem))]
public partial struct ResearchProgressApplySystem : ISystem
{
    private EntityQuery _buildings;

    public void OnCreate(ref SystemState state)
    {
        _buildings = SystemAPI.QueryBuilder().WithAll<ResearchCycleState, ResearchContributionElement>()
            .WithOptions(EntityQueryOptions.IncludeDisabledEntities).Build();
        state.RequireForUpdate<ResearchRegistry>();
        state.RequireForUpdate<BuildingConfig>();
        state.RequireForUpdate<RecipeRegistry>();
        state.RequireForUpdate<ResearchStatBonuses>();
    }

    public void OnUpdate(ref SystemState state)
    {
        var end = state.World.GetOrCreateSystemManaged<EndBuildingEntityCommandBufferSystem>();
        var buildings = _buildings.ToEntityListAsync(Allocator.TempJob, state.Dependency, out var ready);
        var handle = new ApplyResearchProgressJob
        {
            Buildings = buildings.AsDeferredJobArray(), Registry = SystemAPI.GetSingletonEntity<ResearchRegistry>(),
            State = SystemAPI.GetComponentLookup<ResearchState>(true),
            ResetResults = SystemAPI.GetComponentLookup<ResearchCycleResetResult>(true),
            Progress = SystemAPI.GetBufferLookup<ResearchProgressElement>(true),
            Definitions = SystemAPI.GetBufferLookup<ResearchDefinitionElement>(true),
            Results = SystemAPI.GetBufferLookup<ResearchContributionElement>(true),
            ConfigEntity = SystemAPI.GetSingletonEntity<BuildingConfig>(),
            RecipeEntity = SystemAPI.GetSingletonEntity<RecipeRegistry>(),
            Configs = SystemAPI.GetBufferLookup<BuildingConfigElement>(true),
            Recipes = SystemAPI.GetBufferLookup<RecipeConfigElement>(true),
            Rewards = SystemAPI.GetBufferLookup<ResearchRewardElement>(true),
            Bonuses = SystemAPI.GetComponentLookup<ResearchStatBonuses>(true),
            ECB = end.CreateCommandBuffer()
        }.Schedule(JobHandle.CombineDependencies(state.Dependency, ready));
        state.Dependency = buildings.Dispose(handle);
        end.AddJobHandleForProducer(state.Dependency);
    }
}

[BurstCompile]
internal struct ApplyResearchProgressJob : IJob
{
    [ReadOnly] public NativeArray<Entity> Buildings;
    public Entity Registry;
    [ReadOnly] public ComponentLookup<ResearchState> State;
    [ReadOnly] public ComponentLookup<ResearchCycleResetResult> ResetResults;
    [ReadOnly] public BufferLookup<ResearchProgressElement> Progress;
    [ReadOnly] public BufferLookup<ResearchDefinitionElement> Definitions;
    [ReadOnly] public BufferLookup<ResearchContributionElement> Results;
    public Entity ConfigEntity;
    public Entity RecipeEntity;
    [ReadOnly] public BufferLookup<BuildingConfigElement> Configs;
    [ReadOnly] public BufferLookup<RecipeConfigElement> Recipes;
    [ReadOnly] public BufferLookup<ResearchRewardElement> Rewards;
    [ReadOnly] public ComponentLookup<ResearchStatBonuses> Bonuses;
    public EntityCommandBuffer ECB;

    public void Execute()
    {
        var selected = State[Registry];
        double contribution = 0d;
        foreach (Entity building in Buildings)
        {
            var results = Results[building];
            if (results.Length == 0)
            {
                continue;
            }
            foreach (var result in results)
            {
                if (!selected.ActiveResearchId.IsEmpty && result.ResearchId == selected.ActiveResearchId)
                {
                    contribution += result.Progress;
                }
            }
            ECB.SetBuffer<ResearchContributionElement>(building);
        }
        if (contribution == 0d)
        {
            return;
        }
        ResearchDefinitionElement activeDefinition = default;
        foreach (var definition in Definitions[Registry])
        {
            if (definition.StableId == selected.ActiveResearchId)
            {
                activeDefinition = definition;
                break;
            }
        }
        bool completed = false;
        var published = ECB.SetBuffer<ResearchProgressElement>(Registry);
        foreach (var previous in Progress[Registry])
        {
            var entry = previous;
            if (entry.ResearchId == selected.ActiveResearchId && !entry.IsCompleted)
            {
                // 틱당 기여 수는 엔티티 수로 제한된다. double 합산으로 유효한 float 기여 여러 개의 합도 보존한다.
                entry.Progress += contribution;
                entry.IsCompleted = entry.Progress >= activeDefinition.RequiredProgress;
                completed = entry.IsCompleted;
            }
            published.Add(entry);
        }
        if (!completed)
        {
            return;
        }
        // 미완료에서 완료로 바뀐 이 경로만 효과를 누적한다. 완료/해금/보너스는 같은 Playback에 공개된다.
        var rewards = Rewards[Registry];
        var bonuses = Bonuses[ConfigEntity];
        for (int i = 0; i < activeDefinition.RewardCount; i++)
        {
            var reward = rewards[activeDefinition.RewardStart + i];
            if (reward.Type == ResearchRewardTypeEnum.StatModifier)
            {
                ResearchRewardUtility.AddBonus(ref bonuses, reward);
            }
        }
        ECB.SetComponent(ConfigEntity, bonuses);
        var publishedConfigs = ECB.SetBuffer<BuildingConfigElement>(ConfigEntity);
        foreach (var previous in Configs[ConfigEntity])
        {
            var config = previous;
            // 초기화에서 보상과 종류/티어별 RequiredResearch의 양방향 일치를 검증했다.
            if (config.RequiredResearch == selected.ActiveResearchId)
            {
                config.IsUnlocked = true;
            }
            publishedConfigs.Add(config);
        }
        var publishedRecipes = ECB.SetBuffer<RecipeConfigElement>(RecipeEntity);
        foreach (var previous in Recipes[RecipeEntity])
        {
            var recipe = previous;
            recipe.IsUnlocked |= ResearchRewardUtility.UnlocksRecipe(activeDefinition, rewards, recipe.Id);
            publishedRecipes.Add(recipe);
        }
        ECB.SetComponent(Registry, default(ResearchState));
        if (ResetResults.TryGetComponent(Registry, out var notice))
        {
            notice.Revision++;
            notice.Reason = ResearchCycleResetReasonEnum.ResearchCompleted;
            ECB.SetComponent(Registry, notice);
        }
        foreach (Entity building in Buildings)
        {
            ECB.SetComponent(building, default(ResearchCycleState));
        }
    }
}
