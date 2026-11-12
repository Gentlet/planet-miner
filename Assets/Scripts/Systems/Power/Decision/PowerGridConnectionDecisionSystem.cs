using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
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
        var inputs = new NativeList<PowerPoleSelectionInput>(Allocator.TempJob);
        var inputsReady = new NativeReference<bool>(Allocator.TempJob);
        var build = new BuildPowerPoleSelectionInputsJob
        {
            Validation = validationOwner.Validation, Poles = poles.AsDeferredJobArray(),
            HasParticipants = !_participantQuery.IsEmptyIgnoreFilter,
            ConfigEntity = configEntity,
            ConfigLookup = SystemAPI.GetBufferLookup<BuildingConfigElement>(true),
            PositionLookup = SystemAPI.GetComponentLookup<GridPosition>(true),
            TypeLookup = SystemAPI.GetComponentLookup<BuildingType>(true),
            Inputs = inputs, Ready = inputsReady
        }.Schedule(JobHandle.CombineDependencies(polesReady, state.Dependency));
        var selection = new SelectPowerGridConnectionJob
        {
            Validation = validationOwner.Validation, Inputs = inputs.AsDeferredJobArray(), Ready = inputsReady,
            StampLookup = SystemAPI.GetComponentLookup<PlacementStamp>(true)
        }.ScheduleParallel(_participantQuery, JobHandle.CombineDependencies(build, state.Dependency));
        state.Dependency = JobHandle.CombineDependencies(poles.Dispose(selection), inputs.Dispose(selection), inputsReady.Dispose(selection));
        validationOwner.Register(state.Dependency);
    }

}

internal struct PowerPoleSelectionInput
{
    public Entity Pole;
    public int2 Position;
    public int SupplyRange;
    public bool IsConfigured;
}

[BurstCompile]
internal struct BuildPowerPoleSelectionInputsJob : IJob
{
    [ReadOnly] public NativeReference<PowerValidationResult> Validation;
    [ReadOnly] public NativeArray<Entity> Poles;
    public bool HasParticipants;
    public Entity ConfigEntity;
    [ReadOnly] public BufferLookup<BuildingConfigElement> ConfigLookup;
    [ReadOnly] public ComponentLookup<GridPosition> PositionLookup;
    [ReadOnly] public ComponentLookup<BuildingType> TypeLookup;
    public NativeList<PowerPoleSelectionInput> Inputs;
    public NativeReference<bool> Ready;

    public void Execute()
    {
        if (Validation.Value.HasError || !HasParticipants)
        {
            return;
        }
        var configs = ConfigLookup[ConfigEntity];
        foreach (Entity pole in Poles)
        {
            var type = TypeLookup[pole];
            if (!BuildingConfigLookupUtility.TryGetConfig(configs, type.BuildingId, out var config))
            {
                // 선택 순서의 같은 위치에서 미준비로 남기도록 실패 행을 유지한다.
                Inputs.Add(new PowerPoleSelectionInput { Pole = pole });
                Ready.Value = true;
                return;
            }
            Inputs.Add(new PowerPoleSelectionInput
            {
                Pole = pole, Position = PositionLookup[pole].Value,
                SupplyRange = config.Power.SupplyRange, IsConfigured = true
            });
        }
        Ready.Value = true;
    }
}

[BurstCompile]
internal partial struct SelectPowerGridConnectionJob : IJobEntity
{
    [ReadOnly] public NativeReference<PowerValidationResult> Validation;
    [ReadOnly] public NativeArray<PowerPoleSelectionInput> Inputs;
    [ReadOnly] public NativeReference<bool> Ready;
    [ReadOnly] public ComponentLookup<PlacementStamp> StampLookup;

    private void Execute(ref PowerGridConnectionDecision decision,
        EnabledRefRW<PowerGridConnectionDecision> prepared, in GridPosition position,
        in BuildingFootprint footprint, in Direction direction)
    {
        if (Validation.Value.HasError || !Ready.Value)
        {
            return;
        }
        var effectiveSize = footprint.GetEffectiveSize(direction.dir);
        Entity selectedPole = Entity.Null;
        int2 selectedPolePosition = default;
        long selectedDistance = long.MaxValue;
        bool selectedStampLoaded = false;
        bool hasSelectedStamp = false;
        PlacementStamp selectedStamp = default;
        for (int i = 0; i < Inputs.Length; i++)
        {
            var input = Inputs[i];
            Entity pole = input.Pole;
            if (!input.IsConfigured)
            {
                return;
            }
            var polePosition = input.Position;
            if (!PowerRangeUtility.TryGetFootprintDistanceSquared(polePosition, input.SupplyRange,
                    position.Value, effectiveSize, out long distance))
            {
                continue;
            }
            if (selectedPole == Entity.Null)
            {
                selectedPole = pole;
                selectedPolePosition = polePosition;
                selectedDistance = distance;
                continue;
            }
            if (distance > selectedDistance)
            {
                continue;
            }
            if (distance == selectedDistance)
            {
                bool hasStamp = StampLookup.TryGetComponent(pole, out var stamp);
                if (!selectedStampLoaded)
                {
                    hasSelectedStamp = StampLookup.TryGetComponent(selectedPole, out selectedStamp);
                    selectedStampLoaded = true;
                }
                if (PlacementStamp.Compare(hasStamp, stamp, polePosition,
                        hasSelectedStamp, selectedStamp, selectedPolePosition) >= 0)
                {
                    continue;
                }
                hasSelectedStamp = hasStamp;
                selectedStamp = stamp;
            }
            else
            {
                selectedStampLoaded = false;
            }
            selectedPole = pole;
            selectedPolePosition = polePosition;
            selectedDistance = distance;
        }
        decision.PowerPole = selectedPole;
        prepared.ValueRW = true;
    }
}
