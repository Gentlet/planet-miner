using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// 대기 중인 청크의 광맥을 계산하고 스폰 및 완료 알림을 EndCommand ECB에 기록한다.
/// 
/// [책임]
/// - CommandGroup(Phase 1)에서 ChunkLoadCommandSystem 바로 뒤에 실행되어,
///   필수 프리팹 전체가 준비될 때까지 요청을 유지한다.
/// - 불필요한 중간 요청 엔티티(ResourceSpawnRequest) 생성/삭제 오버헤드를 배제하고 직접 엔티티 스폰.
/// - EndCommandEntityCommandBufferSystem을 통해 구조적 변경을 일괄 처리하여,
///   동일 프레임 Phase 2~4 및 SynchronizationGroup(Phase 6) 진입 전 자원 엔티티가 월드에 존재하도록 보장.
///   ResourceSpatialIndex 등록은 Synchronization에서 이루어지며, 완료 알림은 다음 Command에서 소비한다.
/// </summary>
[UpdateInGroup(typeof(CommandGroup))]
[UpdateAfter(typeof(ChunkLoadCommandSystem))]
public partial struct ResourceGenerationCommandSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<ResourceGenerationSettings>();
        state.RequireForUpdate<GeneratedChunkTracker>();
        state.RequireForUpdate<ResourcePrefabDatabase>();
    }

    public void OnUpdate(ref SystemState state)
    {
        Entity trackerEntity = SystemAPI.GetSingletonEntity<GeneratedChunkTracker>();
        var readyBuffer = state.EntityManager.GetBuffer<GeneratedChunkReadyElement>(trackerEntity);

        if (readyBuffer.IsEmpty)
        {
            return;
        }

        Entity prefabDbEntity = SystemAPI.GetSingletonEntity<ResourcePrefabDatabase>();
        if (!state.EntityManager.HasBuffer<ResourcePrefabElement>(prefabDbEntity))
        {
            return;
        }

        var prefabs = state.EntityManager.GetBuffer<ResourcePrefabElement>(prefabDbEntity);
        Entity settingsEntity = SystemAPI.GetSingletonEntity<ResourceGenerationSettings>();
        if (!state.EntityManager.HasBuffer<ResourceGenerationConfigElement>(settingsEntity))
        {
            return;
        }

        var configs = state.EntityManager.GetBuffer<ResourceGenerationConfigElement>(settingsEntity);
        if (configs.IsEmpty)
        {
            return;
        }

        var ecbSystem = state.World.GetExistingSystemManaged<EndCommandEntityCommandBufferSystem>();
        if (ecbSystem == null)
        {
            return;
        }

        using var resolvedPrefabs = new NativeArray<Entity>(configs.Length, Allocator.Temp);
        if (!ResourceGenerationUtility.TryResolvePrefabs(configs, prefabs, resolvedPrefabs))
        {
            return;
        }

        // 잘못된 엔티티/Transform으로 ECB가 부분 재생되지 않도록 기록 전에 확인한다.
        for (int i = 0; i < resolvedPrefabs.Length; i++)
        {
            Entity prefab = resolvedPrefabs[i];
            if (prefab == Entity.Null)
            {
                continue;
            }
            if (!state.EntityManager.Exists(prefab))
            {
                return;
            }
            if (!state.EntityManager.HasComponent<LocalTransform>(prefab))
            {
                return;
            }
        }

        ResourceGenerationSettings settings = SystemAPI.GetSingleton<ResourceGenerationSettings>();
        var ecb = ecbSystem.CreateCommandBuffer();
        for (int i = 0; i < readyBuffer.Length; i++)
        {
            int2 chunkCoord = readyBuffer[i].ChunkCoord;
            ResourceGenerationUtility.GenerateChunkResources(
                ref ecb,
                chunkCoord,
                settings.WorldSeed,
                configs,
                resolvedPrefabs);
            ecb.AppendToBuffer(trackerEntity, new GeneratedChunkCompletedElement { ChunkCoord = chunkCoord });
        }

        // 전달은 소비하되 Pending은 유지한다. 완료 알림을 받는 ChunkLoadCommandSystem만 해제한다.
        readyBuffer.Clear();
    }
}
