using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 월드 초기화 시점에 ResourceGenerationSettings의 InitialChunkSize를 읽어
/// (0, 0) 기준 N×N 청크 영역에 대한 로드 요청을 ChunkLoadRequestQueue에 1회 인큐하는 부트스트랩 시스템.
/// </summary>
[UpdateInGroup(typeof(InitializationSystemGroup))]
[UpdateAfter(typeof(WorldGenerationConfigLoadSystem))]
public partial struct InitialChunkLoadBootstrapSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<ResourceGenerationSettings>();
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    public void OnUpdate(ref SystemState state)
    {
        var settings = SystemAPI.GetSingleton<ResourceGenerationSettings>();

        // 큐 엔티티 및 버퍼 준비
        Entity queueEntity;
        if (!SystemAPI.HasSingleton<ChunkLoadRequestQueue>())
        {
            queueEntity = state.EntityManager.CreateEntity(typeof(ChunkLoadRequestQueue));
            state.EntityManager.AddBuffer<ChunkLoadRequestElement>(queueEntity);
        }
        else
        {
            queueEntity = SystemAPI.GetSingletonEntity<ChunkLoadRequestQueue>();
        }

        var requestBuffer = state.EntityManager.GetBuffer<ChunkLoadRequestElement>(queueEntity);

        // N×N 초기 청크 영역 계산 ((0, 0) 중심 대칭)
        int size = settings.InitialChunkSize > 0 ? settings.InitialChunkSize : 3;
        int half = size / 2;
        int startX = -half;
        int startY = -half;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int2 chunkCoord = new int2(startX + x, startY + y);
                requestBuffer.Add(new ChunkLoadRequestElement(chunkCoord));
            }
        }

        state.Enabled = false;
    }
}
