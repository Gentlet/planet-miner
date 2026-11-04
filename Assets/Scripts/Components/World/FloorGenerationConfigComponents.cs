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
}

/// <summary>
/// 역할·목적: 바이옴 식별자·선택 가중치와 FloorVariantElement 안의 연속 변형 구간을 정의한다.
/// 부착 엔티티: FloorGenerationSettings가 있는 World 설정 엔티티의 버퍼다.
/// 생성: WorldGenerationConfigLoadSystem(Initialization)이 WorldGenerationConfigLoader로 구간 시작/개수를 확정하여 게시한다.
/// 이용: FloorBiomeSampler가 가중치로 바이옴과 해당 변형 구간을 선택한다. 제품 바닥과 진단 색상 표시는 같은 선택 규칙을 사용한다.
/// 제거: 샘플링으로 항목을 소비하지 않는다. 버퍼는 설정 엔티티/World 수명을 따른다.
/// </summary>
public struct FloorBiomeElement : IBufferElementData
{
    public FixedString64Bytes Id;
    public float SelectionWeight;
    public int VariantStart;
    public int VariantCount;
}

/// <summary>
/// 역할·목적: 바닥 스프라이트 Resources 경로와 구간 안의 선택 가중치를 저장한다.
/// 부착 엔티티: FloorGenerationSettings/FloorBiomeElement가 있는 World 설정 엔티티의 버퍼다.
/// 생성: WorldGenerationConfigLoadSystem(Initialization)이 전이 변형을 앞쪽에, 바이옴별 변형을 이후 연속 구간에 게시한다.
/// 이용: FloorBiomeSampler가 전이/바이옴 구간의 변형을 선택한다. 표시 소유자는 경로의 Sprite로 메시를 구성하며 버퍼를 읽기만 한다.
/// 제거: 샘플링으로 항목을 소비하지 않는다. 버퍼는 설정 엔티티/World 수명을 따른다.
/// </summary>
public struct FloorVariantElement : IBufferElementData
{
    public FixedString128Bytes SpriteResourcePath;
    public float Weight;
}
