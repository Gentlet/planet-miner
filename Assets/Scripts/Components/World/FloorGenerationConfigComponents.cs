using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: 바닥 바이옴 경계·전이 폭·잡음·변형 선택의 공통 파라미터다. ResourceGenerationSettings.WorldSeed를 공유한다.
/// 부착 엔티티: 자원 설정과 동일한 World 설정 엔티티다.
/// 생성: WorldGenerationConfigLoadSystem(Initialization)이 WorldGenerationConfigLoader로 자원 설정과 함께 게시한다.
/// 이용: FloorBiomeSampler가 셀의 바이옴/변형을 계산한다. FloorChunkRenderController는 생성 완료 청크의 메시를 구성하고 V2FloorBiomePreview는 진단 색상에 사용한다.
/// 제거: 샘플링으로 소비하지 않고 설정 엔티티/World 종료 시 함께 사라진다. TransitionVariantCount는 변형 버퍼 앞쪽 전이 전용 항목 수다.
/// </summary>
public struct FloorGenerationSettings : IComponentData
{
    public int BiomeRegionSizeInChunks;
    public int TransitionWidthInChunks;
    public float BoundaryNoiseScaleInCells;
    public float BoundaryNoiseAmplitudeInCells;
    public float NearBiomePreferenceExponent;
    public int TransitionVariantCount;
    public FloorBiomeCornerTiles CornerTiles;
}

/// <summary>
/// 두 바이옴의 공통 꼭짓점에서 고른 경계 Sprite 구간이다. 잔디 꼭짓점은 왼쪽 아래부터 1/2/4/8이며
/// 혼합 조합 1~14 순으로 저장한다. VariantStart가 0인 구성은 셀 단위 전이를 사용한다.
/// </summary>
public struct FloorBiomeCornerTiles
{
    public const int AllGrassMask = 15;
    public const int MixedTileCount = AllGrassMask - 1;

    public int GrassBiomeIndex;
    public int DirtBiomeIndex;
    public int VariantStart;

    public readonly bool IsConfigured => VariantStart != 0;
}

/// <summary>
/// 역할·목적: 바이옴 식별자·선택 가중치와 FloorVariantElement 안의 연속 변형 구간을 정의한다.
/// 부착 엔티티: FloorGenerationSettings가 있는 World 설정 엔티티의 버퍼다.
/// 생성: WorldGenerationConfigLoadSystem(Initialization)이 WorldGenerationConfigLoader로 구간 시작/개수를 확정하여 게시한다.
/// 이용: FloorBiomeSampler가 가중치로 바이옴과 해당 변형 구간을 선택한다.
/// 제거: 샘플링으로 항목을 소비하지 않는다. 버퍼는 설정 엔티티/World 수명을 따른다.
/// </summary>
public struct FloorBiomeElement : IBufferElementData
{
    public int Id;
    public float SelectionWeight;
    public int VariantStart;
    public int VariantCount;
}

/// <summary>
/// 역할·목적: 바닥 스프라이트 Resources 경로와 구간 안의 선택 가중치를 저장한다.
/// 부착 엔티티: FloorGenerationSettings/FloorBiomeElement가 있는 World 설정 엔티티의 버퍼다.
/// 생성: WorldGenerationConfigLoadSystem(Initialization)이 셀 단위 전이, 바이옴별 변형, 꼭짓점 경계 순으로 게시한다.
/// 이용: FloorBiomeSampler는 가중 변형을, FloorChunkMeshBuilder는 꼭짓점 경계 Sprite를 고른다.
/// 제거: 샘플링으로 항목을 소비하지 않는다. 버퍼는 설정 엔티티/World 수명을 따른다.
/// </summary>
public struct FloorVariantElement : IBufferElementData
{
    public FixedString128Bytes SpriteResourcePath;
    public float Weight;
}
