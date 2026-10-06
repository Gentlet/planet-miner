using Unity.Entities;

/// <summary>
/// 역할·목적: 자원·바닥 샘플링 공통 월드 시드와 초기 로드 영역의 한 변 청크 수를 제공한다.
/// 부착 엔티티: ResourceGenerationConfigElement·FloorGenerationSettings/바이옴/변형 버퍼를 함께 가진 World 단일 설정 엔티티다.
/// 생성: WorldGenerationConfigLoadSystem(Initialization)이 WorldGenerationConfigLoader로 자원·바닥 설정과 함께 게시한다.
/// 이용: InitialChunkLoadBootstrapSystem(Initialization)이 InitialChunkSize×InitialChunkSize 요청을 만들고 ResourceGenerationCommandSystem(Command)이 WorldSeed로 자원을 생성한다. FloorBiomeSampler/진단 Preview도 시드를 공유한다.
/// 제거: 청크 생성 때 소비하지 않고 설정 엔티티/World 종료 시 함께 사라진다. InitialChunkSize는 타일 수가 아니라 청크 수다.
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
