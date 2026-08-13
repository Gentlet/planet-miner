using NUnit.Framework;

public class CoalGeneratorPowerTests
{
    [Test]
    public void NoFuelProvidesNoAvailableGeneration()
    {
        Assert.That(
            CoalGeneratorPowerUtility.GetAvailableGeneration(
                100f,
                0f,
                0,
                600f,
                1f),
            Is.EqualTo(0f));
    }

    [Test]
    public void NoDemandConsumesNoFuelEnergy()
    {
        float activationRatio =
            CoalGeneratorPowerUtility.GetActivationRatio(0f, 100f);
        float consumedEnergy =
            CoalGeneratorPowerUtility.GetConsumedFuelEnergy(
                100f * activationRatio,
                1f);

        Assert.That(activationRatio, Is.EqualTo(0f));
        Assert.That(consumedEnergy, Is.EqualTo(0f));
    }

    [Test]
    public void PartialLoadUsesProportionalFuelEnergy()
    {
        float activationRatio =
            CoalGeneratorPowerUtility.GetActivationRatio(25f, 100f);
        float actualGeneration = 100f * activationRatio;
        float consumedEnergy =
            CoalGeneratorPowerUtility.GetConsumedFuelEnergy(
                actualGeneration,
                2f);

        Assert.That(activationRatio, Is.EqualTo(0.25f));
        Assert.That(actualGeneration, Is.EqualTo(25f));
        Assert.That(consumedEnergy, Is.EqualTo(50f));
    }

    [Test]
    public void MultipleGeneratorsUseSameActivationRatio()
    {
        float activationRatio =
            CoalGeneratorPowerUtility.GetActivationRatio(100f, 200f);

        Assert.That(100f * activationRatio, Is.EqualTo(50f));
        Assert.That(100f * activationRatio, Is.EqualTo(50f));
    }

    [Test]
    public void RemainingFuelCapsGenerationAtExhaustionBoundary()
    {
        float availableGeneration =
            CoalGeneratorPowerUtility.GetAvailableGeneration(
                100f,
                25f,
                0,
                600f,
                1f);
        float consumedEnergy =
            CoalGeneratorPowerUtility.GetConsumedFuelEnergy(
                availableGeneration,
                1f);

        Assert.That(availableGeneration, Is.EqualTo(25f));
        Assert.That(consumedEnergy, Is.EqualTo(25f));
    }
}
