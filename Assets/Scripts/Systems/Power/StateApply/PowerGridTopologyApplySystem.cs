using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

/// <summary>
/// 준비된 공급 선택과 토폴로지로 망/연결 변경을 EndPower에 기록한다.
/// 연결 집합이나 대표를 다시 선택하지 않으며 예약 망 Entity는 이 시스템의 같은 ECB 안에서만 참조한다.
/// 비활성/철거 전신주와 모든 런타임 참가자의 참조를 정리한 뒤 빈 망을 삭제한다.
/// </summary>
[UpdateInGroup(typeof(PowerStateApplyGroup))]
public partial struct PowerGridTopologyApplySystem : ISystem
{
    private EntityQuery _activePoleQuery;
    private EntityQuery _connectionQuery;
    private EntityQuery _gridQuery;
    private EntityQuery _participants;

    public void OnCreate(ref SystemState state)
    {
        _activePoleQuery = SystemAPI.QueryBuilder().WithAll<PowerPole>()
            .WithNone<PendingBuildingDemolition>().Build();
        _connectionQuery = SystemAPI.QueryBuilder().WithAll<PowerGridConnection>().WithNone<Prefab>()
            .WithOptions(EntityQueryOptions.IncludeDisabledEntities).Build();
        _participants = SystemAPI.QueryBuilder().WithAll<PowerGridConnectionDecision>()
            .WithNone<PowerPole, PendingBuildingDemolition>()
            .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).Build();
        _gridQuery = SystemAPI.QueryBuilder().WithAll<PowerGrid>().WithNone<Prefab>()
            .WithOptions(EntityQueryOptions.IncludeDisabledEntities).Build();
    }

    public void OnUpdate(ref SystemState state)
    {
        var validationOwner = state.World.GetOrCreateSystemManaged<PowerValidationInitializationSystem>();
        var dependency = validationOwner.GetDependency(state.Dependency);
        var poles = _activePoleQuery.ToEntityListAsync(Allocator.TempJob, dependency, out var polesReady);
        var participants = _participants.ToEntityListAsync(Allocator.TempJob, dependency, out var participantsReady);
        var connections = _connectionQuery.ToEntityListAsync(Allocator.TempJob, dependency, out var connectionsReady);
        var grids = _gridQuery.ToEntityListAsync(Allocator.TempJob, dependency, out var gridsReady);

        var publications = new NativeList<PowerGridTopologyPublication>(Allocator.TempJob);
        var ready = new NativeReference<bool>(Allocator.TempJob);
        var collect = new CollectPowerPublicationsJob
        {
            Poles = poles.AsDeferredJobArray(), Participants = participants.AsDeferredJobArray(),
            PowerGridTopologyDecisionLookup = state.GetComponentLookup<PowerGridTopologyDecision>(true),
            PowerGridConnectionDecisionLookup = state.GetComponentLookup<PowerGridConnectionDecision>(true),
            Publications = publications, Ready = ready,
            Validation = validationOwner.Validation
        }.Schedule(JobHandle.CombineDependencies(JobHandle.CombineDependencies(polesReady, participantsReady), state.Dependency));
        var ecbSystem = state.World.GetOrCreateSystemManaged<EndPowerEntityCommandBufferSystem>();
        var apply = new ApplyPowerGridTopologyJob
        {
            Validation = validationOwner.Validation, ActivePoles = poles.AsDeferredJobArray(), Publications = publications, Ready = ready,
            ConnectionEntities = connections.AsDeferredJobArray(), ExistingGrids = grids.AsDeferredJobArray(),
            ConnectionLookup = SystemAPI.GetComponentLookup<PowerGridConnection>(true),
            ConnectionDecisionLookup = SystemAPI.GetComponentLookup<PowerGridConnectionDecision>(true),
            PowerPoleLookup = SystemAPI.GetComponentLookup<PowerPole>(true),
            DisabledLookup = SystemAPI.GetComponentLookup<Disabled>(true),
            DemolitionLookup = SystemAPI.GetComponentLookup<PendingBuildingDemolition>(true),
            ECB = ecbSystem.CreateCommandBuffer()
        }.Schedule(JobHandle.CombineDependencies(JobHandle.CombineDependencies(collect, JobHandle.CombineDependencies(connectionsReady, gridsReady)), state.Dependency));
        state.Dependency = JobHandle.CombineDependencies(JobHandle.CombineDependencies(poles.Dispose(apply), participants.Dispose(apply)),
            JobHandle.CombineDependencies(connections.Dispose(apply), grids.Dispose(apply), publications.Dispose(apply)));
        state.Dependency = Unity.Jobs.JobHandle.CombineDependencies(state.Dependency, ready.Dispose(apply));
        validationOwner.Register(state.Dependency);
        ecbSystem.AddJobHandleForProducer(apply);
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
    [ReadOnly] public NativeReference<PowerValidationResult> Validation;
    [ReadOnly] public NativeReference<bool> Ready;
    [ReadOnly] public NativeArray<Entity> ActivePoles;
    [ReadOnly] public NativeList<PowerGridTopologyPublication> Publications;
    [ReadOnly] public NativeArray<Entity> ConnectionEntities;
    [ReadOnly] public ComponentLookup<PowerGridConnection> ConnectionLookup;
    [ReadOnly] public ComponentLookup<PowerGridConnectionDecision> ConnectionDecisionLookup;
    [ReadOnly] public ComponentLookup<PowerPole> PowerPoleLookup;
    [ReadOnly] public ComponentLookup<Disabled> DisabledLookup;
    [ReadOnly] public ComponentLookup<PendingBuildingDemolition> DemolitionLookup;
    [ReadOnly] public NativeArray<Entity> ExistingGrids;
    public EntityCommandBuffer ECB;

    public void Execute()
    {
        if (Validation.Value.HasError || !Ready.Value || Publications.Length != ActivePoles.Length)
        {
            return;
        }
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
            if (!isPole)
            {
                selectedPole = GetParticipantSelectedPole(participant, connection.PowerPole);
            }
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

    private Entity GetParticipantSelectedPole(Entity participant, Entity previousPole)
    {
        if (DisabledLookup.HasComponent(participant))
        {
            return previousPole;
        }
        if (DemolitionLookup.HasComponent(participant))
        {
            return previousPole;
        }
        if (!ConnectionDecisionLookup.HasComponent(participant))
        {
            return previousPole;
        }
        if (!ConnectionDecisionLookup.IsComponentEnabled(participant))
        {
            return previousPole;
        }
        return ConnectionDecisionLookup[participant].PowerPole;
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

[BurstCompile]
internal struct CollectPowerPublicationsJob : IJob
{
    [ReadOnly] public NativeArray<Entity> Poles;
    [ReadOnly] public NativeArray<Entity> Participants;
    [ReadOnly] public ComponentLookup<PowerGridTopologyDecision> PowerGridTopologyDecisionLookup;
    [ReadOnly] public ComponentLookup<PowerGridConnectionDecision> PowerGridConnectionDecisionLookup;
    [ReadOnly] public NativeReference<PowerValidationResult> Validation;
    public NativeList<PowerGridTopologyPublication> Publications;
    public NativeReference<bool> Ready;

    public void Execute()
    {
        if (Validation.Value.HasError)
        {
            return;
        }
        foreach (Entity pole in Poles)
        {
            var decision = PowerGridTopologyDecisionLookup[pole];
            if (decision.RepresentativePole == Entity.Null)
            {
                return;
            }
            Publications.Add(new PowerGridTopologyPublication { Pole = pole, Decision = decision });
        }
        foreach (Entity participant in Participants)
        {
            if (!PowerGridConnectionDecisionLookup.IsComponentEnabled(participant))
            {
                return;
            }
        }
        Ready.Value = true;
    }
}
