using Unity.Entities;
using Unity.Mathematics;

public struct ChunkLoadRequest : IComponentData
{
    public int2 chunkPosition;
}

public struct ChunkLoadArea : IComponentData
{
    public int2 originChunk;
    public int2 size;
}

public struct ChunkLoadAreaQueued : IComponentData
{
}
