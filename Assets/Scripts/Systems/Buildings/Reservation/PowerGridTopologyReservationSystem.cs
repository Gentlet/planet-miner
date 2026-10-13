using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

/// <summary>
/// 생존 전신주의 연결 집합·대표·유지할 망을 계획한다. 망 생성/연결 변경/삭제 기록은 StateApply가 담당한다.
/// 분리 시 기존 망의 유지권을 먼저 정하고 병합한다. 일반 건물의 공급 전신주 선택은 후속 Task가 담당한다.
/// </summary>
[UpdateInGroup(typeof(BuildingReservationGroup), OrderLast = true)]
public partial struct PowerGridTopologyReservationSystem : ISystem
{
    public void OnCreate(ref SystemState state) { }

    public void OnUpdate(ref SystemState state)
    {
        // 철거/비활성 참가자에도 이전 틱의 계획을 남기지 않는다.
        foreach (var decision in SystemAPI.Query<RefRW<PowerGridTopologyDecision>>()
                     .WithAll<PowerPole>().WithNone<Prefab>()
                     .WithOptions(EntityQueryOptions.IncludeDisabledEntities))
        {
            decision.ValueRW = default;
        }

        bool hasConfig = SystemAPI.TryGetSingletonEntity<BuildingConfig>(out var configEntity) &&
            state.EntityManager.HasBuffer<BuildingConfigElement>(configEntity);
        var configs = hasConfig ? state.EntityManager.GetBuffer<BuildingConfigElement>(configEntity, true) : default;
        var snapshots = new NativeList<PowerPoleSnapshot>(Allocator.TempJob);
        foreach (var (_, entity) in SystemAPI.Query<RefRO<PowerPole>>()
                     .WithNone<PendingBuildingDemolition>().WithEntityAccess())
        {
            if (!hasConfig || !TryReadPole(state.EntityManager, entity, configs, out var snapshot))
            {
                snapshots.Dispose();
                var failureEcb = state.World.GetOrCreateSystemManaged<EndBuildingEntityCommandBufferSystem>()
                    .CreateCommandBuffer();
                SimulationFailureUtility.Record(ref failureEcb,
                    "[PowerGridTopologyReservationSystem] Invalid pole configuration or published connection.");
                return;
            }
            snapshots.Add(snapshot);
        }

        if (snapshots.Length == 0)
        {
            snapshots.Dispose();
            return;
        }

        var job = new BuildPowerGridTopologyJob
        {
            Poles = snapshots,
            Decisions = SystemAPI.GetComponentLookup<PowerGridTopologyDecision>()
        };
        var buildHandle = job.Schedule(state.Dependency);
        state.Dependency = snapshots.Dispose(buildHandle);
    }

    private static bool TryReadPole(EntityManager manager, Entity entity,
        DynamicBuffer<BuildingConfigElement> configs, out PowerPoleSnapshot snapshot)
    {
        snapshot = default;
        if (!manager.HasComponent<BuildingType>(entity) || !manager.HasComponent<GridPosition>(entity) ||
            !manager.HasComponent<PowerGridConnection>(entity) ||
            !manager.HasComponent<PowerGridTopologyDecision>(entity))
        {
            return false;
        }

        var type = manager.GetComponentData<BuildingType>(entity);
        if (type.Type != BuildingTypeEnum.PowerPole && type.Type != BuildingTypeEnum.MainFacility)
        {
            return false;
        }
        if (!BuildingConfigLookupUtility.TryGetConfig(configs, type.Type, type.Tier, out var config) ||
            !PlanetMiner.Config.BuildingPowerConfigParser.Validate(config))
        {
            return false;
        }

        var connection = manager.GetComponentData<PowerGridConnection>(entity);
        if (connection.PowerPole != entity)
        {
            return false;
        }
        if (!IsPublishedGridValid(manager, connection.Grid))
        {
            return false;
        }

        bool hasStamp = manager.HasComponent<PlacementStamp>(entity);
        snapshot = new PowerPoleSnapshot
        {
            Entity = entity,
            Position = manager.GetComponentData<GridPosition>(entity).Value,
            ConnectionRange = config.Power.ConnectionRange,
            ExistingGrid = connection.Grid,
            HasStamp = hasStamp,
            Stamp = hasStamp ? manager.GetComponentData<PlacementStamp>(entity) : default
        };
        return true;
    }

    private static bool IsPublishedGridValid(EntityManager manager, Entity grid)
    {
        if (grid == Entity.Null)
        {
            return true;
        }
        if (!manager.Exists(grid))
        {
            return false;
        }
        if (!manager.HasComponent<PowerGrid>(grid))
        {
            return false;
        }
        return !manager.HasComponent<PowerPole>(grid) && !manager.HasComponent<Disabled>(grid) &&
            !manager.HasComponent<Prefab>(grid);
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
    public NativeList<PowerPoleSnapshot> Poles;
    public ComponentLookup<PowerGridTopologyDecision> Decisions;

    public void Execute()
    {
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
                    ExpectedGrid = pole.ExistingGrid,
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
