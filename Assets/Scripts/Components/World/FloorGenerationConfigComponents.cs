using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>월드 설정 엔티티에 함께 게시되는 바닥 생성 파라미터. 시드는 ResourceGenerationSettings.WorldSeed를 공유한다.</summary>
public struct FloorGenerationSettings : IComponentData
{
    public int BiomeRegionSizeInChunks;
    public int TransitionWidthInChunks;
    public float BoundaryNoiseScaleInCells;
    public float BoundaryNoiseAmplitudeInCells;
    public float NearBiomePreferenceExponent;
    public int TransitionVariantCount;
}

/// <summary>바이옴별 가중치와 FloorVariantElement 버퍼 내 연속 구간.</summary>
public struct FloorBiomeElement : IBufferElementData
{
    public FixedString64Bytes Id;
    public float SelectionWeight;
    public int VariantStart;
    public int VariantCount;
}

/// <summary>앞쪽 TransitionVariantCount개는 전이용, 나머지는 바이옴 순서대로 저장한다.</summary>
public struct FloorVariantElement : IBufferElementData
{
    public FixedString128Bytes SpriteResourcePath;
    public float Weight;
}
