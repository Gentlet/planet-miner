using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 월드 셀의 바이옴/변형 선택 값. ECS 컴포넌트가 아닌 FloorBiomeSampler 반환 값이며 호출자가 표시/검사에 사용한다.
/// VariantIndex는 선택한 일반/전이 목록 내부의 로컬 인덱스다. GetVariant가 시작 오프셋과 결합하며 별도 셀 원본을 게시하지 않는다.
/// </summary>
public struct FloorTileSelection
{
    public int BiomeIndex;
    public int VariantIndex;
    public bool UsesTransitionVariant;

    public FloorTileSelection(int biomeIndex, int variantIndex, bool usesTransitionVariant)
    {
        BiomeIndex = biomeIndex;
        VariantIndex = variantIndex;
        UsesTransitionVariant = usesTransitionVariant;
    }
}

/// <summary>
/// 역할·목적: 월드 셀에서 바이옴 경계/전이와 가중 바닥 변형을 시드 기반으로 결정한다.
/// 입력·출력: 검증된 바닥 설정/바이옴/변형 버퍼를 읽어 FloorTileSelection 또는 선택 행을 반환한다.
/// 이용: V2FloorBiomePreview와 바닥 생성 테스트가 호출한다. 실제 바닥 청크 렌더링/셀 엔티티 Writer는 후속이다.
/// 수명·소유권: 전역 RNG/영속 캐시를 갱신하지 않는 순수 계산이다. 같은 시드·설정·셀은 호출 순서와 무관하게 같은 결과를 낸다.
/// </summary>
public static class FloorBiomeSampler
{
    private const uint BiomeSalt = 0x4F1BBCDCu;
    private const uint TransitionSalt = 0x0F2C7B4Du;
    private const uint TransitionVariantSalt = 0x3F84D5B5u;
    private const uint VariantSalt = 0xB5297A4Du;
    private const uint WarpXSalt = 0x68E31DA4u;
    private const uint WarpYSalt = 0x1B56C4E9u;

    public static FloorTileSelection SelectFloor(
        uint worldSeed,
        in FloorGenerationSettings settings,
        in DynamicBuffer<FloorBiomeElement> biomes,
        in DynamicBuffer<FloorVariantElement> variants,
        int2 worldCell)
    {
        // 경계는 연속 노이즈로 휘게 하고 변형은 셀 해시로 고정한다. 표시/청크 로드 순서가 선택을 바꾸지 않는다.
        float2 warpedCell = worldCell + GetBoundaryWarp(worldSeed, settings, worldCell);
        float regionSizeInCells = settings.BiomeRegionSizeInChunks * (float)ChunkUtility.ChunkSize;
        float2 regionCoordinate = warpedCell / regionSizeInCells;
        int2 currentRegion = (int2)math.floor(regionCoordinate);
        int currentBiomeIndex = SelectBiomeIndex(worldSeed, biomes, currentRegion);
        int biomeIndex = SelectTransitionBiomeIndex(worldSeed, settings, biomes, worldCell,
            regionCoordinate, currentRegion, currentBiomeIndex, regionSizeInCells,
            out bool isInTransition, out float distanceRatio);
        bool usesTransition = isInTransition &&
            HashToUnitFloat(Hash(worldSeed, worldCell.x, worldCell.y, TransitionVariantSalt)) <=
            1f - SmoothStep(distanceRatio);

        // 전이 목록은 버퍼 앞부분, 일반 변형은 바이옴 범위를 사용한다. 반환 인덱스는 이 범위 안의 로컬 값이다.
        int start = usesTransition ? 0 : biomes[biomeIndex].VariantStart;
        int count = usesTransition ? settings.TransitionVariantCount : biomes[biomeIndex].VariantCount;
        uint salt = usesTransition ? TransitionVariantSalt + 1u : VariantSalt + (uint)biomeIndex;
        float selection = HashToUnitFloat(Hash(worldSeed, worldCell.x, worldCell.y, salt));
        int variantIndex = SelectVariantIndex(variants, start, count, selection);
        return new FloorTileSelection(biomeIndex, variantIndex, usesTransition);
    }

    public static FloorVariantElement GetVariant(
        in FloorGenerationSettings settings,
        in DynamicBuffer<FloorBiomeElement> biomes,
        in DynamicBuffer<FloorVariantElement> variants,
        in FloorTileSelection selection)
    {
        int start = selection.UsesTransitionVariant ? 0 : biomes[selection.BiomeIndex].VariantStart;
        return variants[start + selection.VariantIndex];
    }

    private static int SelectTransitionBiomeIndex(
        uint worldSeed,
        in FloorGenerationSettings settings,
        in DynamicBuffer<FloorBiomeElement> biomes,
        int2 worldCell,
        float2 regionCoordinate,
        int2 currentRegion,
        int currentBiomeIndex,
        float regionSizeInCells,
        out bool isInTransition,
        out float distanceRatio)
    {
        isInTransition = false;
        distanceRatio = 1f;
        if (settings.TransitionWidthInChunks == 0) return currentBiomeIndex;

        float2 regionFraction = regionCoordinate - math.floor(regionCoordinate);
        float2 distanceToBoundary = math.min(regionFraction, 1f - regionFraction) * regionSizeInCells;
        bool isVerticalBoundary = distanceToBoundary.x <= distanceToBoundary.y;
        float boundaryDistance = isVerticalBoundary ? distanceToBoundary.x : distanceToBoundary.y;
        float halfTransitionWidthInCells = settings.TransitionWidthInChunks * ChunkUtility.ChunkSize * 0.5f;
        if (boundaryDistance >= halfTransitionWidthInCells) return currentBiomeIndex;

        int2 neighboringRegion = currentRegion;
        if (isVerticalBoundary) neighboringRegion.x += regionFraction.x < 0.5f ? -1 : 1;
        else neighboringRegion.y += regionFraction.y < 0.5f ? -1 : 1;
        int neighboringBiomeIndex = SelectBiomeIndex(worldSeed, biomes, neighboringRegion);
        if (neighboringBiomeIndex == currentBiomeIndex) return currentBiomeIndex;

        isInTransition = true;
        distanceRatio = math.saturate(boundaryDistance / halfTransitionWidthInCells);
        float nearBiomePreference = math.pow(SmoothStep(distanceRatio), settings.NearBiomePreferenceExponent);
        float currentBiomeProbability = math.lerp(0.5f, 1f, nearBiomePreference);
        float selection = HashToUnitFloat(Hash(worldSeed, worldCell.x, worldCell.y, TransitionSalt));
        return selection <= currentBiomeProbability ? currentBiomeIndex : neighboringBiomeIndex;
    }

    private static int SelectBiomeIndex(uint worldSeed, in DynamicBuffer<FloorBiomeElement> biomes, int2 region)
    {
        float totalWeight = 0f;
        for (int i = 0; i < biomes.Length; i++) totalWeight += biomes[i].SelectionWeight;
        float threshold = HashToUnitFloat(Hash(worldSeed, region.x, region.y, BiomeSalt)) * totalWeight;
        float accumulated = 0f;
        for (int i = 0; i < biomes.Length; i++)
        {
            accumulated += biomes[i].SelectionWeight;
            if (threshold < accumulated) return i;
        }
        return biomes.Length - 1;
    }

    private static int SelectVariantIndex(in DynamicBuffer<FloorVariantElement> variants, int start, int count, float selection)
    {
        float totalWeight = 0f;
        for (int i = 0; i < count; i++) totalWeight += variants[start + i].Weight;
        float threshold = selection * totalWeight;
        float accumulated = 0f;
        for (int i = 0; i < count; i++)
        {
            accumulated += variants[start + i].Weight;
            if (threshold < accumulated) return i;
        }
        return count - 1;
    }

    private static float2 GetBoundaryWarp(uint worldSeed, in FloorGenerationSettings settings, int2 worldCell)
    {
        if (settings.BoundaryNoiseAmplitudeInCells <= 0f) return float2.zero;
        float2 samplePosition = (float2)worldCell / settings.BoundaryNoiseScaleInCells;
        float x = (SampleValueNoise(worldSeed, samplePosition, WarpXSalt) * 2f - 1f) * settings.BoundaryNoiseAmplitudeInCells;
        float y = (SampleValueNoise(worldSeed, samplePosition, WarpYSalt) * 2f - 1f) * settings.BoundaryNoiseAmplitudeInCells;
        return new float2(x, y);
    }

    private static float SampleValueNoise(uint worldSeed, float2 samplePosition, uint salt)
    {
        int2 minimum = (int2)math.floor(samplePosition);
        float2 fraction = samplePosition - minimum;
        float2 smooth = new float2(SmoothStep(fraction.x), SmoothStep(fraction.y));
        float bottom = math.lerp(HashToUnitFloat(Hash(worldSeed, minimum.x, minimum.y, salt)),
            HashToUnitFloat(Hash(worldSeed, minimum.x + 1, minimum.y, salt)), smooth.x);
        float top = math.lerp(HashToUnitFloat(Hash(worldSeed, minimum.x, minimum.y + 1, salt)),
            HashToUnitFloat(Hash(worldSeed, minimum.x + 1, minimum.y + 1, salt)), smooth.x);
        return math.lerp(bottom, top, smooth.y);
    }

    private static uint Hash(uint worldSeed, int x, int y, uint salt)
    {
        uint hash = worldSeed == 0 ? 1u : worldSeed;
        hash ^= (uint)x * 0x9E3779B9u;
        hash ^= (uint)y * 0x85EBCA6Bu;
        hash ^= salt;
        hash ^= hash >> 16;
        hash *= 0x7FEB352Du;
        hash ^= hash >> 15;
        hash *= 0x846CA68Bu;
        hash ^= hash >> 16;
        return hash == 0 ? 1u : hash;
    }

    private static float HashToUnitFloat(uint hash) => (hash & 0x00FFFFFFu) / 16777216f;
    private static float SmoothStep(float value)
    {
        float clamped = math.saturate(value);
        return clamped * clamped * (3f - 2f * clamped);
    }
}
