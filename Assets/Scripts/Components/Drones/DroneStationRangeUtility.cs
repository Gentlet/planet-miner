using Unity.Mathematics;

public static class DroneStationRangeUtility
{
    public static GridBounds GetActivityBounds(
        int2 stationCell,
        int2 footprintSize,
        DirectionEnum direction,
        int2 activityRangeInChunks)
    {
        int2 normalizedRange = math.max(activityRangeInChunks, int2.zero);
        int2 normalizedSize = BuildingFootprintUtility.NormalizeSize(
            footprintSize);
        int2 maximumLocalOffset = normalizedSize - new int2(1);
        int2 firstCorner = BuildingFootprintUtility.RotateOffset(
            int2.zero,
            direction);
        int2 secondCorner = BuildingFootprintUtility.RotateOffset(
            new int2(maximumLocalOffset.x, 0),
            direction);
        int2 thirdCorner = BuildingFootprintUtility.RotateOffset(
            new int2(0, maximumLocalOffset.y),
            direction);
        int2 fourthCorner = BuildingFootprintUtility.RotateOffset(
            maximumLocalOffset,
            direction);
        int2 footprintMinimum = stationCell + math.min(
            math.min(firstCorner, secondCorner),
            math.min(thirdCorner, fourthCorner));
        int2 footprintMaximum = stationCell + math.max(
            math.max(firstCorner, secondCorner),
            math.max(thirdCorner, fourthCorner));
        int2 rangeInCells = normalizedRange * GameConstants.chunkSize;
        return new GridBounds(
            footprintMinimum - rangeInCells,
            footprintMaximum + rangeInCells);
    }
}
