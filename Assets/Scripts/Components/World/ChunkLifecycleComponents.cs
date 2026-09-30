using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// ChunkLoadCommandSystem이 소유하는 자원 생성 수명주기. 공간 점유 인덱스와는 별개다.
/// Map은 ECB 반영이 확인된 청크, Pending은 준비 대기 또는 ECB 반영 대기 중인 청크다.
/// </summary>
public struct GeneratedChunkTracker : IComponentData
{
    public NativeParallelHashSet<int2> Map;
    public NativeParallelHashSet<int2> Pending;
}

/// <summary>
/// Producer: ChunkLoadCommandSystem / Consumer: ResourceGenerationCommandSystem (Command).
/// 준비되지 않으면 유지하고, 스폰 및 완료 알림을 EndCommand ECB에 기록한 뒤 소비한다.
/// </summary>
public struct GeneratedChunkReadyElement : IBufferElementData
{
    public int2 ChunkCoord;

    public GeneratedChunkReadyElement(int2 chunkCoord)
    {
        ChunkCoord = chunkCoord;
    }
}

/// <summary>
/// Producer: ResourceGenerationCommandSystem의 EndCommand ECB.
/// Consumer: 다음 Command의 ChunkLoadCommandSystem. 실제 스폰 반영 이후 완료를 확정하고 즉시 비운다.
/// </summary>
public struct GeneratedChunkCompletedElement : IBufferElementData
{
    public int2 ChunkCoord;
}
