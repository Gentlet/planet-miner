using Unity.Entities;

[UpdateBefore(typeof(BeltMoveSystem))]
public partial class BuildingDestroySystem : SystemBase
{
    private ChunkMapSystem _chunkMap;

    protected override void OnCreate()
    {
        _chunkMap = World.GetExistingSystemManaged<ChunkMapSystem>();
        RequireForUpdate<BuildingDestroyRequest>();
    }

    protected override void OnUpdate()
    {
        if (_chunkMap == null)
        {
            _chunkMap = World.GetExistingSystemManaged<ChunkMapSystem>();
            if (_chunkMap == null)
                return;
        }

        EntityCommandBuffer ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(World.Unmanaged);

        foreach (var (request, requestEntity) in SystemAPI.Query<RefRO<BuildingDestroyRequest>>().WithEntityAccess())
        {
            if (!_chunkMap.TryGetBuilding(request.ValueRO.gridPosition, out Entity targetEntity))
            {
                ecb.DestroyEntity(requestEntity);
                continue;
            }

            if (targetEntity != Entity.Null &&
                EntityManager.Exists(targetEntity) &&
                EntityManager.HasComponent<GridOccupant>(targetEntity))
            {
                _chunkMap.TryUnregisterBuilding(request.ValueRO.gridPosition, targetEntity);
                ecb.DestroyEntity(targetEntity);
            }

            ecb.DestroyEntity(requestEntity);
        }
    }
}
