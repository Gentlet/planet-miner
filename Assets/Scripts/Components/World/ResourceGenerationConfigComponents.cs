using Unity.Entities;

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
