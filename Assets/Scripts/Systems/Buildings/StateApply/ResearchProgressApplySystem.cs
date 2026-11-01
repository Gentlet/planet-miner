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
    }

    public void OnUpdate(ref SystemState state)
    {
        var end = state.World.GetOrCreateSystemManaged<EndBuildingEntityCommandBufferSystem>();
        var buildings = _buildings.ToEntityListAsync(Allocator.TempJob, state.Dependency, out var ready);
        var handle = new ApplyResearchProgressJob
        {
            Buildings = buildings.AsDeferredJobArray(), Registry = SystemAPI.GetSingletonEntity<ResearchRegistry>(),
            State = SystemAPI.GetComponentLookup<ResearchState>(true),
            Progress = SystemAPI.GetBufferLookup<ResearchProgressElement>(true),
            Definitions = SystemAPI.GetBufferLookup<ResearchDefinitionElement>(true),
            Results = SystemAPI.GetBufferLookup<ResearchContributionElement>(true),
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
    [ReadOnly] public BufferLookup<ResearchProgressElement> Progress;
    [ReadOnly] public BufferLookup<ResearchDefinitionElement> Definitions;
    [ReadOnly] public BufferLookup<ResearchContributionElement> Results;
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
        float required = 0f;
        foreach (var definition in Definitions[Registry])
        {
            if (definition.StableId == selected.ActiveResearchId)
            {
                required = definition.RequiredProgress;
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
                entry.IsCompleted = entry.Progress >= required;
                completed = entry.IsCompleted;
            }
            published.Add(entry);
        }
        if (!completed)
        {
            return;
        }
        ECB.SetComponent(Registry, default(ResearchState));
        foreach (Entity building in Buildings)
        {
            ECB.SetComponent(building, default(ResearchCycleState));
        }
    }
}
