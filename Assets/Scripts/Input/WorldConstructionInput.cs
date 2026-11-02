using System;
using UnityEngine.InputSystem;

[Flags]
public enum WorldConstructionCommandEnum : byte
{
    None = 0,
    Rotate = 1,
    Copy = 2,
    RangeDemolish = 4,
    Cancel = 8
}

/// <summary>현재 입력 갱신의 건설 단축키다. 전체 UI 차단과 Mouse 기여분의 포인터 차단을 적용한 값이며 게임 요청은 아니다.</summary>
public readonly struct WorldConstructionInput
{
    public readonly WorldConstructionCommandEnum Commands;
    public readonly bool SettingsModifier;
    public readonly int SelectedSlot;

    public WorldConstructionInput(WorldConstructionCommandEnum commands, bool settingsModifier, int selectedSlot)
    {
        Commands = commands;
        SettingsModifier = settingsModifier;
        SelectedSlot = selectedSlot;
    }

    public bool Has(WorldConstructionCommandEnum command) => (Commands & command) != 0;

    internal static readonly string[] ActionNames =
    {
        "Rotate", "Copy", "RangeDemolish", "Cancel", "SettingsModifier",
        "SelectSlot1", "SelectSlot2", "SelectSlot3", "SelectSlot4", "SelectSlot5",
        "SelectSlot6", "SelectSlot7", "SelectSlot8", "SelectSlot9", "SelectSlot10"
    };

    internal static bool TryValidate(InputActionMap map, out string error)
    {
        error = null;
        if (map == null)
        {
            error = "Construction 입력 맵이 필요합니다.";
            return false;
        }
        foreach (string name in ActionNames)
        {
            if (map.FindAction(name) == null)
            {
                error = "Construction 입력 동작이 없습니다: " + name;
                return false;
            }
        }
        return true;
    }

    internal static WorldConstructionInput Read(InputActionMap map, bool blocked, WorldConstructionPointerFilter pointerFilter)
    {
        if (blocked)
        {
            return default;
        }
        WorldConstructionCommandEnum commands = default;
        if (WasPressed(map.FindAction("Rotate"), pointerFilter))
        {
            commands |= WorldConstructionCommandEnum.Rotate;
        }
        if (WasPressed(map.FindAction("Copy"), pointerFilter))
        {
            commands |= WorldConstructionCommandEnum.Copy;
        }
        if (WasPressed(map.FindAction("RangeDemolish"), pointerFilter))
        {
            commands |= WorldConstructionCommandEnum.RangeDemolish;
        }
        if (WasPressed(map.FindAction("Cancel"), pointerFilter))
        {
            commands |= WorldConstructionCommandEnum.Cancel;
        }
        int slot = 0;
        for (int i = 5; i < ActionNames.Length; i++)
        {
            if (WasPressed(map.FindAction(ActionNames[i]), pointerFilter))
            {
                slot = i - 4;
                break;
            }
        }
        InputAction modifier = map.FindAction("SettingsModifier");
        return new WorldConstructionInput(commands, modifier.IsPressed() && pointerFilter.AllowsHeldModifier(modifier), slot);
    }

    private static bool WasPressed(InputAction action, WorldConstructionPointerFilter pointerFilter)
    {
        return action.WasPressedThisFrame() && pointerFilter.Allows(action);
    }
}
