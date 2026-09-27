using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 월드 절차적 생성 및 자원 광맥 배치를 위한 전역 설정 싱글톤 컴포넌트 (Unmanaged / Blittable).
/// </summary>
public struct ResourceGenerationSettings : IComponentData
{
    public uint WorldSeed;
    public int InitialChunkSize;

    public ResourceGenerationSettings(uint worldSeed, int initialChunkSize = 3)
    {
        WorldSeed = worldSeed;
        InitialChunkSize = initialChunkSize > 0 ? initialChunkSize : 3;
    }
}

/// <summary>
/// 자원 종류별 광맥 생성 파라미터 버퍼 엘리먼트 (Unmanaged / Blittable).
/// </summary>
public struct ResourceGenerationConfigElement : IBufferElementData
{
    public ItemTypeEnum ResourceType;
    public float Weight;
    public int MinPatchRadius;
    public int MaxPatchRadius;
    public float CellFillChance;
    public int MinAmount;
    public int MaxAmount;

    public ResourceGenerationConfigElement(
        ItemTypeEnum resourceType,
        float weight,
        int minPatchRadius,
        int maxPatchRadius,
        float cellFillChance,
        int minAmount,
        int maxAmount)
    {
        ResourceType = resourceType;
        Weight = weight;
        MinPatchRadius = minPatchRadius;
        MaxPatchRadius = maxPatchRadius;
        CellFillChance = cellFillChance;
        MinAmount = minAmount;
        MaxAmount = maxAmount;
    }
}

/// <summary>
/// DynamicBuffer로 게시된 자원 생성 설정을 Burst 호환 방식으로 조회하기 위한 유틸리티.
/// </summary>
public static class ResourceGenerationConfigLookupUtility
{
    public static bool TryGetConfig(
        in DynamicBuffer<ResourceGenerationConfigElement> buffer,
        ItemTypeEnum resourceType,
        out ResourceGenerationConfigElement result)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            if (buffer[i].ResourceType == resourceType)
            {
                result = buffer[i];
                return true;
            }
        }

        result = default;
        return false;
    }
}

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
/// 준비되지 않으면 유지하고, 스폰 및 완료 알림을 EndStateApply ECB에 기록한 뒤 소비한다.
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
/// Producer: ResourceGenerationCommandSystem의 EndStateApply ECB.
/// Consumer: 다음 Command의 ChunkLoadCommandSystem. 실제 스폰 반영 이후 완료를 확정하고 즉시 비운다.
/// </summary>
public struct GeneratedChunkCompletedElement : IBufferElementData
{
    public int2 ChunkCoord;
}
