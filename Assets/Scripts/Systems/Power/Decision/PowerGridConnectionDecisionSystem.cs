using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

/// <summary>
/// 활성 일반 참가자가 회전 footprint 전체에서 공급받을 전신주를 매 틱 선택한다.
/// 공개 연결/공간 캐시를 복제하지 않고 선택에 필요한 생존 ECS만 읽는다.
/// 생성된 구성과 설정을 사용하며 토폴로지 준비 여부는 후속 Reservation/Apply가 확인한다.
/// </summary>
[UpdateInGroup(typeof(PowerDecisionGroup))]
public partial struct PowerGridConnectionDecisionSystem : ISystem
{
    private EntityQuery _participantQuery;
    private EntityQuery _poleQuery;

    public void OnCreate(ref SystemState state)
    {
        _participantQuery = SystemAPI.QueryBuilder().WithAll<PowerGridConnectionDecision, GridPosition>()
            .WithAll<BuildingFootprint, Direction>()
            .WithNone<PowerPole, PendingBuildingDemolition>()
            .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).Build();
        _poleQuery = SystemAPI.QueryBuilder().WithAll<PowerPole, BuildingType, GridPosition>()
            .WithNone<PendingBuildingDemolition>().Build();
    }

    public void OnUpdate(ref SystemState state)
    {
        var validationOwner = state.World.GetOrCreateSystemManaged<PowerValidationInitializationSystem>();
        var dependency = validationOwner.GetDependency(state.Dependency);
        dependency = new ResetPowerGridConnectionDecisionJob().ScheduleParallel(dependency);

        Entity configEntity = SystemAPI.TryGetSingletonEntity<BuildingConfig>(out var config) ? config : Entity.Null;
        var poles = _poleQuery.ToEntityListAsync(Allocator.TempJob, dependency, out var polesReady);
        var selection = new SelectPowerGridConnectionJob
        {
            Validation = validationOwner.Validation, Poles = poles.AsDeferredJobArray(), ConfigEntity = configEntity,
            ConfigLookup = SystemAPI.GetBufferLookup<BuildingConfigElement>(true),
            PositionLookup = SystemAPI.GetComponentLookup<GridPosition>(true),
            TypeLookup = SystemAPI.GetComponentLookup<BuildingType>(true),
            StampLookup = SystemAPI.GetComponentLookup<PlacementStamp>(true)
        }.ScheduleParallel(_participantQuery, JobHandle.CombineDependencies(polesReady, state.Dependency));
        state.Dependency = poles.Dispose(selection);
        validationOwner.Register(state.Dependency);
    }

}

[BurstCompile]
internal partial struct SelectPowerGridConnectionJob : IJobEntity
{
    [ReadOnly] public NativeReference<PowerValidationResult> Validation;
    [ReadOnly] public NativeArray<Entity> Poles;
    public Entity ConfigEntity;
    [ReadOnly] public BufferLookup<BuildingConfigElement> ConfigLookup;
    [ReadOnly] public ComponentLookup<GridPosition> PositionLookup;
    [ReadOnly] public ComponentLookup<BuildingType> TypeLookup;
    [ReadOnly] public ComponentLookup<PlacementStamp> StampLookup;

    private void Execute(ref PowerGridConnectionDecision decision,
        EnabledRefRW<PowerGridConnectionDecision> prepared, in GridPosition position,
        in BuildingFootprint footprint, in Direction direction)
    {
        if (Validation.Value.HasError)
        {
            return;
        }
        var configs = ConfigLookup[ConfigEntity];
        var effectiveSize = footprint.GetEffectiveSize(direction.dir);
        Entity selectedPole = Entity.Null;
        long selectedDistance = long.MaxValue;
        for (int i = 0; i < Poles.Length; i++)
        {
            Entity pole = Poles[i];
            var type = TypeLookup[pole];
            if (!BuildingConfigLookupUtility.TryGetConfig(configs, type.Type, type.Tier, out var config))
            {
                return;
            }
            var polePosition = PositionLookup[pole].Value;
            if (!PowerRangeUtility.TryGetFootprintDistanceSquared(polePosition, config.Power.SupplyRange,
                    position.Value, effectiveSize, out long distance))
            {
                continue;
            }
            if (selectedPole == Entity.Null)
            {
                selectedPole = pole;
                selectedDistance = distance;
                continue;
            }
            if (distance > selectedDistance)
            {
                continue;
            }
            if (distance == selectedDistance)
            {
                bool hasStamp = StampLookup.HasComponent(pole);
                bool hasSelectedStamp = StampLookup.HasComponent(selectedPole);
                var stamp = hasStamp ? StampLookup[pole] : default;
                var selectedStamp = hasSelectedStamp ? StampLookup[selectedPole] : default;
                if (PlacementStamp.Compare(hasStamp, stamp, polePosition,
                        hasSelectedStamp, selectedStamp, PositionLookup[selectedPole].Value) >= 0)
                {
                    continue;
                }
            }
            selectedPole = pole;
            selectedDistance = distance;
        }
        decision.PowerPole = selectedPole;
        prepared.ValueRW = true;
    }
}
