using Unity.Mathematics;

public static class CoalGeneratorPowerUtility
{
    public static float GetAvailableGeneration(
        float maximumGeneration,
        float remainingFuelEnergy,
        int storedCoalCount,
        float coalEnergyPerItem,
        float deltaTime)
    {
        float clampedMaximumGeneration = math.max(0f, maximumGeneration);
        float totalFuelEnergy = math.max(0f, remainingFuelEnergy) +
                                math.max(0, storedCoalCount) *
                                math.max(0f, coalEnergyPerItem);

        if (totalFuelEnergy <= 0f)
            return 0f;

        if (deltaTime <= 0f)
            return clampedMaximumGeneration;

        return math.min(
            clampedMaximumGeneration,
            totalFuelEnergy / deltaTime);
    }

    public static float GetActivationRatio(
        float requiredGeneration,
        float availableGeneration)
    {
        if (requiredGeneration <= 0f)
            return 0f;

        if (availableGeneration <= 0f)
            return 0f;

        return math.saturate(requiredGeneration / availableGeneration);
    }

    public static float GetConsumedFuelEnergy(
        float actualGeneration,
        float deltaTime)
    {
        return math.max(0f, actualGeneration) * math.max(0f, deltaTime);
    }
}
