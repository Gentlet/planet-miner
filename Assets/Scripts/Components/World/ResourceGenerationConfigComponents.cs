using Unity.Entities;

/// <summary>
/// 역할·목적: 자원 품목별 생성 가중치·광맥 반경·셀 채움 확률·매장량 범위를 정의한다.
/// 부착 엔티티: ResourceGenerationSettings가 있는 World 설정 엔티티의 버퍼다.
/// 생성: WorldGenerationConfigLoadSystem(Initialization)이 WorldGenerationConfigLoader로 검증하여 게시한다.
/// 이용: ResourceGenerationCommandSystem(Command)이 청크별 자원 후보/수량을 샘플링하고 PrefabDatabaseInitializationSystem(Initialization)이 필수 자원 프리팹 품목을 검증한다.
/// 제거: 자원 생성으로 설정 항목을 소비하지 않는다. 버퍼는 설정 엔티티/World 수명을 따른다.
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
