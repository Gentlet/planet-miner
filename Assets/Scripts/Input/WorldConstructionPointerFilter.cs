using System.Collections.Generic;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;

/// <summary>건설 Action의 실제 Mouse 기여분과 UI 차단 이력을 입력 소유자의 수명 안에서 보존한다.</summary>
internal sealed class WorldConstructionPointerFilter
{
    private readonly Dictionary<InputAction, List<ButtonControl>> _mouseSourcesByAction = new Dictionary<InputAction, List<ButtonControl>>();
    private readonly List<ButtonControl> _compositeMouseSources = new List<ButtonControl>();
    // 해제까지의 거부와 다음 입력 프레임까지의 차단 이력은 수명이 다르다.
    private readonly HashSet<ButtonControl> _suppressed = new HashSet<ButtonControl>();
    private readonly HashSet<ButtonControl> _blockedThisFrame = new HashSet<ButtonControl>();
    private bool _awaitingRelease = true;
    private bool _cancelledThisFrame = true;
    private bool _pointerBlocked;

    public void RecordAction(InputAction.CallbackContext context)
    {
        if (!context.performed)
        {
            return;
        }
        InputAction action = context.action;
        // 해제 뒤 activeControl이 사라져도 누름을 만든 Mouse 기여분을 판정할 수 있게 보존한다.
        if (!_mouseSourcesByAction.TryGetValue(action, out var sources))
        {
            sources = new List<ButtonControl>();
            _mouseSourcesByAction.Add(action, sources);
        }
        sources.Clear();
        var bindings = action.bindings;
        for (int i = 0; i < bindings.Count; i++)
        {
            if (!bindings[i].isComposite)
            {
                if (!bindings[i].isPartOfComposite && InputControlPath.Matches(bindings[i].effectivePath, context.control))
                {
                    if (context.control.device is not Mouse)
                    {
                        sources.Clear();
                        return;
                    }
                    sources.Add((ButtonControl)context.control);
                }
                continue;
            }
            int end = i + 1;
            while (end < bindings.Count && bindings[end].isPartOfComposite)
            {
                end++;
            }
            if (TryCollectCompositeMouseSources(action, i + 1, end, context.control))
            {
                // 순수 Keyboard 조합도 같은 Action을 성립시키면 일반 UI 위에서 계속 사용할 수 있다.
                if (_compositeMouseSources.Count == 0)
                {
                    sources.Clear();
                    return;
                }
                foreach (var control in _compositeMouseSources)
                {
                    if (!sources.Contains(control))
                    {
                        sources.Add(control);
                    }
                }
            }
            i = end - 1;
        }
    }

    private bool TryCollectCompositeMouseSources(InputAction action, int first, int end, InputControl trigger)
    {
        _compositeMouseSources.Clear();
        bool matchesTrigger = false;
        for (int i = first; i < end; i++)
        {
            bool pressed = false;
            foreach (var control in action.controls)
            {
                if (control is not ButtonControl button)
                {
                    continue;
                }
                if (!button.isPressed || !InputControlPath.Matches(action.bindings[i].effectivePath, button))
                {
                    continue;
                }
                pressed = true;
                matchesTrigger |= ReferenceEquals(control, trigger);
                if (button.device is Mouse)
                {
                    _compositeMouseSources.Add(button);
                }
            }
            if (!pressed)
            {
                return false;
            }
        }
        return matchesTrigger;
    }

    public void RecordPointer(Mouse mouse, InputEventPtr inputEvent, bool blocked)
    {
        RecordButton(mouse.leftButton, inputEvent, blocked);
        RecordButton(mouse.rightButton, inputEvent, blocked);
        RecordButton(mouse.middleButton, inputEvent, blocked);
        RecordButton(mouse.forwardButton, inputEvent, blocked);
        RecordButton(mouse.backButton, inputEvent, blocked);
    }

    private void RecordButton(ButtonControl button, InputEventPtr inputEvent, bool blocked)
    {
        bool held = button.isPressed;
        if (button.ReadValueFromEvent(inputEvent, out float value))
        {
            held = value >= 0.5f;
        }
        UpdateButtonSuppression(button, held, blocked);
    }

    private void UpdateButtonSuppression(ButtonControl button, bool held, bool blocked)
    {
        if (blocked && (held || button.isPressed))
        {
            _suppressed.Add(button);
            _blockedThisFrame.Add(button);
        }
        if (!held)
        {
            _suppressed.Remove(button);
        }
    }

    public void BeginFrame(Mouse mouse, bool blocked)
    {
        // UI 표시만 바뀐 프레임도 버튼별 이력에 반영하여 해제 전 Held 재개를 막는다.
        UpdateButtonSuppression(mouse.leftButton, mouse.leftButton.isPressed, blocked);
        UpdateButtonSuppression(mouse.rightButton, mouse.rightButton.isPressed, blocked);
        UpdateButtonSuppression(mouse.middleButton, mouse.middleButton.isPressed, blocked);
        UpdateButtonSuppression(mouse.forwardButton, mouse.forwardButton.isPressed, blocked);
        UpdateButtonSuppression(mouse.backButton, mouse.backButton.isPressed, blocked);
        if (_awaitingRelease && !mouse.leftButton.isPressed && !mouse.rightButton.isPressed &&
            !mouse.middleButton.isPressed && !mouse.forwardButton.isPressed && !mouse.backButton.isPressed)
        {
            _awaitingRelease = false;
        }
        _pointerBlocked = blocked || _awaitingRelease || _cancelledThisFrame;
    }

    public bool Allows(InputAction action)
    {
        if (!_mouseSourcesByAction.TryGetValue(action, out var sources))
        {
            return true;
        }
        foreach (var control in sources)
        {
            if (IsPointerBlocked(control))
            {
                return false;
            }
        }
        return true;
    }

    public bool AllowsHeldModifier(InputAction action)
    {
        // Button의 양수 값이 유지되면 performed가 재발행되지 않으므로 현재 대체 버튼을 읽는다.
        foreach (var control in action.controls)
        {
            if (control is not ButtonControl button)
            {
                continue;
            }
            if (!button.isPressed)
            {
                continue;
            }
            if (button.device is not Mouse)
            {
                return true;
            }
            if (!IsPointerBlocked(button))
            {
                return true;
            }
        }
        return false;
    }

    private bool IsPointerBlocked(ButtonControl control)
    {
        // press는 leftButton과 상태만 공유하므로 이력 조회는 물리 버튼 참조로 맞춘다.
        var mouse = (Mouse)control.device;
        if (ReferenceEquals(control, mouse.press))
        {
            control = mouse.leftButton;
        }
        return _pointerBlocked || _suppressed.Contains(control) || _blockedThisFrame.Contains(control);
    }

    public void EndFrame()
    {
        _blockedThisFrame.Clear();
        _cancelledThisFrame = false;
    }

    public void CancelGestures()
    {
        _suppressed.Clear();
        _blockedThisFrame.Clear();
        _awaitingRelease = true;
        _cancelledThisFrame = true;
    }

    public void ResetActions()
    {
        _mouseSourcesByAction.Clear();
        _compositeMouseSources.Clear();
        CancelGestures();
    }
}
