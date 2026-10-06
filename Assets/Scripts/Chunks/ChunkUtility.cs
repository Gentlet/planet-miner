using Unity.Mathematics;

/// <summary>
/// 역할·목적: 월드 셀·청크·로컬 셀/배열 인덱스와 포함 경계를 같은 규칙으로 계산한다.
/// 입력·출력: GameConstants의 청크 크기를 사용한다. 월드 음수 좌표의 나눗셈은 양수 크기에 대한 floor division으로 처리하며 최대 셀 경계는 포함한다.
/// 이용·수명: 청크 요청·자원/바닥 생성·진단에서 호출하는 순수 계산이다. 청크 엔티티/버퍼를 만들거나 캐시를 유지하지 않는다.
/// </summary>
public static class ChunkUtility
{
    public const int CellCount = GameConstants.ChunkCellCount;
    public const int ChunkSize = GameConstants.ChunkSize;

    /// <summary>
    /// 월드 셀(타일) 좌표를 청크 좌표로 변환합니다.
    /// 음수 좌표에서도 floor division을 적용하여 정확한 청크 좌표를 산출합니다.
    /// </summary>
    public static int2 ToChunkPosition(int2 cell)
    {
        return new int2(
            FloorDiv(cell.x, ChunkSize),
            FloorDiv(cell.y, ChunkSize));
    }

    /// <summary>
    /// 월드 셀 좌표를 해당 청크 내부의 로컬 셀 좌표(0 ~ ChunkSize - 1)로 변환합니다.
    /// </summary>
    public static int2 ToLocalCell(int2 cell)
    {
        int2 chunkPosition = ToChunkPosition(cell);
        return cell - chunkPosition * ChunkSize;
    }

    /// <summary>
    /// 청크 로컬 셀 좌표를 1차원 인덱스(0 ~ ChunkCellCount - 1)로 변환합니다.
    /// </summary>
    public static int ToCellIndex(int2 localCell)
    {
        return localCell.y * ChunkSize + localCell.x;
    }

    /// <summary>
    /// 청크 좌표의 최소 타일 좌표와 최대 타일 좌표(Inclusive Bounds)를 반환합니다.
    /// </summary>
    public static void GetChunkBounds(int2 chunkCoord, out int2 minCell, out int2 maxCell)
    {
        minCell = chunkCoord * ChunkSize;
        maxCell = minCell + new int2(ChunkSize - 1, ChunkSize - 1);
    }

    /// <summary>
    /// 음수 좌표를 포함한 정수 나눗셈에서 내림(floor) 나눗셈을 수행합니다.
    /// </summary>
    public static int FloorDiv(int value, int divisor)
    {
        int quotient = value / divisor;
        int remainder = value % divisor;

        if (remainder != 0 && value < 0)
        {
            quotient--;
        }

        return quotient;
    }

    /// <summary>
    /// 특정 월드 셀 좌표가 지정된 청크 영역(16x16) 내부에 포함되는지 여부를 반환합니다.
    /// </summary>
    public static bool IsInsideChunk(int2 cell, int2 chunkCoord)
    {
        GetChunkBounds(chunkCoord, out int2 minCell, out int2 maxCell);
        return cell.x >= minCell.x && cell.x <= maxCell.x &&
               cell.y >= minCell.y && cell.y <= maxCell.y;
    }
}
