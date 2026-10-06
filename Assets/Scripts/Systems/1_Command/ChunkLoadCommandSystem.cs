using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: Command에서 청크 로드 요청을 중복 제거하고 청크 생성 수명주기를 소유한다.
/// 입력·생성자: 초기 부트스트랩/외부 입력의 요청 큐와 ResourceGenerationCommandSystem이 EndCommand에 게시한 완료 알림.
/// 출력·소유권: GeneratedChunkTracker의 완료 Map/대기 Pending과 준비/완료 버퍼를 갱신한다. 새 좌표는 준비 대기에만 등록한다.
/// 이용·정리: ResourceGenerationCommandSystem이 준비 버퍼를 소비한다. 요청/완료 알림은 Clear하고 대기는 실제 완료 알림을 받을 때 해제한다.
/// 가시화: 이 시스템은 직접 엔티티를 스폰하지 않는다. Tracker의 Persistent 집합은 시스템 종료 시 해제하며 아직 대기 중인 청크를 완료로 간주하지 않는다.
/// </summary>
[UpdateInGroup(typeof(CommandGroup))]
public partial struct ChunkLoadCommandSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        // GeneratedChunkTracker 싱글톤 엔티티가 없으면 자동 생성
        if (!SystemAPI.HasSingleton<GeneratedChunkTracker>())
        {
            var trackerEntity = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponentData(trackerEntity, new GeneratedChunkTracker
            {
                Map = new NativeParallelHashSet<int2>(256, Allocator.Persistent),
                Pending = new NativeParallelHashSet<int2>(256, Allocator.Persistent)
            });
            state.EntityManager.AddBuffer<GeneratedChunkReadyElement>(trackerEntity);
            state.EntityManager.AddBuffer<GeneratedChunkCompletedElement>(trackerEntity);
        }
    }

    public void OnDestroy(ref SystemState state)
    {
        if (SystemAPI.TryGetSingleton<GeneratedChunkTracker>(out var tracker))
        {
            if (tracker.Map.IsCreated)
            {
                tracker.Map.Dispose();
            }
            if (tracker.Pending.IsCreated)
            {
                tracker.Pending.Dispose();
            }
        }
    }

    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingletonRW<GeneratedChunkTracker>(out var trackerRw))
        {
            return;
        }

        Entity trackerEntity = SystemAPI.GetSingletonEntity<GeneratedChunkTracker>();
        var readyBuffer = state.EntityManager.GetBuffer<GeneratedChunkReadyElement>(trackerEntity);

        // 완료 알림은 스폰 명령 뒤에 같은 ECB로 기록되어 실제 반영 이후에만 도착한다.
        var completedBuffer = state.EntityManager.GetBuffer<GeneratedChunkCompletedElement>(trackerEntity);
        for (int i = 0; i < completedBuffer.Length; i++)
        {
            int2 coord = completedBuffer[i].ChunkCoord;
            trackerRw.ValueRW.Map.Add(coord);
            trackerRw.ValueRW.Pending.Remove(coord);
        }
        completedBuffer.Clear();

        if (!SystemAPI.HasSingleton<ChunkLoadRequestQueue>())
        {
            return;
        }

        Entity queueEntity = SystemAPI.GetSingletonEntity<ChunkLoadRequestQueue>();
        var requestBuffer = state.EntityManager.GetBuffer<ChunkLoadRequestElement>(queueEntity);

        if (requestBuffer.IsEmpty)
        {
            return;
        }

        for (int i = 0; i < requestBuffer.Length; i++)
        {
            int2 coord = requestBuffer[i].ChunkCoord;

            // 이미 자원/지형이 생성된 청크는 조용히 드롭 (Idempotent Drop)
            if (trackerRw.ValueRW.Map.Contains(coord))
            {
                continue;
            }

            if (!trackerRw.ValueRW.Pending.Add(coord))
            {
                continue;
            }

            // 준비 대기 요청은 소비자가 처리할 때까지 보존한다.
            readyBuffer.Add(new GeneratedChunkReadyElement(coord));
        }

        // Consume-on-Apply: 요청 버퍼 비우기
        requestBuffer.Clear();
    }
}
