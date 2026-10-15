using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

/// <summary>준비된 채굴 자격과 공급으로 실행 결정을 작성한다. 자원 탐색을 반복하지 않는다.</summary>
[UpdateInGroup(typeof(BuildingDecisionGroup))]
public partial struct MinerDecisionSystem : ISystem
{
    public void OnCreate(ref SystemState state) { }
    public void OnUpdate(ref SystemState state)
    {
        state.Dependency = new DecideMinerExecutionJob
        {
            Demolition = SystemAPI.GetComponentLookup<PendingBuildingDemolition>(true),
            Preparation = SystemAPI.GetComponentLookup<MinerWorkPreparation>(true),
            Demands = SystemAPI.GetComponentLookup<PowerDemand>(true),
            Supplies = SystemAPI.GetComponentLookup<PowerSupplyResult>(true)
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile, WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)]
internal partial struct DecideMinerExecutionJob : IJobEntity
{
    [ReadOnly] public ComponentLookup<PendingBuildingDemolition> Demolition;
    [ReadOnly] public ComponentLookup<MinerWorkPreparation> Preparation;
    [ReadOnly] public ComponentLookup<PowerDemand> Demands;
    [ReadOnly] public ComponentLookup<PowerSupplyResult> Supplies;

    private void Execute(Entity entity, ref MinerDecision decision, EnabledRefRW<MinerDecision> enabled,
        in MinerState state)
    {
        decision = default;
        enabled.ValueRW = false;
        if (Demolition.HasComponent(entity))
        {
            return;
        }
        if (!Preparation.HasComponent(entity))
        {
            return;
        }
        if (!Preparation.IsComponentEnabled(entity))
        {
            return;
        }
        var preparation = Preparation[entity];
        decision.TargetResource = preparation.TargetResource;
        bool canProgress = PowerFrameUtility.HasAllocatedPower(entity, Demands, Supplies);
        decision.CanMine = preparation.CanMine && (canProgress || state.Progress >= 1f);
        enabled.ValueRW = decision.CanMine;
    }
}
