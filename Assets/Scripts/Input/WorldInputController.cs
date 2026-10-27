using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>
/// 역할: 키보드/마우스를 UI 차단과 카메라 반영 뒤 프레임 입력으로 전달한다.
/// 수명: 입력 Action은 이 컴포넌트의 복제본만 활성화/해제한다. FrameReady 소비자는 OnEnable/OnDisable에서 구독/해제한다.
/// 경계: LateUpdate의 입력 전달은 ECS 상태 변경이 아니다. 후속 소비자는 기존 요청 API와 접수 시점을 따른다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CameraMovementController))]
public sealed class WorldInputController : MonoBehaviour
{
    [SerializeField] private InputActionAsset _inputActions;

    private readonly List<WorldInputUiBlocker> _uiBlockers = new List<WorldInputUiBlocker>();
    private InputActionAsset _ownedActions;
    private InputAction _move;
    private InputAction _speedBoost;
    private WorldInputEventScope _eventScope;
    private Camera _camera;
    private CameraMovementController _movement;
    private WorldPointerGesture _primary;
    private WorldPointerGesture _secondary;
    private bool _hasFocus;
    private bool _awaitingButtonRelease;
    // 버튼 해제까지의 거부와 다음 LateUpdate까지의 차단 이력은 서로 다른 수명이다.
    private bool _primarySuppressed;
    private bool _secondarySuppressed;
    private bool _primaryBlockedThisFrame;
    private bool _secondaryBlockedThisFrame;
    private float _pendingScrollY;
    // 게임 상태를 복제하지 않고 제스처가 속한 World의 수명만 구분한다.
    private World _pointerWorld;

    public bool IsReady => _ownedActions != null && isActiveAndEnabled;
    public WorldInputFrame CurrentFrame { get; private set; }
    public event Action<WorldInputFrame> FrameReady;

    private void OnEnable()
    {
        _camera = GetComponent<Camera>();
        _movement = GetComponent<CameraMovementController>();
        _hasFocus = Application.isFocused;
        _awaitingButtonRelease = true;
        _pointerWorld = null;
        if (_inputActions == null)
        {
            RejectConfiguration("Input Actions 참조가 필요합니다.");
            return;
        }

        _ownedActions = Instantiate(_inputActions);
        _ownedActions.hideFlags = HideFlags.HideAndDontSave;
        _ownedActions.bindingMask = InputBinding.MaskByGroup("Keyboard&Mouse");
        _move = _ownedActions.FindAction("Player/Move");
        if (_move == null)
        {
            RejectConfiguration("Player/Move가 없습니다.");
            return;
        }

        _speedBoost = _ownedActions.FindAction("Player/Sprint");
        if (_speedBoost == null)
        {
            RejectConfiguration("Player/Sprint가 없습니다.");
            return;
        }

        _move.Enable();
        _speedBoost.Enable();
        _eventScope = new WorldInputEventScope();
        InputSystem.onEvent += OnInputEvent;
    }

    private void OnDisable()
    {
        InputSystem.onEvent -= OnInputEvent;
        PublishCancellation();
        _pointerWorld = null;
        ClearPointerSuppression();
        _eventScope?.Dispose();
        _eventScope = null;
        if (_ownedActions == null)
        {
            return;
        }

        _ownedActions.Disable();
        if (Application.isPlaying)
        {
            Destroy(_ownedActions);
        }
        else
        {
            DestroyImmediate(_ownedActions);
        }

        _ownedActions = null;
        _move = null;
        _speedBoost = null;
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        _hasFocus = hasFocus;
        _awaitingButtonRelease = true;
        if (!hasFocus)
        {
            PublishCancellation();
            ClearPointerSuppression();
        }
    }

    private void LateUpdate()
    {
        ProcessFrame(Time.deltaTime);
    }

    private void ProcessFrame(float deltaTime)
    {
        float pendingScroll = _pendingScrollY;
        bool primaryBlocked = _primaryBlockedThisFrame;
        bool secondaryBlocked = _secondaryBlockedThisFrame;
        _pendingScrollY = 0f;
        _primaryBlockedThisFrame = false;
        _secondaryBlockedThisFrame = false;
        if (!IsReady || !_hasFocus)
        {
            PublishCancellation();
            return;
        }

        if (!_movement.CanProcessInput)
        {
            PublishCancellation();
            return;
        }

        World world = World.DefaultGameObjectInjectionWorld;
        bool contextChanged = false;
        if (!ReferenceEquals(_pointerWorld, world))
        {
            bool previousContext = _pointerWorld != null;
            _pointerWorld = world;
            if (previousContext)
            {
                _awaitingButtonRelease = true;
                contextChanged = true;
            }
        }

        Mouse mouse = Mouse.current;
        Vector2 pointer = mouse == null ? default : mouse.position.ReadValue();
        WorldInputUiState ui = ReadUiState(pointer);
        Vector2 movement = ui.BlocksAllInput ? Vector2.zero : _move.ReadValue<Vector2>();
        bool speedBoost = !ui.BlocksAllInput && _speedBoost.IsPressed();
        float scroll = 0f;
        if (mouse != null)
        {
            if (!ui.BlocksPointer)
            {
                scroll = ReadScrollSteps(pendingScroll);
            }
        }
        _movement.ApplyInput(movement, speedBoost, scroll, pointer, deltaTime);
        if (contextChanged)
        {
            PublishCancellation();
            return;
        }

        bool hasPosition = TryReadWorldPointer(world, mouse, ui.BlocksPointer, pointer, out Vector3 worldPosition, out int2 cell);

        if (mouse == null)
        {
            PublishCancellation();
            return;
        }

        if (_awaitingButtonRelease && !mouse.leftButton.isPressed && !mouse.rightButton.isPressed)
        {
            _awaitingButtonRelease = false;
        }

        bool allowed = hasPosition && !_awaitingButtonRelease;
        var primary = _primary.Advance(allowed && !_primarySuppressed && !primaryBlocked, mouse.leftButton.wasPressedThisFrame,
            mouse.leftButton.isPressed, mouse.leftButton.wasReleasedThisFrame);
        var secondary = _secondary.Advance(allowed && !_secondarySuppressed && !secondaryBlocked, mouse.rightButton.wasPressedThisFrame,
            mouse.rightButton.isPressed, mouse.rightButton.wasReleasedThisFrame);
        CurrentFrame = new WorldInputFrame(Time.frameCount, hasPosition, pointer, worldPosition, cell, primary, secondary);
        FrameReady?.Invoke(CurrentFrame);
    }

    public void RegisterUiBlocker(WorldInputUiBlocker blocker)
    {
        if (blocker == null)
        {
            throw new ArgumentNullException(nameof(blocker));
        }

        if (!_uiBlockers.Contains(blocker))
        {
            _uiBlockers.Add(blocker);
        }
    }

    private bool TryReadWorldPointer(World world, Mouse mouse, bool blocked, Vector2 pointer,
        out Vector3 worldPosition, out int2 cell)
    {
        worldPosition = default;
        cell = default;
        if (mouse == null)
        {
            return false;
        }

        if (blocked)
        {
            return false;
        }

        if (world == null)
        {
            return false;
        }

        if (!world.IsCreated)
        {
            return false;
        }

        return WorldPointerUtility.TryGetWorldPosition(_camera, pointer, out worldPosition) &&
            WorldPointerUtility.TryGetGridCell(worldPosition, out cell);
    }

    private void OnInputEvent(InputEventPtr inputEvent, InputDevice device)
    {
        if (!_hasFocus)
        {
            return;
        }

        if (device is not Mouse mouse)
        {
            return;
        }

        if (inputEvent.type != StateEvent.Type && inputEvent.type != DeltaStateEvent.Type)
        {
            return;
        }

        Vector2 position = mouse.position.ReadValue();
        if (mouse.position.ReadValueFromEvent(inputEvent, out Vector2 eventPosition))
        {
            position = eventPosition;
        }

        bool primaryHeld = mouse.leftButton.isPressed;
        bool secondaryHeld = mouse.rightButton.isPressed;
        if (mouse.leftButton.ReadValueFromEvent(inputEvent, out float primaryValue))
        {
            primaryHeld = primaryValue >= 0.5f;
        }

        if (mouse.rightButton.ReadValueFromEvent(inputEvent, out float secondaryValue))
        {
            secondaryHeld = secondaryValue >= 0.5f;
        }

        // UI가 닫히거나 같은 입력 갱신에서 포인터가 이동하기 전에 버튼별 차단 이력을 보존한다.
        bool blocked = ReadUiState(position).BlocksPointer || !_camera.pixelRect.Contains(position);
        // 휠은 UI에서 발생한 기여분을 제외하고 합산한다. 기본 World가 없어도 카메라는 조작할 수 있다.
        if (!blocked && mouse.scroll.ReadValueFromEvent(inputEvent, out Vector2 scroll))
        {
            _pendingScrollY += scroll.y;
        }

        World eventWorld = World.DefaultGameObjectInjectionWorld;
        if (eventWorld == null)
        {
            blocked = true;
        }
        else if (!eventWorld.IsCreated)
        {
            blocked = true;
        }
        if (blocked)
        {
            if (primaryHeld || mouse.leftButton.isPressed)
            {
                _primaryBlockedThisFrame = true;
                _primarySuppressed = true;
            }

            if (secondaryHeld || mouse.rightButton.isPressed)
            {
                _secondaryBlockedThisFrame = true;
                _secondarySuppressed = true;
            }
        }

        if (!primaryHeld)
        {
            _primarySuppressed = false;
        }

        if (!secondaryHeld)
        {
            _secondarySuppressed = false;
        }
    }

    private void ClearPointerSuppression()
    {
        _primarySuppressed = false;
        _secondarySuppressed = false;
        _primaryBlockedThisFrame = false;
        _secondaryBlockedThisFrame = false;
        _pendingScrollY = 0f;
    }

    public void UnregisterUiBlocker(WorldInputUiBlocker blocker)
    {
        _uiBlockers.Remove(blocker);
    }

    private WorldInputUiState ReadUiState(Vector2 pointer)
    {
        bool blocksPointer = false;
        bool blocksAll = false;
        foreach (WorldInputUiBlocker blocker in _uiBlockers)
        {
            if (blocker == null)
            {
                continue;
            }

            if (!blocker.isActiveAndEnabled)
            {
                continue;
            }

            WorldInputUiState state = blocker.ReadState(pointer);
            blocksPointer |= state.BlocksPointer;
            blocksAll |= state.BlocksAllInput;
        }

        return new WorldInputUiState(blocksPointer, blocksAll);
    }

    private static float ReadScrollSteps(float value)
    {
        // 기본 Uniform 설정은 한 칸이 1이다. Windows native 모드에서만 120 단위를 변환한다.
        if (InputSystem.settings.scrollDeltaBehavior == InputSettings.ScrollDeltaBehavior.KeepPlatformSpecificInputRange &&
            (Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor))
        {
            value /= 120f;
        }

        return value;
    }

    private void PublishCancellation()
    {
        WorldPointerButtonStateEnum primary = _primary.Cancel();
        WorldPointerButtonStateEnum secondary = _secondary.Cancel();
        CurrentFrame = new WorldInputFrame(Time.frameCount, false, default, default, default, primary, secondary);
        if (primary != WorldPointerButtonStateEnum.None || secondary != WorldPointerButtonStateEnum.None)
        {
            FrameReady?.Invoke(CurrentFrame);
        }
    }

    private void RejectConfiguration(string reason)
    {
        Debug.LogError($"{nameof(WorldInputController)}: {reason}", this);
        enabled = false;
    }
}
