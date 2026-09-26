using Unity.Entities;

/// <summary>
/// 월드 절차적 생성 및 자원 광맥 배치를 위한 전역 설정 싱글톤 컴포넌트 (Unmanaged / Blittable).
/// </summary>
public struct ResourceGenerationSettings : IComponentData
{
    public uint WorldSeed;

    public ResourceGenerationSettings(uint worldSeed)
    {
        WorldSeed = worldSeed;
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
