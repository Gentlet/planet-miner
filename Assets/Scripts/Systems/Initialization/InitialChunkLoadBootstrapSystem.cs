using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: Initialization에서 시작 영역의 청크 로드 요청을 한 번 넣는다.
/// 입력·생성자: WorldGenerationConfigLoadSystem이 게시한 ResourceGenerationSettings.InitialChunkSize.
/// 출력·소유권: 없으면 World 단일 ChunkLoadRequestQueue/버퍼를 즉시 만들고 원점 주변 N×N 좌표를 추가한다.
/// 이용·정리: ChunkLoadCommandSystem이 요청을 소비하고 ResourceGenerationCommandSystem으로 넘긴다. 부트스트랩은 제출 뒤 비활성화한다.
/// 요청 게시에는 ECB를 사용하지 않는다. 실제 자원 생성 완료나 공간 인덱스 등록을 이 단계에서 확정하지 않는다.
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

        // 원점의 음방향 절반부터 N×N을 요청한다. 짝수 크기는 정수 좌표상 음방향에 한 칸 더 걸친다.
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
