/// <summary>
/// PlanetMiner 전역 공통 게임 상수.
/// </summary>
public static class GameConstants
{
    public const int ChunkSize = 16;
    public const int ChunkCellCount = ChunkSize * ChunkSize;

    /// <summary>
    /// 벨트 위 아이템 간 최소 간격 (1타일 1.0f 기준, 최대 4개 수용 가능).
    /// </summary>
    public const float ItemSpacing = 0.25f;

    /// <summary>
    /// 벨트 1타일 당 최대 아이템 수용량 (1.0f / ItemSpacing).
    /// </summary>
    public const int MaxItemsPerBeltTile = (int)(1.0f / ItemSpacing);

    /// <summary>
    /// 타일 정렬 및 부동소수점 오차 보정용 엡실론.
    /// </summary>
    public const float AlignmentEpsilon = 0.001f;

    /// <summary>
    /// 단일 창고/저장고 건물이 보유할 수 있는 최대 슬롯 수.
    /// 스택 기반 FixedList512Bytes(최대 124개)와의 호환성 및 메모리 안정성을 보장.
    /// </summary>
    public const int MaxStorageSlots = 120;

    /// <summary>
    /// 프레임 히치나 대형 DeltaTime 발생 시 1프레임 내 단일 아이템이 연속 횡단할 수 있는 최대 타일 홉(Hop) 수.
    /// 무한 루프 방지 및 안전 상한으로 사용.
    /// </summary>
    public const int MaxTileHopsPerFrame = 4;

    /// <summary>
    /// 시뮬레이션 시스템에서 1프레임에 허용되는 최대 델타 타임(초).
    /// 프레임 드랍(Spike/Hitch) 시 터널링·오버슈팅 완화.
    /// </summary>
    public const float MaxSimulationDeltaTime = 0.1f;
}

