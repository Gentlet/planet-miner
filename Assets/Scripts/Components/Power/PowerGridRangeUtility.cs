using Unity.Entities;
using Unity.Mathematics;

public static class PowerGridRangeUtility
{
    public static GridBounds GetBounds(int2 center, int2 range)
    {
        return new GridBounds(center - range, center + range);
    }

    public static bool ArePolesConnected(
        PowerPoleTopologyData first,
        PowerPoleTopologyData second)
    {
        return AreCentersConnected(
            first.Center,
            first.ConnectionRange,
            second.Center,
            second.ConnectionRange);
    }

    public static bool AreCentersConnected(
        int2 firstCenter,
        int2 firstConnectionRange,
        int2 secondCenter,
        int2 secondConnectionRange)
    {
        return GetBounds(firstCenter, firstConnectionRange)
                   .Contains(secondCenter) ||
               GetBounds(secondCenter, secondConnectionRange)
                   .Contains(firstCenter);
    }

    public static bool TryGetPowerPoleConfig(
        DynamicBuffer<PowerPoleConfigElement> configs,
        out PowerPoleConfigElement config)
    {
        for (int i = 0; i < configs.Length; i++)
        {
            if (configs[i].buildingType != BuildingTypeEnum.PowerPole)
                continue;

            config = configs[i];
            return true;
        }

        config = default;
        return false;
    }

    public static long GetSquaredDistance(int2 first, int2 second)
    {
        long deltaX = (long)first.x - second.x;
        long deltaY = (long)first.y - second.y;
        return deltaX * deltaX + deltaY * deltaY;
    }
}
