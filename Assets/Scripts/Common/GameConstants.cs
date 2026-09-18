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
    /// 타일 정렬 및 부동소수점 오차 보정용 엡실론.
    /// </summary>
    public const float AlignmentEpsilon = 0.001f;
}
