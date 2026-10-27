using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

/// <summary>가상 입력과 실제 카메라/UI 트리로 입력 전달 경계를 검사한다. Player 조작/화면 검증은 아니다.</summary>
public class WorldInputPipelineTests
{
    private GameObject _cameraObject;
    private Camera _camera;
    private CameraMovementController _movement;
    private WorldInputController _input;
    private InputActionAsset _sourceActions;
    private Mouse _mouse;
    private Keyboard _keyboard;
    private World _world;
    private World _previousWorld;
    private WorldInputTestWindow _window;
    private GameObject _uiObject;
    private PanelSettings _panelSettings;
    private InputSettings _previousInputSettings;
    private bool _previousEventMerging;
    private object _inputFixture;

    [SetUp]
    public void SetUp()
    {
        // 공식 격리 런타임을 사용해야 EditMode에서도 Player와 같은 버튼 프레임을 검증할 수 있다.
        // predefined Editor 어셈블리는 autoReferenced=false인 TestFramework를 직접 참조할 수 없다.
        System.Type fixtureType = System.Type.GetType(
            "UnityEngine.InputSystem.InputTestFixture, Unity.InputSystem.TestFramework", true);
        _inputFixture = System.Activator.CreateInstance(fixtureType);
        Invoke(_inputFixture, "Setup");
        _previousInputSettings = InputSystem.settings;
        _previousEventMerging = _previousInputSettings.disableRedundantEventsMerging;
        _previousWorld = World.DefaultGameObjectInjectionWorld;
        _world = new World("World input pipeline test");
        World.DefaultGameObjectInjectionWorld = _world;
        _mouse = InputSystem.AddDevice<Mouse>();
        _keyboard = InputSystem.AddDevice<Keyboard>();
        _sourceActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions");
        Assert.IsNotNull(_sourceActions);

        _cameraObject = new GameObject("World input camera");
        _cameraObject.SetActive(false);
        _camera = _cameraObject.AddComponent<Camera>();
        _camera.orthographic = true;
        _camera.orthographicSize = 5f;
        _camera.pixelRect = new Rect(0, 0, Screen.width, Screen.height);
        _cameraObject.transform.position = new Vector3(0, 0, -10);
        _movement = _cameraObject.AddComponent<CameraMovementController>();
        _input = _cameraObject.AddComponent<WorldInputController>();
        var serialized = new SerializedObject(_input);
        serialized.FindProperty("_inputActions").objectReferenceValue = _sourceActions;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        _cameraObject.SetActive(true);
        // EditMode에서는 MonoBehaviour의 런타임 갱신을 자동 실행하지 않고 필요한 진입점을 호출한다.
        if (!_movement.CanProcessInput)
        {
            Invoke(_movement, "OnEnable");
        }

        if (!_input.IsReady)
        {
            Invoke(_input, "OnEnable");
        }
        Invoke(_input, "OnApplicationFocus", true);
        StepMouse(_camera.pixelRect.center, false);
    }

    [TearDown]
    public void TearDown()
    {
        if (_window != null)
        {
            _window.Close();
        }

        if (_uiObject != null)
        {
            Object.DestroyImmediate(_uiObject);
        }

        if (_panelSettings != null)
        {
            Object.DestroyImmediate(_panelSettings);
        }

        if (_input != null)
        {
            Invoke(_input, "OnDisable");
        }

        if (_cameraObject != null)
        {
            Object.DestroyImmediate(_cameraObject);
        }

        if (_mouse != null)
        {
            InputSystem.RemoveDevice(_mouse);
        }

        if (_keyboard != null)
        {
            InputSystem.RemoveDevice(_keyboard);
        }
        World.DefaultGameObjectInjectionWorld = _previousWorld;
        if (_world != null && _world.IsCreated)
        {
            _world.Dispose();
        }

        if (_inputFixture != null)
        {
            Invoke(_inputFixture, "TearDown");
            _inputFixture = null;
        }
    }

    [Test]
    public void PointerPipeline_PublishesFinalZoomCoordinatesAndCancelsWhenWorldEnds()
    {
        Vector2 pointer = _camera.WorldToScreenPoint(new Vector3(-0.51f, 0.49f, 0));
        StepMouse(pointer, true);
        Assert.IsTrue(_input.CurrentFrame.HasWorldPosition);
        Assert.AreEqual(new int2(-1, 0), _input.CurrentFrame.GridCell);
        Assert.That(_input.CurrentFrame.Primary.HasFlag(WorldPointerButtonStateEnum.Pressed));
        Vector3 beforeZoom = _input.CurrentFrame.WorldPosition;

        StepMouse(pointer, true, 1f);
        Assert.AreEqual(4f, _camera.orthographicSize, 0.0001f);
        Assert.Less(Vector3.Distance(beforeZoom, _input.CurrentFrame.WorldPosition), 0.0001f);
        Assert.AreEqual(new int2(-1, 0), _input.CurrentFrame.GridCell);

        _world.Dispose();
        _world = new World("Replacement input world");
        World.DefaultGameObjectInjectionWorld = _world;
        StepMouse(pointer, true);
        Assert.IsFalse(_input.CurrentFrame.HasWorldPosition);
        Assert.AreEqual(WorldPointerButtonStateEnum.Cancelled, _input.CurrentFrame.Primary);
        StepMouse(pointer, true);
        Assert.AreEqual(WorldPointerButtonStateEnum.None, _input.CurrentFrame.Primary);
        StepMouse(pointer, false);
        StepMouse(pointer, true);
        Assert.That(_input.CurrentFrame.Primary.HasFlag(WorldPointerButtonStateEnum.Pressed));
        _world.Dispose();
        StepMouse(pointer, true);
        Assert.AreEqual(WorldPointerButtonStateEnum.Cancelled, _input.CurrentFrame.Primary);
        StepMouse(pointer, false);
        QueueMouse(pointer, true);
        InputSystem.Update();
        _world = new World("World after input without a world");
        World.DefaultGameObjectInjectionWorld = _world;
        Invoke(_input, "LateUpdate");
        Assert.AreEqual(WorldPointerButtonStateEnum.None, _input.CurrentFrame.Primary);
        StepMouse(pointer, true);
        Assert.AreEqual(WorldPointerButtonStateEnum.None, _input.CurrentFrame.Primary);
        StepMouse(pointer, false);
        StepMouse(pointer, true);
        Assert.That(_input.CurrentFrame.Primary.HasFlag(WorldPointerButtonStateEnum.Pressed));
    }

    [Test]
    public void FocusLoss_CancelsOnceAndRequiresAReleasedButtonBeforeNewPress()
    {
        Vector2 pointer = _camera.pixelRect.center;
        StepMouse(pointer, true);
        int cancellations = 0;
        _input.FrameReady += frame =>
        {
            if (frame.Primary == WorldPointerButtonStateEnum.Cancelled)
            {
                cancellations++;
            }
        };

        Invoke(_input, "OnApplicationFocus", false);
        Invoke(_input, "LateUpdate");
        Assert.AreEqual(1, cancellations);
        Invoke(_input, "OnApplicationFocus", true);
        StepMouse(pointer, true);
        Assert.AreEqual(WorldPointerButtonStateEnum.None, _input.CurrentFrame.Primary);
        StepMouse(pointer, false);
        StepMouse(pointer, true);
        Assert.That(_input.CurrentFrame.Primary.HasFlag(WorldPointerButtonStateEnum.Pressed));
    }

    [Test]
    public void CameraMovement_NormalizesDiagonalBoostAndKeepsThePointerAnchorAtZoomLimits()
    {
        Vector2 pointer = _camera.ViewportToScreenPoint(new Vector3(0.8f, 0.75f, 0));
        _movement.ApplyInput(Vector2.one, true, 0, pointer, 1f);
        Assert.AreEqual(16f, ((Vector2)_camera.transform.position).magnitude, 0.0001f);
        Assert.AreEqual(-10f, _camera.transform.position.z);
        Assert.IsTrue(WorldPointerUtility.TryGetWorldPosition(_camera, pointer, out Vector3 anchor));
        _movement.ApplyInput(Vector2.zero, false, 100, pointer, 0f);
        Assert.AreEqual(2f, _camera.orthographicSize);
        Assert.IsTrue(WorldPointerUtility.TryGetWorldPosition(_camera, pointer, out Vector3 afterNear));
        Assert.Less(Vector3.Distance(anchor, afterNear), 0.0001f);
        _movement.ApplyInput(Vector2.zero, false, -100, pointer, 0f);
        Assert.AreEqual(20f, _camera.orthographicSize);
        Assert.IsTrue(WorldPointerUtility.TryGetWorldPosition(_camera, pointer, out Vector3 afterFar));
        Assert.Less(Vector3.Distance(anchor, afterFar), 0.0001f);
        Assert.IsTrue(WorldPointerUtility.TryGetGridCell(new Vector3(-0.5f, 0.5f, 0), out int2 boundary));
        Assert.AreEqual(new int2(0, 1), boundary);
    }

    [Test]
    public void DisablingInput_ReleasesOnlyItsOwnActionsAndReenableDoesNotRepeatOldPress()
    {
        InputAction sharedMove = _sourceActions.FindAction("Player/Move", true);
        bool wasEnabled = sharedMove.enabled;
        sharedMove.Enable();
        try
        {
            Assert.IsTrue(InputSystem.settings.disableRedundantEventsMerging);
            Assert.AreEqual(_previousEventMerging, _previousInputSettings.disableRedundantEventsMerging);
            using (var otherReader = new WorldInputEventScope())
            {
                StepMouse(_camera.pixelRect.center, true);
                _input.enabled = false;
                Invoke(_input, "OnDisable");
                Assert.IsTrue(InputSystem.settings.disableRedundantEventsMerging);
            }

            Assert.AreSame(_previousInputSettings, InputSystem.settings);
            Assert.AreEqual(_previousEventMerging, InputSystem.settings.disableRedundantEventsMerging);
            Assert.IsTrue(sharedMove.enabled);
            Assert.IsFalse(_input.CurrentFrame.HasWorldPosition);
            _input.enabled = true;
            if (!_input.IsReady)
            {
                Invoke(_input, "OnEnable");
            }
            Invoke(_input, "OnApplicationFocus", true);
            StepMouse(_camera.pixelRect.center, true);
            Assert.AreEqual(WorldPointerButtonStateEnum.None, _input.CurrentFrame.Primary);
            Assert.IsTrue(sharedMove.enabled);
        }
        finally
        {
            if (!wasEnabled)
            {
                sharedMove.Disable();
            }
        }
    }

    [UnityTest]
    public IEnumerator RegisteredUi_RejectsPressBeforeUiClosesAndCancelsTransitInOneInputUpdate()
    {
        _camera.pixelRect = new Rect(0, 0, Screen.width, Screen.height);
        _panelSettings = Object.Instantiate(AssetDatabase.LoadAssetAtPath<PanelSettings>(
            "Assets/UI Toolkit/PanelSettings.asset"));
        _panelSettings.scaleMode = PanelScaleMode.ConstantPixelSize;
        _panelSettings.scale = 1;
        _uiObject = new GameObject("Registered input UI");
        _uiObject.SetActive(false);
        UIDocument document = _uiObject.AddComponent<UIDocument>();
        document.panelSettings = _panelSettings;
        WorldInputUiBlocker blocker = _uiObject.AddComponent<WorldInputUiBlocker>();
        var serialized = new SerializedObject(blocker);
        serialized.FindProperty("_worldInput").objectReferenceValue = _input;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        _uiObject.SetActive(true);
        Invoke(blocker, "OnEnable");
        ConfigureUiRoot(document.rootVisualElement);
        yield return null;
        PrepareRuntimeUiForInput(document.rootVisualElement);

        Vector2 uiPoint = new Vector2(50, Screen.height - 50);
        Vector2 worldPoint = new Vector2(Screen.width * 0.75f, Screen.height * 0.25f);
        Assert.IsTrue(blocker.ReadState(uiPoint).BlocksPointer);
        Assert.IsFalse(blocker.ReadState(worldPoint).BlocksPointer);
        StepMouse(worldPoint, false);
        float sizeBeforeUiWheel = _camera.orthographicSize;
        QueueMouse(uiPoint, true, 1);
        QueueMouse(worldPoint, true);
        InputSystem.Update();
        _uiObject.SetActive(false);
        Invoke(blocker, "OnDisable");
        Invoke(_input, "LateUpdate");
        Assert.IsTrue(_input.CurrentFrame.HasWorldPosition);
        Assert.AreEqual(sizeBeforeUiWheel, _camera.orthographicSize);
        Assert.AreEqual(WorldPointerButtonStateEnum.None, _input.CurrentFrame.Primary);
        StepMouse(worldPoint, true);
        Assert.AreEqual(WorldPointerButtonStateEnum.None, _input.CurrentFrame.Primary);
        StepMouse(worldPoint, false);
        StepMouse(worldPoint, true);
        Assert.That(_input.CurrentFrame.Primary.HasFlag(WorldPointerButtonStateEnum.Pressed));

        QueueMouse(worldPoint, false);
        QueueMouse(worldPoint, true);
        InputSystem.Update();
        Invoke(_input, "LateUpdate");
        Assert.That(_input.CurrentFrame.Primary.HasFlag(WorldPointerButtonStateEnum.Pressed));
        Assert.That(_input.CurrentFrame.Primary.HasFlag(WorldPointerButtonStateEnum.Held));
        Assert.That(_input.CurrentFrame.Primary.HasFlag(WorldPointerButtonStateEnum.Released));
        StepMouse(worldPoint, true);
        Assert.AreEqual(WorldPointerButtonStateEnum.Held, _input.CurrentFrame.Primary);

        _uiObject.SetActive(true);
        Invoke(blocker, "OnEnable");
        ConfigureUiRoot(document.rootVisualElement);
        yield return null;
        PrepareRuntimeUiForInput(document.rootVisualElement);
        QueueMouse(uiPoint, true);
        QueueMouse(worldPoint, true);
        InputSystem.Update();
        Invoke(_input, "LateUpdate");
        Assert.AreEqual(WorldPointerButtonStateEnum.Cancelled, _input.CurrentFrame.Primary);
        StepMouse(worldPoint, true);
        Assert.AreEqual(WorldPointerButtonStateEnum.None, _input.CurrentFrame.Primary);
        float size = _camera.orthographicSize;
        StepMouse(uiPoint, false, 1);
        Assert.IsFalse(_input.CurrentFrame.HasWorldPosition);
        Assert.AreEqual(size, _camera.orthographicSize);

        QueueMouse(worldPoint, false, 1);
        QueueMouse(uiPoint, false);
        InputSystem.Update();
        Invoke(_input, "LateUpdate");
        Assert.AreEqual(size, _camera.orthographicSize);

        // 일반 UI는 키보드 이동을 허용하고 텍스트 입력과 모달은 같은 입력 경로 전체를 막는다.
        InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.D, Key.LeftShift));
        InputSystem.Update();
        Vector3 beforeMovement = _camera.transform.position;
        Invoke(_input, "ProcessFrame", 0.25f);
        Assert.AreEqual(beforeMovement.x + 4f, _camera.transform.position.x, 0.0001f);

        var textField = new TextField();
        document.rootVisualElement.Add(textField);
        PrepareRuntimeUiForInput(document.rootVisualElement);
        textField.Focus();
        yield return null;
        Assert.IsTrue(blocker.ReadState(worldPoint).BlocksAllInput);
        beforeMovement = _camera.transform.position;
        Invoke(_input, "ProcessFrame", 0.25f);
        Assert.AreEqual(beforeMovement, _camera.transform.position);

        textField.Blur();
        var modal = new VisualElement();
        modal.AddToClassList(WorldInputUiBlocker.ModalUiClass);
        modal.AddToClassList("input-test-panel");
        document.rootVisualElement.Add(modal);
        PrepareRuntimeUiForInput(document.rootVisualElement);
        yield return null;
        Assert.IsTrue(blocker.ReadState(worldPoint).BlocksAllInput);
        Invoke(_input, "ProcessFrame", 0.25f);
        Assert.AreEqual(beforeMovement, _camera.transform.position);
    }

    [UnityTest]
    public IEnumerator UiPipeline_BlocksUiOriginAndCancelledDragsAndHonorsTextFocusAndHiddenModals()
    {
        _window = ScriptableObject.CreateInstance<WorldInputTestWindow>();
        _window.position = new Rect(100, 100, 500, 350);
        VisualElement root = _window.rootVisualElement;
        root.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>(
            "Assets/Editor/Tests/Fixtures/WorldInputTest.uss"));
        var panel = new VisualElement();
        panel.AddToClassList("input-test-panel");
        panel.AddToClassList(WorldInputUiBlocker.BlockingUiClass);
        var field = new TextField();
        panel.Add(field);
        root.Add(panel);
        var modalParent = new VisualElement();
        modalParent.AddToClassList("input-test-hidden");
        var modal = new VisualElement();
        modal.AddToClassList(WorldInputUiBlocker.ModalUiClass);
        modal.AddToClassList("input-test-panel");
        modalParent.Add(modal);
        root.Add(modalParent);
        _window.Show();
        yield return null;

        Vector2 inside = panel.worldBound.center;
        Vector2 outside = root.worldBound.max - new Vector2(5, 5);
        WorldInputUiState ui = WorldInputUiBlocker.ReadPanelState(root, inside);
        Assert.IsTrue(ui.BlocksPointer);
        Assert.IsFalse(ui.BlocksAllInput);
        var gesture = new WorldPointerGesture();
        Assert.AreEqual(WorldPointerButtonStateEnum.None, gesture.Advance(!ui.BlocksPointer, true, true, false));
        Assert.AreEqual(WorldPointerButtonStateEnum.None, gesture.Advance(true, false, true, false));
        gesture.Advance(true, false, false, true);
        Assert.That(gesture.Advance(true, true, true, false).HasFlag(WorldPointerButtonStateEnum.Pressed));
        Assert.AreEqual(WorldPointerButtonStateEnum.Cancelled, gesture.Advance(!ui.BlocksPointer, false, true, false));
        Assert.AreEqual(WorldPointerButtonStateEnum.None, gesture.Advance(true, false, true, false));

        field.Focus();
        yield return null;
        Assert.IsTrue(WorldInputUiBlocker.ReadPanelState(root, outside).BlocksAllInput);
        field.Blur();
        modalParent.RemoveFromClassList("input-test-hidden");
        yield return null;
        Assert.IsTrue(WorldInputUiBlocker.ReadPanelState(root, outside).BlocksAllInput);
        modalParent.AddToClassList("input-test-hidden");
        panel.SetEnabled(false);
        yield return null;
        Assert.IsFalse(WorldInputUiBlocker.ReadPanelState(root, outside).BlocksAllInput);
        Assert.IsTrue(WorldInputUiBlocker.ReadPanelState(root, inside).BlocksPointer);

        root.Clear();
        var clip = new VisualElement();
        clip.AddToClassList("input-test-clip");
        var clippedRegion = new VisualElement();
        clippedRegion.AddToClassList("input-test-clipped-region");
        clippedRegion.AddToClassList(WorldInputUiBlocker.BlockingUiClass);
        clip.Add(clippedRegion);
        root.Add(clip);
        yield return null;
        Vector2 visiblePoint = clip.worldBound.position + new Vector2(10, 10);
        Vector2 clippedPoint = clip.worldBound.position + new Vector2(150, 10);
        Assert.IsTrue(WorldInputUiBlocker.ReadPanelState(root, visiblePoint).BlocksPointer);
        Assert.IsFalse(WorldInputUiBlocker.ReadPanelState(root, clippedPoint).BlocksPointer);
        clippedRegion.AddToClassList(WorldInputUiBlocker.ModalUiClass);
        Assert.IsTrue(WorldInputUiBlocker.ReadPanelState(root, outside).BlocksAllInput);
        clippedRegion.AddToClassList("input-test-outside-clip");
        yield return null;
        Assert.IsFalse(WorldInputUiBlocker.ReadPanelState(root, outside).BlocksAllInput);
    }

    private void StepMouse(Vector2 position, bool pressed, float scroll = 0)
    {
        QueueMouse(position, pressed, scroll);
        InputSystem.Update();
        Invoke(_input, "LateUpdate");
    }

    private void QueueMouse(Vector2 position, bool pressed, float scroll = 0)
    {
        var state = new MouseState { position = position, scroll = new Vector2(0, scroll) };
        if (pressed)
        {
            state = state.WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left);
        }
        InputSystem.QueueStateEvent(_mouse, state);
    }

    private static void ConfigureUiRoot(VisualElement root)
    {
        root.Clear();
        root.styleSheets.Clear();
        root.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>(
            "Assets/Editor/Tests/Fixtures/WorldInputTest.uss"));
        var panel = new VisualElement();
        panel.AddToClassList("input-test-panel");
        panel.AddToClassList(WorldInputUiBlocker.BlockingUiClass);
        root.Add(panel);
    }

    private static void PrepareRuntimeUiForInput(VisualElement root)
    {
        Assert.IsNotNull(root.panel, "Runtime UI panel must be attached before preparing input.");

        // worldClip은 렌더 데이터에서 읽으므로 EditMode에서도 이 패널의 Repaint 준비를 실행한다.
        MethodInfo prepareRepaint = root.panel.GetType().GetMethod("UpdateForRepaint",
            BindingFlags.Instance | BindingFlags.Public);
        Assert.IsNotNull(prepareRepaint, "This Unity version must expose the panel repaint preparation method.");
        prepareRepaint.Invoke(root.panel, null);

        var pickedElements = new List<VisualElement>();
        Vector2 uiPoint = new Vector2(50, 50);
        root.panel.PickAll(uiPoint, pickedElements);
        VisualElement blockingElement = root.Q<VisualElement>(className: WorldInputUiBlocker.BlockingUiClass);
        Assert.IsNotNull(blockingElement);
        Assert.AreEqual(200f, blockingElement.worldBound.width, 0.001f);
        Assert.AreEqual(100f, blockingElement.worldBound.height, 0.001f);
        Assert.That(pickedElements, Does.Contain(blockingElement), "The fixture UI point must hit its blocking region.");

        PropertyInfo worldClip = typeof(VisualElement).GetProperty("worldClip",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(worldClip);
        Rect clip = (Rect)worldClip.GetValue(blockingElement);
        Assert.IsTrue(math.all(math.isfinite(new float4(clip.x, clip.y, clip.width, clip.height))),
            "Runtime UI clipping must be produced before checking input.");
        Assert.IsTrue(clip.Contains(uiPoint), "The fixture UI point must be inside the produced clip.");
    }

    private static void Invoke(object target, string method, params object[] arguments)
    {
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Invoke(target, arguments);
    }

    public class WorldInputTestWindow : EditorWindow
    {
    }
}
