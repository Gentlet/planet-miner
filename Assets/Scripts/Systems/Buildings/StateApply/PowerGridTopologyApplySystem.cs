using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

/// <summary>
/// Reservation의 연결 계획을 최종 검사하고 망 생성/연결 변경/병합된 망 삭제를 EndBuilding에 기록한다.
/// 연결 집합이나 대표를 다시 선택하지 않으며 예약 망 Entity는 이 시스템의 같은 ECB 안에서만 참조한다.
/// </summary>
[UpdateInGroup(typeof(BuildingStateApplyGroup))]
[UpdateBefore(typeof(BuildingLifecycleApplySystem))]
public partial struct PowerGridTopologyApplySystem : ISystem
{
    public void OnCreate(ref SystemState state) { }

    public void OnUpdate(ref SystemState state)
    {
        var publications = new NativeList<PowerGridTopologyPublication>(Allocator.TempJob);
        foreach (var (decision, connection, entity) in
                 SystemAPI.Query<RefRO<PowerGridTopologyDecision>, RefRO<PowerGridConnection>>()
                     .WithAll<PowerPole>().WithNone<PendingBuildingDemolition>().WithEntityAccess())
        {
            if (decision.ValueRO.RepresentativePole == Entity.Null)
            {
                continue;
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

        if (publications.Length == 0)
        {
            publications.Dispose();
            return;
        }
        var ecbSystem = state.World.GetOrCreateSystemManaged<EndBuildingEntityCommandBufferSystem>();
        var applyHandle = new ApplyPowerGridTopologyJob
        {
            Publications = publications,
            ECB = ecbSystem.CreateCommandBuffer()
        }.Schedule(state.Dependency);
        state.Dependency = publications.Dispose(applyHandle);
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
        if (!manager.Exists(representative) || !manager.HasComponent<PowerPole>(representative) ||
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
            representativeDecision.ExpectedGrid != decision.RetainedGrid ||
            representativeConnection.PowerPole != representative ||
            representativeConnection.Grid != decision.RetainedGrid)
        {
            return false;
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
    public EntityCommandBuffer ECB;

    public void Execute()
    {
        int count = Publications.Length;
        var gridsByRepresentative = new NativeParallelHashMap<Entity, Entity>(count, Allocator.Temp);
        var retainedGrids = new NativeParallelHashSet<Entity>(count, Allocator.Temp);
        var previousGrids = new NativeParallelHashSet<Entity>(count, Allocator.Temp);
        foreach (var publication in Publications)
        {
            var decision = publication.Decision;
            if (decision.ExpectedGrid != Entity.Null) previousGrids.Add(decision.ExpectedGrid);
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
            if (decision.ExpectedGrid != grid)
            {
                ECB.SetComponent(publication.Pole,
                    new PowerGridConnection { PowerPole = publication.Pole, Grid = grid });
            }
        }

        // 다른 집합이 아직 유지하는 망은 삭제하지 않는다. 분리·빈 망 회수는 Task 8.3이다.
        foreach (var grid in previousGrids)
        {
            if (!retainedGrids.Contains(grid)) ECB.DestroyEntity(grid);
        }
        previousGrids.Dispose();
        retainedGrids.Dispose();
        gridsByRepresentative.Dispose();
    }
}
