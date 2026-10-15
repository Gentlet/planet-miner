using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

/// <summary>
/// 생존 전신주의 연결 집합·대표·유지할 망을 계획한다. 망 생성/연결 변경/삭제 기록은 StateApply가 담당한다.
/// 분리 시 기존 망의 유지권을 먼저 정하고 병합한다. 일반 건물의 공급 전신주 선택은 앞선 Decision이 담당한다.
/// </summary>
[UpdateInGroup(typeof(PowerReservationGroup), OrderLast = true)]
public partial struct PowerGridTopologyReservationSystem : ISystem
{
    private EntityQuery _poles;
    public void OnCreate(ref SystemState state)
    {
        _poles = SystemAPI.QueryBuilder().WithAll<PowerPole>().WithNone<PendingBuildingDemolition>().Build();
    }

    public void OnUpdate(ref SystemState state)
    {
        var validationOwner = state.World.GetOrCreateSystemManaged<PowerValidationInitializationSystem>();
        var dependency = validationOwner.GetDependency(state.Dependency);
        dependency = new ResetPowerGridTopologyDecisionJob().ScheduleParallel(dependency);

        Entity configEntity = SystemAPI.TryGetSingletonEntity<BuildingConfig>(out var config) ? config : Entity.Null;
        var poles = _poles.ToEntityListAsync(Allocator.TempJob, dependency, out var polesReady);

        var snapshots = new NativeList<PowerPoleSnapshot>(Allocator.TempJob);
        var collect = new CollectPowerPolesJob
        {
            Entities = poles.AsDeferredJobArray(), ConfigEntity = configEntity,
            BuildingTypeLookup = state.GetComponentLookup<BuildingType>(true),
            ConfigLookup = state.GetBufferLookup<BuildingConfigElement>(true),
            DisabledLookup = state.GetComponentLookup<Disabled>(true),
            EntityStorageLookup = state.GetEntityStorageInfoLookup(),
            GridPositionLookup = state.GetComponentLookup<GridPosition>(true),
            PlacementStampLookup = state.GetComponentLookup<PlacementStamp>(true),
            PowerGridConnectionLookup = state.GetComponentLookup<PowerGridConnection>(true),
            PowerGridLookup = state.GetComponentLookup<PowerGrid>(true),
            PowerGridTopologyDecisionLookup = state.GetComponentLookup<PowerGridTopologyDecision>(true),
            PowerPoleLookup = state.GetComponentLookup<PowerPole>(true),
            PrefabLookup = state.GetComponentLookup<Prefab>(true),
            Poles = snapshots,
            Validation = validationOwner.Validation
        }.Schedule(JobHandle.CombineDependencies(polesReady, state.Dependency));
        var build = new BuildPowerGridTopologyJob
        {
            Validation = validationOwner.Validation, Poles = snapshots,
            Decisions = SystemAPI.GetComponentLookup<PowerGridTopologyDecision>()
        }.Schedule(JobHandle.CombineDependencies(collect, state.Dependency));
        state.Dependency = JobHandle.CombineDependencies(poles.Dispose(build), snapshots.Dispose(build));
        validationOwner.Register(state.Dependency);
    }

    internal static bool TryReadPole(ComponentLookup<BuildingType> buildingTypeLookup,
        ComponentLookup<Disabled> disabledLookup,
        EntityStorageInfoLookup entityStorageLookup,
        ComponentLookup<GridPosition> gridPositionLookup,
        ComponentLookup<PlacementStamp> placementStampLookup,
        ComponentLookup<PowerGridConnection> powerGridConnectionLookup,
        ComponentLookup<PowerGrid> powerGridLookup,
        ComponentLookup<PowerGridTopologyDecision> powerGridTopologyDecisionLookup,
        ComponentLookup<PowerPole> powerPoleLookup,
        ComponentLookup<Prefab> prefabLookup, Entity entity,
        DynamicBuffer<BuildingConfigElement> configs, out PowerPoleSnapshot snapshot)
    {
        snapshot = default;
        if (!buildingTypeLookup.HasComponent(entity) || !gridPositionLookup.HasComponent(entity) ||
            !powerGridConnectionLookup.HasComponent(entity) ||
            !powerGridTopologyDecisionLookup.HasComponent(entity))
        {
            return false;
        }

        var type = buildingTypeLookup[entity];
        if (type.Type != BuildingTypeEnum.PowerPole && type.Type != BuildingTypeEnum.MainFacility)
        {
            return false;
        }
        if (!BuildingConfigLookupUtility.TryGetConfig(configs, type.Type, type.Tier, out var config))
        {
            return false;
        }

        var connection = powerGridConnectionLookup[entity];
        if (connection.PowerPole != entity)
        {
            return false;
        }
        if (!IsPublishedGridValid(disabledLookup, entityStorageLookup, powerGridLookup, powerPoleLookup, prefabLookup, connection.Grid))
        {
            return false;
        }

        bool hasStamp = placementStampLookup.HasComponent(entity);
        snapshot = new PowerPoleSnapshot
        {
            Entity = entity,
            Position = gridPositionLookup[entity].Value,
            ConnectionRange = config.Power.ConnectionRange,
            ExistingGrid = connection.Grid,
            HasStamp = hasStamp,
            Stamp = hasStamp ? placementStampLookup[entity] : default
        };
        return true;
    }

    private static bool IsPublishedGridValid(ComponentLookup<Disabled> disabledLookup,
        EntityStorageInfoLookup entityStorageLookup,
        ComponentLookup<PowerGrid> powerGridLookup,
        ComponentLookup<PowerPole> powerPoleLookup,
        ComponentLookup<Prefab> prefabLookup, Entity grid)
    {
        if (grid == Entity.Null)
        {
            return true;
        }
        if (!entityStorageLookup.Exists(grid))
        {
            return false;
        }
        if (!powerGridLookup.HasComponent(grid))
        {
            return false;
        }
        return !powerPoleLookup.HasComponent(grid) && !disabledLookup.HasComponent(grid) &&
            !prefabLookup.HasComponent(grid);
    }
}

internal struct PowerPoleSnapshot : System.IComparable<PowerPoleSnapshot>
{
    public Entity Entity;
    public int2 Position;
    public int ConnectionRange;
    public Entity ExistingGrid;
    public bool HasStamp;
    public PlacementStamp Stamp;

    public int CompareTo(PowerPoleSnapshot other) => PlacementStamp.Compare(
        HasStamp, Stamp, Position, other.HasStamp, other.Stamp, other.Position);
}

[BurstCompile]
internal struct BuildPowerGridTopologyJob : IJob
{
    [ReadOnly] public NativeReference<PowerValidationResult> Validation;
    public NativeList<PowerPoleSnapshot> Poles;
    public ComponentLookup<PowerGridTopologyDecision> Decisions;

    public void Execute()
    {
        if (Validation.Value.HasError || Poles.Length == 0)
        {
            return;
        }
        Poles.Sort();
        int count = Poles.Length;
        var parents = new NativeArray<int>(count, Allocator.Temp);
        var retainedGridOwners = new NativeParallelHashMap<Entity, int>(count, Allocator.Temp);
        for (int i = 0; i < count; i++)
        {
            parents[i] = i;
        }

        ConnectPoles(parents);
        AssignRetainedGridOwners(parents, retainedGridOwners);
        PlanConnectedSets(parents, retainedGridOwners);

        retainedGridOwners.Dispose();
        parents.Dispose();
    }

    private void ConnectPoles(NativeArray<int> parents)
    {
        for (int i = 0; i < Poles.Length; i++)
        {
            for (int j = i + 1; j < Poles.Length; j++)
            {
                if (!PowerRangeUtility.AreCentersConnected(
                        Poles[i].Position, Poles[i].ConnectionRange, Poles[j].Position, Poles[j].ConnectionRange))
                {
                    continue;
                }
                int leftRoot = FindRoot(parents, i);
                int rightRoot = FindRoot(parents, j);
                parents[math.max(leftRoot, rightRoot)] = math.min(leftRoot, rightRoot);
            }
        }
    }

    private void AssignRetainedGridOwners(NativeArray<int> parents,
        NativeParallelHashMap<Entity, int> retainedGridOwners)
    {
        // 1A·2A: 망별 가장 이른 생존 멤버의 집합에만 유지권을 부여한 뒤 병합을 판단한다.
        for (int i = 0; i < Poles.Length; i++)
        {
            Entity existingGrid = Poles[i].ExistingGrid;
            if (existingGrid == Entity.Null)
            {
                continue;
            }
            retainedGridOwners.TryAdd(existingGrid, FindRoot(parents, i));
        }
    }

    private void PlanConnectedSets(NativeArray<int> parents,
        NativeParallelHashMap<Entity, int> retainedGridOwners)
    {
        for (int root = 0; root < Poles.Length; root++)
        {
            if (FindRoot(parents, root) != root)
            {
                continue;
            }
            int representative = root;
            Entity grid = Entity.Null;
            for (int i = root; i < Poles.Length; i++)
            {
                if (FindRoot(parents, i) != root)
                {
                    continue;
                }
                Entity existingGrid = Poles[i].ExistingGrid;
                if (existingGrid == Entity.Null)
                {
                    continue;
                }
                if (retainedGridOwners[existingGrid] != root)
                {
                    continue;
                }
                // 유지권을 가진 기존 망 중 가장 이른 멤버의 망을 유지한다. 신규 멤버는 선택에 끼지 않는다.
                representative = i;
                grid = existingGrid;
                break;
            }
            for (int i = root; i < Poles.Length; i++)
            {
                if (FindRoot(parents, i) != root)
                {
                    continue;
                }
                var pole = Poles[i];
                Decisions[pole.Entity] = new PowerGridTopologyDecision
                {
                    RepresentativePole = Poles[representative].Entity,
                    RetainedGrid = grid
                };
            }
        }
    }

    private static int FindRoot(NativeArray<int> parents, int index)
    {
        while (parents[index] != index)
        {
            parents[index] = parents[parents[index]];
            index = parents[index];
        }
        return index;
    }
}

[BurstCompile]
internal struct CollectPowerPolesJob : IJob
{
    [ReadOnly] public NativeArray<Entity> Entities;
    public Entity ConfigEntity;
    [ReadOnly] public ComponentLookup<BuildingType> BuildingTypeLookup;
    [ReadOnly] public BufferLookup<BuildingConfigElement> ConfigLookup;
    [ReadOnly] public ComponentLookup<Disabled> DisabledLookup;
    [ReadOnly] public EntityStorageInfoLookup EntityStorageLookup;
    [ReadOnly] public ComponentLookup<GridPosition> GridPositionLookup;
    [ReadOnly] public ComponentLookup<PlacementStamp> PlacementStampLookup;
    [ReadOnly] public ComponentLookup<PowerGridConnection> PowerGridConnectionLookup;
    [ReadOnly] public ComponentLookup<PowerGrid> PowerGridLookup;
    [ReadOnly] public ComponentLookup<PowerGridTopologyDecision> PowerGridTopologyDecisionLookup;
    [ReadOnly] public ComponentLookup<PowerPole> PowerPoleLookup;
    [ReadOnly] public ComponentLookup<Prefab> PrefabLookup;
    public NativeList<PowerPoleSnapshot> Poles;
    public NativeReference<PowerValidationResult> Validation;
    public void Execute()
    {
        if (Validation.Value.HasError)
        {
            return;
        }
        foreach (Entity entity in Entities)
        {
            if (!PowerValidationUtility.HasConfiguration(ConfigEntity, ConfigLookup) || !PowerGridTopologyReservationSystem.TryReadPole(
                BuildingTypeLookup, DisabledLookup, EntityStorageLookup, GridPositionLookup, PlacementStampLookup,
                PowerGridConnectionLookup, PowerGridLookup, PowerGridTopologyDecisionLookup, PowerPoleLookup,
                PrefabLookup, entity, ConfigLookup[ConfigEntity], out var snapshot))
            {
                PowerValidationUtility.Fail(Validation,
                    "[PowerGridTopologyReservationSystem] Invalid pole configuration or published connection.");
                return;
            }
            Poles.Add(snapshot);
        }
    }
}
