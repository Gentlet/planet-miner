using System.Collections.Generic;

public static class ConstructionBuildingHotkeyUtility
{
    public static int GetButtonIndexForVisibleSlot(
        IReadOnlyList<bool> buttonVisibility,
        int visibleSlot)
    {
        if (buttonVisibility == null)
            return -1;

        if (visibleSlot < 0)
            return -1;

        int currentVisibleSlot = 0;

        for (int buttonIndex = 0;
             buttonIndex < buttonVisibility.Count;
             buttonIndex++)
        {
            if (!buttonVisibility[buttonIndex])
                continue;

            if (currentVisibleSlot == visibleSlot)
                return buttonIndex;

            currentVisibleSlot++;
        }

        return -1;
    }
}
