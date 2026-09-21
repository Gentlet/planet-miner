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
    /// 스택 기반 FixedList512Bytes(최대 124개)와의 호환성 및 메모리 안정성을 보장합니다.
    /// </summary>
    public const int MaxStorageSlots = 120;
}
