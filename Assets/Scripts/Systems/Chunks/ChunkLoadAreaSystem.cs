using Unity.Entities;
using Unity.Mathematics;

[UpdateBefore(typeof(ResourceMapGenerationSystem))]
public partial class ChunkLoadAreaSystem : SystemBase
{
    protected override void OnCreate()
    {
        RequireForUpdate<ChunkLoadArea>();
    }

    protected override void OnUpdate()
    {
        EntityCommandBuffer ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(World.Unmanaged);

        foreach (var (area, entity) in SystemAPI.Query<RefRO<ChunkLoadArea>>().WithNone<ChunkLoadAreaQueued>().WithEntityAccess())
        {
            int2 size = new int2(math.max(1, area.ValueRO.size.x), math.max(1, area.ValueRO.size.y));

            for (int y = 0; y < size.y; y++)
            {
                for (int x = 0; x < size.x; x++)
                {
                    Entity request = ecb.CreateEntity();
                    ecb.AddComponent(request, new ChunkLoadRequest
                    {
                        chunkPosition = area.ValueRO.originChunk + new int2(x, y)
                    });
                }
            }

            ecb.AddComponent<ChunkLoadAreaQueued>(entity);
        }
    }
}
