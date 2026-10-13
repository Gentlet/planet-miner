using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

/// <summary>
/// Reservation의 연결 계획을 최종 검사하고 망 생성/연결 변경/병합된 망 삭제를 EndBuilding에 기록한다.
/// 연결 집합이나 대표를 다시 선택하지 않으며 예약 망 Entity는 이 시스템의 같은 ECB 안에서만 참조한다.
/// 비활성/철거 전신주와 모든 런타임 참가자의 참조를 정리한 뒤 빈 망을 삭제한다.
/// </summary>
[UpdateInGroup(typeof(BuildingStateApplyGroup))]
[UpdateBefore(typeof(BuildingLifecycleApplySystem))]
public partial struct PowerGridTopologyApplySystem : ISystem
{
    private EntityQuery _activePoleQuery;
    private EntityQuery _connectionQuery;
    private EntityQuery _gridQuery;

    public void OnCreate(ref SystemState state)
    {
        _activePoleQuery = SystemAPI.QueryBuilder().WithAll<PowerPole>()
            .WithNone<PendingBuildingDemolition>().Build();
        _connectionQuery = SystemAPI.QueryBuilder().WithAll<PowerGridConnection>().WithNone<Prefab>()
            .WithOptions(EntityQueryOptions.IncludeDisabledEntities).Build();
        _gridQuery = SystemAPI.QueryBuilder().WithAll<PowerGrid>().WithNone<Prefab>()
            .WithOptions(EntityQueryOptions.IncludeDisabledEntities).Build();
    }

    public void OnUpdate(ref SystemState state)
    {
        var publications = new NativeList<PowerGridTopologyPublication>(Allocator.TempJob);
        foreach (var (decision, connection, entity) in
                 SystemAPI.Query<RefRO<PowerGridTopologyDecision>, RefRO<PowerGridConnection>>()
                     .WithAll<PowerPole>().WithNone<PendingBuildingDemolition>().WithEntityAccess())
        {
            if (decision.ValueRO.RepresentativePole == Entity.Null)
            {
                // Reservation의 입력 실패/미실행을 빈 망으로 오인하여 공개 상태를 삭제하지 않는다.
                publications.Dispose();
                return;
            }
            if (!IsPublicationValid(state.EntityManager, entity, decision.ValueRO, connection.ValueRO))
            {
                publications.Dispose();
                var failureEcb = state.World.GetOrCreateSystemManaged<EndBuildingEntityCommandBufferSystem>()
                    .CreateCommandBuffer();
                SimulationFailureUtility.Record(ref failureEcb,
                    "[PowerGridTopologyApplySystem] Prepared connection changed before apply.");
                return;
            }
            publications.Add(new PowerGridTopologyPublication { Pole = entity, Decision = decision.ValueRO });
        }

        if (publications.Length != _activePoleQuery.CalculateEntityCount())
        {
            publications.Dispose();
            return;
        }

        var connectionEntities = _connectionQuery.ToEntityArray(Allocator.TempJob);
        var existingGrids = _gridQuery.ToEntityArray(Allocator.TempJob);
        if (publications.Length == 0 && connectionEntities.Length == 0 && existingGrids.Length == 0)
        {
            publications.Dispose();
            connectionEntities.Dispose();
            existingGrids.Dispose();
            return;
        }
        var ecbSystem = state.World.GetOrCreateSystemManaged<EndBuildingEntityCommandBufferSystem>();
        var applyHandle = new ApplyPowerGridTopologyJob
        {
            Publications = publications,
            ConnectionEntities = connectionEntities,
            ConnectionLookup = SystemAPI.GetComponentLookup<PowerGridConnection>(true),
            PowerPoleLookup = SystemAPI.GetComponentLookup<PowerPole>(true),
            ExistingGrids = existingGrids,
            ECB = ecbSystem.CreateCommandBuffer()
        }.Schedule(state.Dependency);
        state.Dependency = JobHandle.CombineDependencies(publications.Dispose(applyHandle),
            connectionEntities.Dispose(applyHandle), existingGrids.Dispose(applyHandle));
        ecbSystem.AddJobHandleForProducer(state.Dependency);
    }

    private static bool IsPublicationValid(EntityManager manager, Entity pole,
        in PowerGridTopologyDecision decision, in PowerGridConnection connection)
    {
        if (connection.PowerPole != pole || connection.Grid != decision.ExpectedGrid)
        {
            return false;
        }
        Entity representative = decision.RepresentativePole;
        if (!manager.Exists(representative))
        {
            return false;
        }
        if (!manager.HasComponent<PowerPole>(representative) ||
            !manager.HasComponent<PowerGridTopologyDecision>(representative) ||
            !manager.HasComponent<PowerGridConnection>(representative))
        {
            return false;
        }
        if (manager.HasComponent<Disabled>(representative) || manager.HasComponent<Prefab>(representative) ||
            manager.HasComponent<PendingBuildingDemolition>(representative))
        {
            return false;
        }

        var representativeDecision = manager.GetComponentData<PowerGridTopologyDecision>(representative);
        var representativeConnection = manager.GetComponentData<PowerGridConnection>(representative);
        if (representativeDecision.RepresentativePole != representative ||
            representativeDecision.RetainedGrid != decision.RetainedGrid ||
            representativeConnection.PowerPole != representative ||
            representativeConnection.Grid != representativeDecision.ExpectedGrid)
        {
            return false;
        }
        // 분리되어 새 망이 필요한 집합은 대표의 이전 망과 유지 망(Null)이 다를 수 있다.
        if (decision.RetainedGrid != Entity.Null)
        {
            if (representativeDecision.ExpectedGrid != decision.RetainedGrid)
            {
                return false;
            }
        }
        return IsPublishedGridValid(manager, decision.ExpectedGrid) &&
            IsPublishedGridValid(manager, decision.RetainedGrid);
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

internal struct PowerGridTopologyPublication
{
    public Entity Pole;
    public PowerGridTopologyDecision Decision;
}

[BurstCompile]
internal struct ApplyPowerGridTopologyJob : IJob
{
    [ReadOnly] public NativeList<PowerGridTopologyPublication> Publications;
    [ReadOnly] public NativeArray<Entity> ConnectionEntities;
    [ReadOnly] public ComponentLookup<PowerGridConnection> ConnectionLookup;
    [ReadOnly] public ComponentLookup<PowerPole> PowerPoleLookup;
    [ReadOnly] public NativeArray<Entity> ExistingGrids;
    public EntityCommandBuffer ECB;

    public void Execute()
    {
        int count = Publications.Length > 0 ? Publications.Length : 1;
        var gridsByRepresentative = new NativeParallelHashMap<Entity, Entity>(count, Allocator.Temp);
        var gridsByPole = new NativeParallelHashMap<Entity, Entity>(count, Allocator.Temp);
        var retainedGrids = new NativeParallelHashSet<Entity>(count, Allocator.Temp);

        PrepareGridEntities(gridsByRepresentative, gridsByPole, retainedGrids);
        UpdateConnections(gridsByPole);
        DeleteUnusedGrids(retainedGrids);

        retainedGrids.Dispose();
        gridsByPole.Dispose();
        gridsByRepresentative.Dispose();
    }

    private void PrepareGridEntities(NativeParallelHashMap<Entity, Entity> gridsByRepresentative,
        NativeParallelHashMap<Entity, Entity> gridsByPole, NativeParallelHashSet<Entity> retainedGrids)
    {
        for (int i = 0; i < Publications.Length; i++)
        {
            var publication = Publications[i];
            var decision = publication.Decision;
            if (!gridsByRepresentative.TryGetValue(decision.RepresentativePole, out var grid))
            {
                grid = decision.RetainedGrid;
                if (grid == Entity.Null)
                {
                    grid = ECB.CreateEntity();
                    ECB.AddComponent<PowerGrid>(grid);
                }
                else
                {
                    retainedGrids.Add(grid);
                }
                gridsByRepresentative.Add(decision.RepresentativePole, grid);
            }
            gridsByPole.Add(publication.Pole, grid);
        }
    }

    private void UpdateConnections(NativeParallelHashMap<Entity, Entity> gridsByPole)
    {
        for (int i = 0; i < ConnectionEntities.Length; i++)
        {
            Entity participant = ConnectionEntities[i];
            var connection = ConnectionLookup[participant];
            bool isPole = PowerPoleLookup.HasComponent(participant);
            Entity selectedPole = isPole ? participant : connection.PowerPole;
            Entity grid = Entity.Null;
            if (!gridsByPole.TryGetValue(selectedPole, out grid))
            {
                // 3A: 전신주는 자기 참조/설치 기록을 보존하고, 일반 참가자는 유효하지 않은 선택을 해제한다.
                if (!isPole)
                {
                    selectedPole = Entity.Null;
                }
                grid = Entity.Null;
            }
            if (connection.PowerPole != selectedPole || connection.Grid != grid)
            {
                ECB.SetComponent(participant, new PowerGridConnection { PowerPole = selectedPole, Grid = grid });
            }
        }
    }

    private void DeleteUnusedGrids(NativeParallelHashSet<Entity> retainedGrids)
    {
        // 생존 전신주에서 역추적하지 않는다. 마지막 전신주가 사라진 뒤에도 기존 빈 망을 회수한다.
        for (int i = 0; i < ExistingGrids.Length; i++)
        {
            Entity grid = ExistingGrids[i];
            if (!retainedGrids.Contains(grid))
            {
                ECB.DestroyEntity(grid);
            }
        }
    }
}
