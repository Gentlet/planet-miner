using Unity.Burst;
using Unity.Entities;

/// <summary>Decision 결과를 EndSimulation에 공개한다. 미참가/비활성 정거장의 이전 연결도 같은 경계에서 비운다.</summary>
[UpdateInGroup(typeof(DroneStateApplyGroup))]
[BurstCompile]
public partial struct DroneStationNetworkApplySystem : ISystem
{
    private EntityQuery _stations;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        _stations = SystemAPI.QueryBuilder().WithAll<DroneStation, DroneStationNetwork, DroneStationNetworkDecision>()
            .WithOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IgnoreComponentEnabledState).Build();
    }

    public void OnUpdate(ref SystemState state)
    {
        var ecbSystem = state.World.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>();
        var handle = new ApplyDroneStationNetworkJob { ECB = ecbSystem.CreateCommandBuffer().AsParallelWriter() }
            .ScheduleParallel(_stations, state.Dependency);
        ecbSystem.AddJobHandleForProducer(handle);
        state.Dependency = handle;
    }
}

[BurstCompile]
internal partial struct ApplyDroneStationNetworkJob : IJobEntity
{
    public EntityCommandBuffer.ParallelWriter ECB;

    public void Execute([ChunkIndexInQuery] int sortKey, Entity entity, in DroneStationNetworkDecision decision,
        EnabledRefRO<DroneStationNetworkDecision> prepared)
    {
        ECB.SetComponent(sortKey, entity, new DroneStationNetwork
        {
            RepresentativeStation = prepared.ValueRO ? decision.RepresentativeStation : Entity.Null
        });
    }
}
