using System;
using Unity.Mathematics;
using UnityEngine;

[Flags]
public enum WorldPointerButtonStateEnum
{
    None = 0,
    Pressed = 1,
    Held = 2,
    Released = 4,
    Cancelled = 8
}

/// <summary>입력 컨트롤러가 카메라 반영 후 한 번 전달한다. 좌표가 없을 때도 Cancelled는 소비해야 한다.</summary>
public readonly struct WorldInputFrame
{
    public readonly int FrameNumber;
    public readonly bool HasWorldPosition;
    public readonly Vector2 ScreenPosition;
    public readonly Vector3 WorldPosition;
    public readonly int2 GridCell;
    public readonly WorldPointerButtonStateEnum Primary;
    public readonly WorldPointerButtonStateEnum Secondary;

    public WorldInputFrame(int frameNumber, bool hasWorldPosition, Vector2 screenPosition,
        Vector3 worldPosition, int2 gridCell, WorldPointerButtonStateEnum primary, WorldPointerButtonStateEnum secondary)
    {
        FrameNumber = frameNumber;
        HasWorldPosition = hasWorldPosition;
        ScreenPosition = screenPosition;
        WorldPosition = worldPosition;
        GridCell = gridCell;
        Primary = primary;
        Secondary = secondary;
    }
}

/// <summary>월드에서 접수한 누름만 유지한다. UI 진입으로 취소한 누름은 새 Press 전까지 재개하지 않는다.</summary>
public struct WorldPointerGesture
{
    private bool _accepted;

    public WorldPointerButtonStateEnum Advance(bool allowed, bool pressed, bool held, bool released)
    {
        if (!allowed)
        {
            return Cancel();
        }

        WorldPointerButtonStateEnum result = WorldPointerButtonStateEnum.None;
        if (pressed)
        {
            _accepted = true;
            result |= WorldPointerButtonStateEnum.Pressed;
        }

        if (!_accepted)
        {
            return result;
        }

        if (released)
        {
            result |= WorldPointerButtonStateEnum.Released;
            // 같은 입력 갱신의 해제 후 재누름은 최종 Held 상태의 새 제스처를 유지한다.
            if (!held)
            {
                _accepted = false;
                return result;
            }
        }

        if (held)
        {
            return result | WorldPointerButtonStateEnum.Held;
        }

        return result | Cancel();
    }

    public WorldPointerButtonStateEnum Cancel()
    {
        if (!_accepted)
        {
            return WorldPointerButtonStateEnum.None;
        }

        _accepted = false;
        return WorldPointerButtonStateEnum.Cancelled;
    }
}
