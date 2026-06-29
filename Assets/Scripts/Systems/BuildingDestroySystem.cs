using Unity.Entities;

[UpdateBefore(typeof(BeltMoveSystem))]
public partial class BuildingDestroySystem : SystemBase
{
    private GridOccupancySystem _gridOccupancy;

    protected override void OnCreate()
    {
        _gridOccupancy = World.GetExistingSystemManaged<GridOccupancySystem>();
        RequireForUpdate<BuildingDestroyRequest>();
    }

    protected override void OnUpdate()
    {
        if (_gridOccupancy == null)
        {
            _gridOccupancy = World.GetExistingSystemManaged<GridOccupancySystem>();
            if (_gridOccupancy == null)
                return;
        }

        EntityCommandBuffer ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(World.Unmanaged);

        foreach (var (request, requestEntity) in SystemAPI.Query<RefRO<BuildingDestroyRequest>>().WithEntityAccess())
        {
            if (!_gridOccupancy.TryGetEntity(request.ValueRO.gridPosition, out Entity targetEntity))
            {
                ecb.DestroyEntity(requestEntity);
                continue;
            }

            if (targetEntity != Entity.Null &&
                EntityManager.Exists(targetEntity) &&
                EntityManager.HasComponent<GridOccupant>(targetEntity))
            {
                _gridOccupancy.TryUnregister(request.ValueRO.gridPosition, targetEntity);
                ecb.DestroyEntity(targetEntity);
            }

            ecb.DestroyEntity(requestEntity);
        }
    }
}
