using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// 역할·목적: 시드와 후보 청크/자원 종류로 광맥을 계산하고 타깃 청크 안의 자원 실물만 생성 기록한다.
/// 입력·출력: 자원 설정과 등록 프리팹/해결된 참조 배열을 읽어 위치·품목·잔량·Transform을 호출자의 ECB에 기록한다.
/// 이용: ResourceGenerationCommandSystem이 청크 생성 요청에서 호출한다. 필요한 프리팹 조회 실패는 생성 기록 전에 false로 반환한다.
/// 수명·경계: 후보별 RNG는 시드 해시로 재생성하고 주변 후보 청크를 평가하므로 로드 순서가 광맥 경계를 바꾸지 않는다.
/// 임시 참조 배열은 호출 범위에서 Dispose하며 자원 생성은 EndCommand Playback에 확정한다.
/// </summary>
public static class ResourceGenerationUtility
{
    /// <summary>
    /// 월드 시드, 후보 청크 좌표, 자원 종류를 조합한 결정론적 해시 시드를 생성합니다.
    /// </summary>
    public static uint HashSeed(uint worldSeed, int2 chunkPosition, ItemTypeEnum type)
    {
        uint hash = worldSeed == 0 ? 1u : worldSeed;
        hash ^= (uint)chunkPosition.x * 73856093u;
        hash ^= (uint)chunkPosition.y * 19349663u;
        hash ^= (uint)type * 83492791u;

        return hash == 0 ? 1u : hash;
    }

    /// <summary>
    /// 설정 목록에서 모든 자원의 최대 패치 반경을 구합니다.
    /// </summary>
    public static int GetMaxPatchRadius(in DynamicBuffer<ResourceGenerationConfigElement> configs)
    {
        int maxRadius = 0;
        for (int i = 0; i < configs.Length; i++)
        {
            if (!IsValidConfig(configs[i]))
            {
                continue;
            }
            maxRadius = math.max(maxRadius, configs[i].MaxPatchRadius);
        }
        return maxRadius;
    }

    /// <summary>
    /// 자원 생성 설정 항목의 유효성을 검사합니다.
    /// </summary>
    public static bool IsValidConfig(in ResourceGenerationConfigElement config)
    {
        return config.ResourceType > ItemTypeEnum.None &&
               config.Weight > 0f &&
               ResourceGenerationConfigLookupUtility.IsPatchRadiusSupported(config.MinPatchRadius, config.MaxPatchRadius) &&
               ResourceGenerationConfigLookupUtility.IsAmountRangeSupported(config.MinAmount, config.MaxAmount);
    }

    /// <summary>
    /// 지정된 타깃 청크에 대해 시드 기반 광맥을 계산하고,
    /// 프리팹 데이터베이스를 참조하여 타깃 청크 내부에 포함되는 자원 셀에 대해 프리팹을 인스턴스화합니다.
    /// 필요한 프리팹이 하나라도 없으면 아무 스폰 명령도 기록하지 않습니다.
    /// </summary>
    public static bool GenerateChunkResources(
        ref EntityCommandBuffer ecb,
        int2 targetChunkCoord,
        uint worldSeed,
        in DynamicBuffer<ResourceGenerationConfigElement> configs,
        in DynamicBuffer<ResourcePrefabElement> prefabs)
    {
        using var resolvedPrefabs = new NativeArray<Entity>(configs.Length, Allocator.Temp);
        if (!TryResolvePrefabs(configs, prefabs, resolvedPrefabs))
        {
            return false;
        }

        GenerateChunkResources(ref ecb, targetChunkCoord, worldSeed, configs, resolvedPrefabs);
        return true;
    }

    /// <summary>업데이트당 한 번 조회하여 후보 청크마다 같은 프리팹 버퍼를 재검색하지 않는다.</summary>
    public static bool TryResolvePrefabs(
        in DynamicBuffer<ResourceGenerationConfigElement> configs,
        in DynamicBuffer<ResourcePrefabElement> prefabs,
        NativeArray<Entity> resolvedPrefabs)
    {
        for (int i = 0; i < configs.Length; i++)
        {
            resolvedPrefabs[i] = Entity.Null;
            if (!IsValidConfig(configs[i]))
            {
                continue;
            }
            if (!PrefabLookupUtility.TryGetResourcePrefab(prefabs, configs[i].ResourceType, out var prefab))
            {
                return false;
            }
            resolvedPrefabs[i] = prefab;
        }
        return true;
    }

    public static void GenerateChunkResources(
        ref EntityCommandBuffer ecb,
        int2 targetChunkCoord,
        uint worldSeed,
        in DynamicBuffer<ResourceGenerationConfigElement> configs,
        NativeArray<Entity> resolvedPrefabs)
    {
        // 무효 입력을 정상적인 빈 생성으로 처리하지 않고 ECB 기록 전에 거절한다.
        ChunkUtility.GetChunkBounds(targetChunkCoord, out int2 minCell, out int2 maxCell);
        for (int i = 0; i < configs.Length; i++)
        {
            ResourceGenerationConfigElement config = configs[i];
            if (!ResourceGenerationConfigLookupUtility.IsPatchRadiusSupported(config.MinPatchRadius, config.MaxPatchRadius) ||
                !ResourceGenerationConfigLookupUtility.IsAmountRangeSupported(config.MinAmount, config.MaxAmount))
            {
                throw new ArgumentException("Resource generation parameters exceed the supported ranges.", nameof(configs));
            }
        }

        int maxPatchRadius = GetMaxPatchRadius(configs);
        int neighborRange = maxPatchRadius / ChunkUtility.ChunkSize + 1;
        var occupied = new NativeParallelHashSet<int2>(ChunkUtility.CellCount, Allocator.Temp);

        for (int y = -neighborRange; y <= neighborRange; y++)
        {
            for (int x = -neighborRange; x <= neighborRange; x++)
            {
                int2 candidateChunk = targetChunkCoord + new int2(x, y);

                for (int i = 0; i < configs.Length; i++)
                {
                    TryGeneratePatchFromCandidateChunk(
                        ref ecb,
                        minCell,
                        maxCell,
                        candidateChunk,
                        worldSeed,
                        configs[i],
                        resolvedPrefabs[i],
                        ref occupied);
                }
            }
        }

        occupied.Dispose();
    }

    /// <summary>
    /// 특정 후보 청크에서 발생한 광맥 패치를 계산하고, 타깃 청크 내부에 들어오는 셀만 추출하여 프리팹 엔티티를 인스턴스화합니다.
    /// </summary>
    private static void TryGeneratePatchFromCandidateChunk(
        ref EntityCommandBuffer ecb,
        int2 minCell,
        int2 maxCell,
        int2 candidateChunk,
        uint worldSeed,
        in ResourceGenerationConfigElement config,
        Entity prefabEntity,
        ref NativeParallelHashSet<int2> occupied)
    {
        if (!IsValidConfig(config))
            return;

        // 같은 후보 광맥의 난수 흐름을 타깃 청크마다 재현한다. 어느 청크를 먼저 요청했는지 전역 RNG에 의존하지 않는다.
        var random = new Unity.Mathematics.Random(HashSeed(worldSeed, candidateChunk, config.ResourceType));

        if (random.NextFloat() > math.saturate(config.Weight))
            return;

        int radius = random.NextInt(config.MinPatchRadius, config.MaxPatchRadius + 1);
        long patchCenterX = (long)candidateChunk.x * ChunkUtility.ChunkSize + random.NextInt(0, ChunkUtility.ChunkSize);
        long patchCenterY = (long)candidateChunk.y * ChunkUtility.ChunkSize + random.NextInt(0, ChunkUtility.ChunkSize);
        long squaredRadius = (long)radius * radius;

        for (int y = -radius; y <= radius; y++)
        {
            for (int x = -radius; x <= radius; x++)
            {
                long squaredDistance = (long)x * x + (long)y * y;
                if (squaredDistance > squaredRadius)
                    continue;
                if (random.NextFloat() > math.saturate(config.CellFillChance))
                    continue;

                int amount = random.NextInt(config.MinAmount, config.MaxAmount + 1);
                long cellX = patchCenterX + x;
                long cellY = patchCenterY + y;

                // 광맥 전체를 동일하게 계산하되 이번 청크 부분만 실체화한다. 설정/광맥이 겹친 셀은 첫 품목 하나만 생성한다.
                if (cellX < minCell.x || cellX > maxCell.x || cellY < minCell.y || cellY > maxCell.y)
                    continue;

                // 대상 청크 경계를 통과한 뒤에만 int 셀로 변환한다. 주변 후보는 경계 밖에 있을 수 있다.
                int2 cell = new int2((int)cellX, (int)cellY);
                if (!occupied.Add(cell))
                    continue;

                Entity entity = ecb.Instantiate(prefabEntity);
                ecb.SetComponent(entity, LocalTransform.FromPosition(new float3(cell.x, cell.y, 0f)));
                ecb.AddComponent(entity, new GridPosition(cell));
                ecb.AddComponent(entity, new ResourceNode(config.ResourceType, amount));
            }
        }
    }
}
