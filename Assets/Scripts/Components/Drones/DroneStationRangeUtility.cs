using Unity.Mathematics;

public static class DroneStationRangeUtility
{
    public static GridBounds GetActivityBounds(
        int2 stationCell,
        int2 activityRangeInChunks)
    {
        int2 normalizedRange = math.max(activityRangeInChunks, int2.zero);
        int2 stationChunk = ChunkUtility.ToChunkPosition(stationCell);
        int2 minimumChunk = stationChunk - normalizedRange;
        int2 maximumChunk = stationChunk + normalizedRange;
        int2 minimumCell = minimumChunk * GameConstants.chunkSize;
        int2 maximumCell =
            (maximumChunk + new int2(1)) * GameConstants.chunkSize -
            new int2(1);
        return new GridBounds(minimumCell, maximumCell);
    }
}
