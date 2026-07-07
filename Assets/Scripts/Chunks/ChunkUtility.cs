using Unity.Mathematics;

public static class ChunkUtility
{
    public const int cellCount = GameConstants.chunkCellCount;

    public static int2 ToChunkPosition(int2 cell)
    {
        return new int2(
            FloorDiv(cell.x, GameConstants.chunkSize),
            FloorDiv(cell.y, GameConstants.chunkSize));
    }

    public static int2 ToLocalCell(int2 cell)
    {
        int2 chunkPosition = ToChunkPosition(cell);
        return cell - chunkPosition * GameConstants.chunkSize;
    }

    public static int ToCellIndex(int2 localCell)
    {
        return localCell.y * GameConstants.chunkSize + localCell.x;
    }

    private static int FloorDiv(int value, int divisor)
    {
        int quotient = value / divisor;
        int remainder = value % divisor;

        if (remainder != 0 && value < 0)
            quotient--;

        return quotient;
    }
}
