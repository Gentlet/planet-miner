using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

/// <summary>
/// 철거 요청의 대상/철거 가능 여부와 중복을 검증한다.
/// 거부 요청은 EndCommand에서 제거하고, 유효한 요청은 StateApply가 소비할 때까지 유지한다.
/// 요청은 이 시스템 실행 전에 존재해야 하며, 이후 StateApply까지 대상과 철거 조건을 변경하지 않는다.
/// </summary>
[UpdateInGroup(typeof(CommandGroup))]
public partial struct BuildingDemolitionCommandSystem : ISystem
{
    private EntityQuery _requestQuery;
    private ComponentLookup<BuildingType> _buildingTypeLookup;
    private ComponentLookup<IndestructibleBuilding> _indestructibleLookup;

    public void OnCreate(ref SystemState state)
    {
        _requestQuery = SystemAPI.QueryBuilder().WithAll<DemolishBuildingRequest>().Build();
        _buildingTypeLookup = state.GetComponentLookup<BuildingType>(true);
        _indestructibleLookup = state.GetComponentLookup<IndestructibleBuilding>(true);
        state.RequireForUpdate(_requestQuery);
    }

    public void OnUpdate(ref SystemState state)
    {
        _buildingTypeLookup.Update(ref state);
        _indestructibleLookup.Update(ref state);
        var ecbSystem = state.World.GetOrCreateSystemManaged<EndCommandEntityCommandBufferSystem>();
        var acceptedTargets = new NativeParallelHashSet<Entity>(_requestQuery.CalculateEntityCount(), Allocator.TempJob);
        var job = new ValidateBuildingDemolitionJob
        {
            ECB = ecbSystem.CreateCommandBuffer(),
            BuildingTypeLookup = _buildingTypeLookup,
            IndestructibleLookup = _indestructibleLookup,
            AcceptedTargets = acceptedTargets
        };
        var handle = job.Schedule(_requestQuery, state.Dependency);
        state.Dependency = acceptedTargets.Dispose(handle);
        ecbSystem.AddJobHandleForProducer(state.Dependency);
    }
}

[BurstCompile]
public partial struct ValidateBuildingDemolitionJob : IJobEntity
{
    public EntityCommandBuffer ECB;
    [ReadOnly] public ComponentLookup<BuildingType> BuildingTypeLookup;
    [ReadOnly] public ComponentLookup<IndestructibleBuilding> IndestructibleLookup;
    public NativeParallelHashSet<Entity> AcceptedTargets;

    public void Execute(Entity requestEntity, in DemolishBuildingRequest request)
    {
        if (!CanDemolish(request.TargetBuilding))
        {
            ECB.DestroyEntity(requestEntity);
            return;
        }

        if (!AcceptedTargets.Add(request.TargetBuilding))
        {
            ECB.DestroyEntity(requestEntity);
        }
    }

    private bool CanDemolish(Entity target)
    {
        if (target == Entity.Null)
        {
            return false;
        }

        if (!BuildingTypeLookup.HasComponent(target))
        {
            return false;
        }

        BuildingTypeEnum type = BuildingTypeLookup[target].Type;
        if (type == BuildingTypeEnum.None || type == BuildingTypeEnum.ConstructionSite || type == BuildingTypeEnum.MainFacility)
        {
            return false;
        }

        return !IndestructibleLookup.HasComponent(target);
    }
}
