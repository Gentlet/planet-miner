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
    private InputActionMap _constructionMap;
    private readonly WorldConstructionPointerFilter _constructionPointerFilter = new WorldConstructionPointerFilter();
    // Action 복제본의 수명과 분리하여 비활성화/재활성화에도 사용자가 적용한 키를 보존한다.
    private WorldInputBindingOverride[] _bindingOverrides = Array.Empty<WorldInputBindingOverride>();
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
    public uint SettingsRevision { get; private set; }
    public event Action<WorldInputFrame> FrameReady;

    public WorldInputSettings GetSettings()
    {
        return new WorldInputSettings
        {
            Camera = GetComponent<CameraMovementController>().GetSettings(),
            BindingOverrides = (WorldInputBindingOverride[])_bindingOverrides.Clone()
        };
    }

    public WorldInputBindingDescriptor[] GetRebindableBindings()
    {
        if (_inputActions == null)
        {
            throw new InvalidOperationException("Input Actions 참조가 필요합니다.");
        }
        return WorldInputBindingUtility.Describe(_inputActions, _bindingOverrides);
    }

    /// <summary>전체 스냅샷을 검증/준비한 후 교체한다. 잘못된 값은 기존 설정/입력을 보존해 거부하며 콜백의 외부 수명 변경은 되돌리지 않는다. 성공은 포인터 입력을 취소한다.</summary>
    public bool TryApplySettings(WorldInputSettings settings, out string error)
    {
        error = null;
        if (settings == null)
        {
            error = "입력 설정이 필요합니다.";
            return false;
        }
        CameraMovementSettings cameraSettings = settings.Camera;
        if (!cameraSettings.TryValidate(out error))
        {
            return false;
        }
        if (settings.BindingOverrides == null)
        {
            error = "키 설정 배열이 필요합니다. 기본 키는 빈 배열로 지정하세요.";
            return false;
        }
        var replacements = (WorldInputBindingOverride[])settings.BindingOverrides.Clone();
        // 활성 플래그만으로는 OnEnable의 준비 완료를 보장하지 않는다. 준비 전 호출은 키 프로필을 보관하고 수치만 적용한다.
        bool replaceLive = IsReady;
        var previous = _ownedActions;
        if (!TryCreateActions(replacements, replaceLive, out var actions, out var move, out var boost, out error))
        {
            return false;
        }

        // Enable은 공개 동기 콜백을 발생시킨다. 그 사이 비활성화/재초기화된 소유자에게 후보를 인계하지 않는다.
        if (replaceLive && (!IsReady || _ownedActions != previous))
        {
            ReleaseActions(actions);
            error = "설정 준비 중 입력 수명이 변경되어 적용하지 않았습니다.";
            return false;
        }
        if (replaceLive)
        {
            _ownedActions = actions;
            _move = move;
            _speedBoost = boost;
            ObserveConstructionActions(actions.FindActionMap("Construction", true));
        }
        else
        {
            ReleaseActions(actions);
        }
        _bindingOverrides = replacements;
        SettingsRevision++;
        GetComponent<CameraMovementController>().ApplyValidatedSettings(cameraSettings);
        ReleaseActions(previous);
        _awaitingButtonRelease = true;
        ClearPointerSuppression();
        // 현재 입력 갱신에서 이미 끝난 클릭도 다음 프레임의 새 Press로 재접수하지 않는다.
        _primaryBlockedThisFrame = true;
        _secondaryBlockedThisFrame = true;
        PublishCancellation();
        return true;
    }

    private void OnEnable()
    {
        _camera = GetComponent<Camera>();
        _movement = GetComponent<CameraMovementController>();
        _hasFocus = Application.isFocused;
        _awaitingButtonRelease = true;
        _pointerWorld = null;
        if (!TryCreateActions(_bindingOverrides, true, out var actions, out var move, out var boost, out string error))
        {
            RejectConfiguration(error);
            return;
        }
        _ownedActions = actions;
        _move = move;
        _speedBoost = boost;
        ObserveConstructionActions(actions.FindActionMap("Construction", true));
        _eventScope = new WorldInputEventScope();
        InputSystem.onEvent += OnInputEvent;
    }

    private void OnDisable()
    {
        InputSystem.onEvent -= OnInputEvent;
        ObserveConstructionActions(null);
        PublishCancellation();
        _pointerWorld = null;
        ClearPointerSuppression();
        _eventScope?.Dispose();
        _eventScope = null;
        ReleaseActions(_ownedActions);
        _ownedActions = null;
        _move = null;
        _speedBoost = null;
    }

    private void ObserveConstructionActions(InputActionMap map)
    {
        if (_constructionMap != null)
        {
            _constructionMap.actionTriggered -= _constructionPointerFilter.RecordAction;
        }
        _constructionPointerFilter.ResetActions();
        _constructionMap = map;
        if (_constructionMap != null)
        {
            _constructionMap.actionTriggered += _constructionPointerFilter.RecordAction;
        }
    }

    private bool TryCreateActions(WorldInputBindingOverride[] overrides, bool enable,
        out InputActionAsset actions, out InputAction move, out InputAction boost, out string error)
    {
        actions = null;
        move = null;
        boost = null;
        error = null;
        if (_inputActions == null)
        {
            error = "Input Actions 참조가 필요합니다.";
            return false;
        }
        var candidate = Instantiate(_inputActions);
        bool transferred = false;
        try
        {
            candidate.hideFlags = HideFlags.HideAndDontSave;
            candidate.bindingMask = InputBinding.MaskByGroup("Keyboard&Mouse");
            move = candidate.FindAction("Player/Move");
            if (move == null)
            {
                error = "Player/Move가 없습니다.";
                return false;
            }
            boost = candidate.FindAction("Player/Sprint");
            if (boost == null)
            {
                error = "Player/Sprint가 없습니다.";
                return false;
            }
            var construction = candidate.FindActionMap("Construction");
            if (!WorldConstructionInput.TryValidate(construction, out error))
            {
                return false;
            }
            candidate.RemoveAllBindingOverrides();
            if (!WorldInputBindingUtility.TryApplyOverrides(candidate, overrides, out error))
            {
                return false;
            }
            if (enable)
            {
                move.Enable();
                boost.Enable();
                construction.Enable();
            }
            actions = candidate;
            transferred = true;
            return true;
        }
        finally
        {
            if (!transferred)
            {
                ReleaseActions(candidate);
            }
        }
    }

    private static void ReleaseActions(InputActionAsset actions)
    {
        if (actions == null)
        {
            return;
        }
        actions.Disable();
        if (Application.isPlaying)
        {
            Destroy(actions);
        }
        else
        {
            DestroyImmediate(actions);
        }
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
                _constructionPointerFilter.CancelGestures();
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
        _constructionPointerFilter.BeginFrame(mouse, !hasPosition);
        var construction = WorldConstructionInput.Read(_constructionMap, ui.BlocksAllInput, _constructionPointerFilter);
        _constructionPointerFilter.EndFrame();
        CurrentFrame = new WorldInputFrame(Time.frameCount, hasPosition, pointer, worldPosition, cell, primary, secondary, construction);
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
        _constructionPointerFilter.RecordPointer(mouse, inputEvent, blocked);
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
        _constructionPointerFilter.CancelGestures();
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
