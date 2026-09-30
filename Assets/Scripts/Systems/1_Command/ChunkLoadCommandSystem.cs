using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 청크 로드 요청을 소비하여 중복을 O(1)로 제거하고,
/// 신규 청크를 대기 상태로 등록하고, 이전 EndCommand에서 게시된 완료 알림을 확정하는 수명주기 소유자.
/// 
/// [책임]
/// - CommandGroup(Phase 1)에서 실행되어 모든 외부 청크 로드 요청(초기 부트스트랩, 카메라 이동 등)을 일괄 처리.
/// - 이미 생성된 청크 좌표는 안전하게 조용히 드롭(Idempotent Drop).
/// - 대기 중인 요청도 병합하며, 완료 알림이 도착하기 전에는 생성 완료로 취급하지 않음.
/// - 처리 완료된 요청 버퍼는 Consume-on-Apply 원칙에 따라 즉시 비움(Clear).
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
