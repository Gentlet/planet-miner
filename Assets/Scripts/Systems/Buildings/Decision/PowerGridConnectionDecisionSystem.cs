using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

/// <summary>
/// 활성 일반 참가자가 회전 footprint 전체에서 공급받을 전신주를 매 틱 선택한다.
/// 공개 연결/공간 캐시를 복제하지 않고 선택에 필요한 생존 ECS만 읽는다.
/// 토폴로지 준비 성공과 선택한 전신주의 최종 망 연결은 후속 Apply가 검사한다.
/// </summary>
[UpdateInGroup(typeof(BuildingDecisionGroup))]
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
        _poleQuery = SystemAPI.QueryBuilder().WithAll<PowerPole>()
            .WithNone<PendingBuildingDemolition>().Build();
    }

    public void OnUpdate(ref SystemState state)
    {
        // 비활성/철거 참가자도 이전 선택 계획을 소비할 수 없게 한다.
        foreach (var (decision, entity) in SystemAPI.Query<RefRW<PowerGridConnectionDecision>>()
                     .WithNone<Prefab>().WithEntityAccess()
                     .WithOptions(EntityQueryOptions.IncludeDisabledEntities |
                                  EntityQueryOptions.IgnoreComponentEnabledState))
        {
            decision.ValueRW = default;
            state.EntityManager.SetComponentEnabled<PowerGridConnectionDecision>(entity, false);
        }
        foreach (var (_, entity) in SystemAPI.Query<RefRO<PowerGridConnectionDecision>>()
                     .WithNone<PowerPole, PendingBuildingDemolition>().WithEntityAccess()
                     .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            if (!IsParticipantValid(state.EntityManager, entity))
            {
                RecordFailure(ref state);
                return;
            }
        }
        if (_participantQuery.IsEmptyIgnoreFilter)
        {
            return;
        }
        if (!SystemAPI.TryGetSingletonEntity<BuildingConfig>(out var configEntity))
        {
            RecordFailure(ref state);
            return;
        }
        if (!state.EntityManager.HasBuffer<BuildingConfigElement>(configEntity))
        {
            RecordFailure(ref state);
            return;
        }
        var configs = state.EntityManager.GetBuffer<BuildingConfigElement>(configEntity, true);
        foreach (var (_, entity) in SystemAPI.Query<RefRO<PowerPole>>()
                     .WithNone<PendingBuildingDemolition>().WithEntityAccess())
        {
            if (!IsPoleInputValid(state.EntityManager, entity, configs))
            {
                RecordFailure(ref state);
                return;
            }
        }

        var poles = _poleQuery.ToEntityArray(Allocator.TempJob);
        var selectionHandle = new SelectPowerGridConnectionJob
        {
            Poles = poles,
            ConfigEntity = configEntity,
            ConfigLookup = SystemAPI.GetBufferLookup<BuildingConfigElement>(true),
            PositionLookup = SystemAPI.GetComponentLookup<GridPosition>(true),
            TypeLookup = SystemAPI.GetComponentLookup<BuildingType>(true),
            StampLookup = SystemAPI.GetComponentLookup<PlacementStamp>(true)
        }.ScheduleParallel(_participantQuery, state.Dependency);
        state.Dependency = poles.Dispose(selectionHandle);
    }

    private static bool IsPoleInputValid(EntityManager manager, Entity pole,
        DynamicBuffer<BuildingConfigElement> configs)
    {
        if (!manager.HasComponent<BuildingType>(pole) || !manager.HasComponent<GridPosition>(pole))
        {
            return false;
        }
        var type = manager.GetComponentData<BuildingType>(pole);
        if (type.Type != BuildingTypeEnum.PowerPole && type.Type != BuildingTypeEnum.MainFacility)
        {
            return false;
        }
        if (!BuildingConfigLookupUtility.TryGetConfig(configs, type.Type, type.Tier, out var config))
        {
            return false;
        }
        return PlanetMiner.Config.BuildingPowerConfigParser.Validate(config);
    }

    internal static bool IsParticipantValid(EntityManager manager, Entity entity)
    {
        if (!manager.HasComponent<PowerGridConnection>(entity) || !manager.HasComponent<BuildingType>(entity) ||
            !manager.HasComponent<GridPosition>(entity) || !manager.HasComponent<BuildingFootprint>(entity) ||
            !manager.HasComponent<Direction>(entity))
        {
            return false;
        }
        var footprint = manager.GetComponentData<BuildingFootprint>(entity);
        if (footprint.Size.x <= 0 || footprint.Size.y <= 0)
        {
            return false;
        }
        var direction = manager.GetComponentData<Direction>(entity).dir;
        if (direction < DirectionEnum.Up || direction >= DirectionEnum.Count)
        {
            return false;
        }
        switch (manager.GetComponentData<BuildingType>(entity).Type)
        {
            case BuildingTypeEnum.Miner:
            case BuildingTypeEnum.Crafter:
            case BuildingTypeEnum.ResearchBuilding:
            case BuildingTypeEnum.CoalGenerator:
            case BuildingTypeEnum.DroneStation:
                return true;
            default:
                return false;
        }
    }

    private static void RecordFailure(ref SystemState state)
    {
        var failureEcb = state.World.GetOrCreateSystemManaged<EndBuildingEntityCommandBufferSystem>()
            .CreateCommandBuffer();
        SimulationFailureUtility.Record(ref failureEcb,
            "[PowerGridConnectionDecisionSystem] Invalid participant or pole selection input/configuration.");
    }
}

[BurstCompile]
internal partial struct SelectPowerGridConnectionJob : IJobEntity
{
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
