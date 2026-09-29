using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 청크 로드 요청 큐 싱글톤 마커 컴포넌트 (Unmanaged / Blittable).
/// </summary>
public struct ChunkLoadRequestQueue : IComponentData
{
}

/// <summary>
/// 청크 로드 요청 큐의 개별 청크 좌표 버퍼 엘리먼트 (Unmanaged / Blittable).
/// </summary>
public struct ChunkLoadRequestElement : IBufferElementData
{
    public int2 ChunkCoord;

    public ChunkLoadRequestElement(int2 chunkCoord)
    {
        ChunkCoord = chunkCoord;
    }
}
