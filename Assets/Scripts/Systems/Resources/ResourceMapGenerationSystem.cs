using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

[UpdateAfter(typeof(ChunkLoadAreaSystem))]
[UpdateBefore(typeof(ResourceSpawnSystem))]
public partial class ResourceMapGenerationSystem : SystemBase
{
    private ChunkMapSystem _chunkMap;

    protected override void OnCreate()
    {
        RequireForUpdate<ResourceChunkGenerationSettings>();
        RequireForUpdate<ResourceGenerationConfigElement>();
        RequireForUpdate<ChunkLoadRequest>();
    }

    protected override void OnUpdate()
    {
        if (_chunkMap == null)
            _chunkMap = World.GetExistingSystemManaged<ChunkMapSystem>();

        if (_chunkMap == null)
            return;

        ResourceChunkGenerationSettings settings = SystemAPI.GetSingleton<ResourceChunkGenerationSettings>();
        DynamicBuffer<ResourceGenerationConfigElement> configs = SystemAPI.GetSingletonBuffer<ResourceGenerationConfigElement>(true);
        EntityCommandBuffer ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(World.Unmanaged);

        foreach (var (request, requestEntity) in SystemAPI.Query<RefRO<ChunkLoadRequest>>().WithEntityAccess())
        {
            Chunk chunk = _chunkMap.GetOrCreateChunk(request.ValueRO.chunkPosition);

            if (!chunk.hasGeneratedResources)
            {
                GenerateChunkResources(ecb, chunk, settings, configs);
                chunk.MarkResourcesGenerated();
            }

            ecb.DestroyEntity(requestEntity);
        }
    }

    private void GenerateChunkResources(
        EntityCommandBuffer ecb,
        Chunk targetChunk,
        ResourceChunkGenerationSettings settings,
        DynamicBuffer<ResourceGenerationConfigElement> configs)
    {
        int maxPatchRadius = GetMaxPatchRadius(configs);
        int neighborRange = maxPatchRadius / ChunkUtility.chunkSize + 1;
        var occupied = new NativeParallelHashSet<int2>(ChunkUtility.cellCount, Allocator.Temp);

        for (int y = -neighborRange; y <= neighborRange; y++)
        {
            for (int x = -neighborRange; x <= neighborRange; x++)
            {
                int2 candidateChunk = targetChunk.chunkPosition + new int2(x, y);

                for (int i = 0; i < configs.Length; i++)
                {
                    TryGeneratePatchFromCandidateChunk(
                        ecb,
                        targetChunk,
                        candidateChunk,
                        settings.worldSeed,
                        configs[i],
                        occupied);
                }
            }
        }

        occupied.Dispose();
    }

    private void TryGeneratePatchFromCandidateChunk(
        EntityCommandBuffer ecb,
        Chunk targetChunk,
        int2 candidateChunk,
        uint worldSeed,
        ResourceGenerationConfigElement config,
        NativeParallelHashSet<int2> occupied)
    {
        if (!IsValidConfig(config))
            return;

        var random = new Random(HashSeed(worldSeed, candidateChunk, config.type));

        if (random.NextFloat() > math.saturate(config.weight))
            return;

        int minRadius = math.max(0, config.minPatchRadius);
        int maxRadius = math.max(minRadius, config.maxPatchRadius);
        int radius = random.NextInt(minRadius, maxRadius + 1);
        int2 patchCenter = candidateChunk * ChunkUtility.chunkSize + new int2(
            random.NextInt(0, ChunkUtility.chunkSize),
            random.NextInt(0, ChunkUtility.chunkSize));

        for (int y = -radius; y <= radius; y++)
        {
            for (int x = -radius; x <= radius; x++)
            {
                int2 offset = new int2(x, y);

                if (offset.x * offset.x + offset.y * offset.y > radius * radius)
                    continue;
                if (random.NextFloat() > math.saturate(config.cellFillChance))
                    continue;

                int2 cell = patchCenter + offset;

                if (!IsInsideChunk(cell, targetChunk) || !occupied.Add(cell))
                    continue;

                ChunkCell cellData = targetChunk.GetCellByWorldPosition(cell);

                if (cellData.hasResource)
                    continue;

                int minAmount = math.max(1, config.minAmount);
                int maxAmount = math.max(minAmount, config.maxAmount);
                Entity request = ecb.CreateEntity();
                ecb.AddComponent(request, new ResourceSpawnRequest
                {
                    type = config.type,
                    gridPosition = cell,
                    amount = random.NextInt(minAmount, maxAmount + 1)
                });
            }
        }
    }

    private static bool IsValidConfig(ResourceGenerationConfigElement config)
    {
        return config.type > ResourceTypeEnum.None &&
            config.type < ResourceTypeEnum.Count &&
            config.weight > 0f &&
            config.maxPatchRadius >= 0 &&
            config.maxAmount > 0;
    }

    private static bool IsInsideChunk(int2 cell, Chunk chunk)
    {
        int2 min = chunk.chunkPosition * ChunkUtility.chunkSize;
        int2 max = min + new int2(ChunkUtility.chunkSize - 1, ChunkUtility.chunkSize - 1);

        return cell.x >= min.x &&
            cell.x <= max.x &&
            cell.y >= min.y &&
            cell.y <= max.y;
    }

    private static int GetMaxPatchRadius(DynamicBuffer<ResourceGenerationConfigElement> configs)
    {
        int maxRadius = 0;

        for (int i = 0; i < configs.Length; i++)
            maxRadius = math.max(maxRadius, configs[i].maxPatchRadius);

        return maxRadius;
    }

    private static uint HashSeed(uint worldSeed, int2 chunkPosition, ResourceTypeEnum type)
    {
        uint hash = worldSeed == 0 ? 1u : worldSeed;
        hash ^= (uint)chunkPosition.x * 73856093u;
        hash ^= (uint)chunkPosition.y * 19349663u;
        hash ^= (uint)type * 83492791u;

        return hash == 0 ? 1u : hash;
    }
}
