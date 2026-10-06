using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// 역할·목적: Command에서 청크 준비 요청의 광맥을 계산하여 자원 실물 생성과 완료 알림을 기록한다.
/// 입력·생성자: ChunkLoadCommandSystem의 GeneratedChunkReadyElement, WorldGeneration 설정과 베이킹된 ResourcePrefabDatabase.
/// 출력·소유권: ResourceGenerationUtility의 직접 스폰과 같은 순서의 완료 알림을 EndCommand ECB에 기록한다. Tracker의 완료 Map은 쓰지 않는다.
/// 이용·정리: 준비 버퍼만 Clear하고 Pending은 유지한다. 다음 Command의 ChunkLoadCommandSystem이 재생된 완료 알림으로 완료 상태를 확정한다.
/// 가시화: 필수 프리팹/Transform 준비 전에는 요청을 보존한다. 자원은 EndCommand에 실체화하고 인덱스 등록은 Synchronization에서 수행한다.
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
