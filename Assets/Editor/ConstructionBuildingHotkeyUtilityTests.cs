using NUnit.Framework;

public class ConstructionBuildingHotkeyUtilityTests
{
    [Test]
    public void AllVisibleButtonsKeepTheirDisplayOrder()
    {
        bool[] visibility =
        {
            true, true, true, true, true,
            true, true, true, true, true
        };

        Assert.That(GetButtonIndex(visibility, 0), Is.EqualTo(0));
        Assert.That(GetButtonIndex(visibility, 4), Is.EqualTo(4));
        Assert.That(GetButtonIndex(visibility, 9), Is.EqualTo(9));
    }

    [Test]
    public void HiddenButtonsDoNotLeaveEmptyHotkeySlots()
    {
        bool[] visibility =
        {
            true, false, false, true, true,
            false, false, false, false, false
        };

        Assert.That(GetButtonIndex(visibility, 0), Is.EqualTo(0));
        Assert.That(GetButtonIndex(visibility, 1), Is.EqualTo(3));
        Assert.That(GetButtonIndex(visibility, 2), Is.EqualTo(4));
        Assert.That(GetButtonIndex(visibility, 3), Is.EqualTo(-1));
    }

    [Test]
    public void MappingReflectsVisibilityChangesImmediately()
    {
        bool[] visibility =
        {
            true, false, false, true, true
        };

        Assert.That(GetButtonIndex(visibility, 1), Is.EqualTo(3));

        visibility[1] = true;

        Assert.That(GetButtonIndex(visibility, 1), Is.EqualTo(1));
        Assert.That(GetButtonIndex(visibility, 2), Is.EqualTo(3));
    }

    [Test]
    public void InvalidVisibleSlotDoesNotSelectButton()
    {
        bool[] visibility = { true };

        Assert.That(GetButtonIndex(visibility, -1), Is.EqualTo(-1));
        Assert.That(GetButtonIndex(visibility, 1), Is.EqualTo(-1));
        Assert.That(
            ConstructionBuildingHotkeyUtility
                .GetButtonIndexForVisibleSlot(null, 0),
            Is.EqualTo(-1));
    }

    private static int GetButtonIndex(
        bool[] visibility,
        int visibleSlot)
    {
        return ConstructionBuildingHotkeyUtility
            .GetButtonIndexForVisibleSlot(visibility, visibleSlot);
    }
}
