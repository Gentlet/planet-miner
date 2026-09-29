using Unity.Entities;

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
