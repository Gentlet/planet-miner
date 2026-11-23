using System;
using System.IO;
using System.Threading.Tasks;
using Unity.Entities;
using Unity.Scenes;
using UnityEngine;
using UnityEngine.UIElements;

[DisallowMultipleComponent]
[RequireComponent(typeof(WorldInputController))]
public sealed class GameSessionStartView : MonoBehaviour
{
    [SerializeField] private PanelSettings _panelSettings;
    [SerializeField] private SubScene _prefabSubScene;

    private GameSaveSlot _slot;
    private World _world;
    private Entity _startGate;
    private bool _startNewGame;
    private int _startClickFrame;
    private Task<GameWorldReadResult> _slotCheck;
    private GameSaveStatusEnum _slotStatus;
    private Task<GameWorldReadResult> _readTask;
    private GameLoadWorldPreparation _loadWorld;
    private GameWorldReadResult _loadResult;
    private GameSimulationGroup _captureGroup;
    private Task<GameSaveStatusEnum> _writeTask;
    private string _lastSaveResult = string.Empty;
    private GameObject _documentObject;
    private Button _newGameButton;
    private Button _loadButton;
    private Button _saveButton;
    private Button _cancelOverwriteButton;
    private bool _awaitingOverwrite;
    private Button _refreshButton;
    private Button _quitButton;
    private VisualElement _startMenu;
    private Label _slotDetails;
    private Label _status;

    private void OnEnable()
    {
        BindWorld();
        if (_panelSettings == null)
        {
            Debug.LogError("시작 화면에 HUD 패널 설정이 필요합니다.", this);
            return;
        }
        if (_prefabSubScene == null)
        {
            Debug.LogError("시작 화면에 프리팹 SubScene 참조가 필요합니다.", this);
            return;
        }

        _slot ??= new GameSaveSlot(Application.persistentDataPath);
        CreateMenu();
        ShowBoundSession();
    }

    private void Update()
    {
        if (_world == null)
        {
            BindWorld();
            if (_documentObject != null)
            {
                ShowBoundSession();
            }
        }
        if (_documentObject == null)
        {
            return;
        }

        // 버튼 클릭 프레임까지 모달을 유지해 같은 입력이 건설/선택으로 전달되지 않게 한다.
        if (_startNewGame && _startClickFrame != Time.frameCount)
        {
            ReleaseStartGate();
            ShowSaveControls(string.Empty);
            return;
        }

        if (_readTask != null)
        {
            CompleteRead();
        }
        if (_loadWorld != null)
        {
            AdvanceLoadWorld();
        }
        if (_slotCheck != null)
        {
            CompleteSlotCheck();
        }
        if (_writeTask != null)
        {
            CompleteWrite();
        }
        if (_captureGroup != null)
        {
            if (SimulationFailureUtility.HasFatalError(_world.EntityManager))
            {
                StopCapture();
                ShowSaveResult("게임 틱이 중단되어 저장할 수 없습니다.");
            }
        }
    }

    private void BindWorld()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (world != null)
        {
            if (!world.IsCreated)
            {
                world = null;
            }
        }
        if (ReferenceEquals(_world, world))
        {
            return;
        }
        // 시작 대기와 쓰기 완료 표시는 각각 바인딩한 World의 수명을 따른다.
        StopCapture();
        ReleaseStartGate();
        _world = world;
        _writeTask = null;
        _awaitingOverwrite = false;
        _lastSaveResult = string.Empty;
        if (world == null)
        {
            return;
        }
        using var started = world.EntityManager.CreateEntityQuery(typeof(GameBootstrapState));
        if (started.IsEmptyIgnoreFilter)
        {
            _startGate = world.EntityManager.CreateEntity(typeof(GameSessionStartPending));
        }
    }

    private void ShowBoundSession()
    {
        if (_world == null)
        {
            return;
        }
        if (_startGate != Entity.Null)
        {
            CheckSlot();
            return;
        }
        ShowSaveControls(_lastSaveResult);
    }

    private void StartNewGame()
    {
        _startNewGame = true;
        _startClickFrame = Time.frameCount;
        RefreshStartControls();
    }

    private void CheckSlot()
    {
        _slotDetails.text = "저장 슬롯 확인 중";
        _status.text = string.Empty;
        _slotCheck = _slot.ReadWorldAsync();
        RefreshStartControls();
    }

    private void CompleteSlotCheck()
    {
        if (!_slotCheck.IsCompleted)
        {
            return;
        }
        var check = _slotCheck;
        _slotCheck = null;
        try
        {
            ShowSlotDetails(check.GetAwaiter().GetResult());
        }
        catch (Exception error)
        {
            Debug.LogException(error, this);
            _slotStatus = GameSaveStatusEnum.IoError;
            _slotDetails.text = SlotMessage(_slotStatus);
        }
        RefreshStartControls();
    }

    private void ShowSlotDetails(in GameWorldReadResult result)
    {
        _slotStatus = result.Status;
        if (result.Status == GameSaveStatusEnum.Success)
        {
            string saved = result.SavedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            _slotDetails.text = $"마지막 저장 / {saved}";
            return;
        }
        _slotDetails.text = SlotMessage(result.Status);
    }

    private static string SlotMessage(GameSaveStatusEnum status) => status switch
    {
        GameSaveStatusEnum.Missing => "저장된 게임이 없습니다.",
        GameSaveStatusEnum.Corrupt => "저장 파일이 손상되었습니다.",
        GameSaveStatusEnum.Incompatible => "이 버전에서 열 수 없는 저장 파일입니다.",
        _ => "저장 파일을 읽을 수 없습니다."
    };

    private void ReadSave()
    {
        _status.text = "저장 파일을 확인하고 있습니다.";
        _readTask = _slot.ReadWorldAsync();
        RefreshStartControls();
    }

    private void CompleteRead()
    {
        Task<GameWorldReadResult> readTask = _readTask;
        if (!readTask.IsCompleted)
        {
            return;
        }
        _readTask = null;
        try
        {
            GameWorldReadResult result = readTask.GetAwaiter().GetResult();
            if (result.Status != GameSaveStatusEnum.Success)
            {
                ShowSlotDetails(result);
                _status.text = string.Empty;
                RefreshStartControls();
                return;
            }

            _loadResult = result;
            ShowSlotDetails(result);
            _status.text = "저장 파일을 확인했습니다. 복원용 월드를 준비합니다.";
            _loadWorld = new GameLoadWorldPreparation(_prefabSubScene.SceneGUID);
        }
        catch (Exception error)
        {
            Debug.LogException(error, this);
            ReportLoadFailure("저장 파일을 불러올 수 없습니다.");
        }
    }

    private void AdvanceLoadWorld()
    {
        World previous = _world;
        try
        {
            _loadWorld.AdvanceInitialization();
            if (_loadWorld.HasFailed)
            {
                ReportLoadFailure("복원용 월드의 프리팹을 준비할 수 없습니다.");
                return;
            }
            if (!_loadWorld.IsReady)
            {
                return;
            }

            _world = _loadWorld.RestoreAndActivate(_loadResult.Snapshot, previous);
            _slot.ConfirmRestored(_loadResult);
        }
        catch (InvalidDataException error)
        {
            ReportLoadFailure(error.Message);
            return;
        }
        catch (Exception error)
        {
            Debug.LogException(error, this);
            ReportLoadFailure("월드를 불러올 수 없습니다.");
            return;
        }

        _startGate = Entity.Null;
        ReleaseLoadWorld();
        ShowSaveControls("슬롯 01에서 이어갑니다.");
        try
        {
            previous.Dispose();
        }
        catch (Exception error)
        {
            Debug.LogException(error, this);
        }
    }

    private void ReportLoadFailure(string message)
    {
        ReleaseLoadWorld();
        _status.text = message;
        RefreshStartControls();
    }

    private void ReleaseLoadWorld()
    {
        GameLoadWorldPreparation loadWorld = _loadWorld;
        _loadWorld = null;
        _loadResult = default;
        loadWorld?.Dispose();
    }

    private void RequestSave()
    {
        if (_captureGroup != null || _writeTask != null)
        {
            return;
        }
        if (SimulationFailureUtility.HasFatalError(_world.EntityManager))
        {
            ShowSaveResult("게임 틱이 중단되어 저장할 수 없습니다.");
            return;
        }
        _captureGroup = _world.GetExistingSystemManaged<GameSimulationGroup>();
        _captureGroup.TickCompleted += CaptureAndWrite;
        RefreshSaveControls();
    }

    private void CaptureAndWrite(EntityManager manager)
    {
        StopCapture();
        try
        {
            GameWorldSnapshot snapshot = GameWorldCapture.Capture(manager);
            _writeTask = _awaitingOverwrite ? _slot.WriteConfirmedAsync(snapshot) : _slot.WriteAsync(snapshot);
            _awaitingOverwrite = false;
            RefreshSaveControls();
        }
        catch (InvalidOperationException error)
        {
            ShowSaveResult(error.Message);
        }
        catch (InvalidDataException error)
        {
            ShowSaveResult(error.Message);
        }
        catch (Exception error)
        {
            Debug.LogException(error, this);
            ShowSaveResult("월드 상태를 저장할 수 없습니다.");
        }
    }

    private void StopCapture()
    {
        if (_captureGroup == null)
        {
            return;
        }
        _captureGroup.TickCompleted -= CaptureAndWrite;
        _captureGroup = null;
    }

    private void CompleteWrite()
    {
        Task<GameSaveStatusEnum> task = _writeTask;
        if (!task.IsCompleted)
        {
            return;
        }
        _writeTask = null;
        try
        {
            GameSaveStatusEnum status = task.GetAwaiter().GetResult();
            _awaitingOverwrite = status == GameSaveStatusEnum.ConfirmOverwrite;
            ShowSaveResult(status switch
            {
                GameSaveStatusEnum.Success => "슬롯 01에 저장했습니다.",
                GameSaveStatusEnum.ConfirmOverwrite => "슬롯 01에 기존 진행이 있습니다. 덮어쓰면 이전 진행을 복구할 수 없습니다.",
                GameSaveStatusEnum.Incompatible => "다른 버전의 기존 저장 파일을 보존했습니다.",
                _ => "저장 파일을 쓸 수 없습니다."
            });
        }
        catch (Exception error)
        {
            Debug.LogException(error, this);
            ShowSaveResult("저장 파일을 쓸 수 없습니다.");
        }
    }

    private void ShowSaveResult(string message)
    {
        _lastSaveResult = message;
        RefreshSaveControls();
    }

    private void RefreshSaveControls()
    {
        if (_documentObject == null)
        {
            return;
        }
        bool idle = _captureGroup == null && _writeTask == null;
        _saveButton.text = _awaitingOverwrite ? "기존 슬롯 덮어쓰기" : "저장";
        _cancelOverwriteButton.style.display = _awaitingOverwrite && idle ? DisplayStyle.Flex : DisplayStyle.None;
        _saveButton.SetEnabled(idle);
        _quitButton.SetEnabled(idle);
        if (_captureGroup != null)
        {
            _status.text = "저장할 게임 틱을 기다리고 있습니다.";
        }
        else if (_writeTask != null)
        {
            _status.text = "저장하고 있습니다.";
        }
        else
        {
            _status.text = _lastSaveResult;
        }
    }

    private void ShowSaveControls(string message)
    {
        _slotCheck = null;
        var root = _startMenu.parent;
        root.RemoveFromClassList(WorldInputUiBlocker.ModalUiClass);
        root.pickingMode = PickingMode.Ignore;
        root.style.backgroundColor = Color.clear;
        root.style.justifyContent = Justify.FlexStart;
        root.style.alignItems = Align.Center;
        _startMenu.Clear();
        _startMenu.pickingMode = PickingMode.Ignore;
        _startMenu.style.marginTop = 12;
        var actions = CreateButtonRow();
        _startMenu.Add(actions);
        _saveButton = AddButton(actions, "저장", "save-game", RequestSave);
        _saveButton.AddToClassList(WorldInputUiBlocker.BlockingUiClass);
        _cancelOverwriteButton = AddButton(actions, "취소", "cancel-overwrite", () =>
        {
            _awaitingOverwrite = false;
            ShowSaveResult("기존 저장 파일을 보존했습니다.");
        });
        _cancelOverwriteButton.AddToClassList(WorldInputUiBlocker.BlockingUiClass);
        _quitButton = AddButton(actions, "종료", "quit-game", QuitGame);
        _quitButton.AddToClassList(WorldInputUiBlocker.BlockingUiClass);
        _status = new Label { name = "save-status", pickingMode = PickingMode.Ignore };
        _status.style.whiteSpace = WhiteSpace.Normal;
        _status.style.unityTextAlign = TextAnchor.MiddleCenter;
        _startMenu.Add(_status);
        ShowSaveResult(message);
    }

    private void RefreshStartControls()
    {
        bool readingSave = _readTask != null;
        bool restoringWorld = _loadWorld != null;
        bool checkingSlot = _slotCheck != null;
        bool worldReady = _world != null;
        bool starting = _startNewGame || readingSave || restoringWorld;
        bool canStart = !starting && worldReady;
        _newGameButton.SetEnabled(canStart);
        _loadButton.SetEnabled(canStart && !checkingSlot && _slotStatus == GameSaveStatusEnum.Success);
        _refreshButton.SetEnabled(!starting && !checkingSlot);
        _quitButton.SetEnabled(!starting);
    }

    private void CreateMenu()
    {
        _documentObject = new GameObject("Game session start") { hideFlags = HideFlags.DontSave };
        _documentObject.SetActive(false);
        _documentObject.transform.SetParent(transform, false);
        var document = _documentObject.AddComponent<UIDocument>();
        document.panelSettings = _panelSettings;
        document.sortingOrder = 30;
        _documentObject.AddComponent<WorldInputUiBlocker>().Initialize(GetComponent<WorldInputController>());
        _documentObject.SetActive(true);

        var root = document.rootVisualElement;
        root.AddToClassList(WorldInputUiBlocker.ModalUiClass);
        root.style.flexGrow = 1;
        root.style.alignItems = Align.Center;
        root.style.justifyContent = Justify.Center;
        root.style.backgroundColor = new Color(0.035f, 0.055f, 0.08f, 0.96f);
        root.style.color = Color.white;
        root.style.unityFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var menu = new VisualElement();
        _startMenu = menu;
        menu.style.width = 400;
        menu.style.maxWidth = new Length(90, LengthUnit.Percent);
        root.Add(menu);

        var title = new Label("Planet Miner");
        title.style.fontSize = 34;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.marginBottom = 20;
        menu.Add(title);

        var slot = new VisualElement();
        slot.style.backgroundColor = new Color(0.09f, 0.14f, 0.2f);
        slot.style.paddingTop = 12;
        slot.style.paddingBottom = 12;
        slot.style.paddingLeft = 14;
        slot.style.paddingRight = 14;
        slot.style.marginBottom = 12;
        menu.Add(slot);

        var slotName = new Label("슬롯 01");
        slotName.style.unityFontStyleAndWeight = FontStyle.Bold;
        slot.Add(slotName);
        _slotDetails = new Label { name = "slot-details" };
        _slotDetails.style.whiteSpace = WhiteSpace.Normal;
        _slotDetails.style.marginTop = 4;
        _slotDetails.style.marginBottom = 8;
        slot.Add(_slotDetails);
        _loadButton = AddButton(slot, "이어하기", "continue-game", ReadSave);

        _newGameButton = AddButton(menu, "새 게임", "new-game", StartNewGame);
        var actions = CreateButtonRow();
        menu.Add(actions);
        _refreshButton = AddButton(actions, "슬롯 새로고침", "refresh-slot", CheckSlot);
        _quitButton = AddButton(actions, "종료", "quit-game", QuitGame);
        _status = new Label { name = "save-status" };
        _status.style.whiteSpace = WhiteSpace.Normal;
        _status.style.minHeight = 42;
        _status.style.marginTop = 8;
        menu.Add(_status);
        RefreshStartControls();
    }

    private static VisualElement CreateButtonRow()
    {
        var row = new VisualElement { pickingMode = PickingMode.Ignore };
        row.style.flexDirection = FlexDirection.Row;
        row.style.justifyContent = Justify.Center;
        return row;
    }

    private static Button AddButton(VisualElement menu, string text, string name, Action clicked)
    {
        var button = new Button(clicked) { name = name, text = text };
        button.style.height = 44;
        button.style.minWidth = 84;
        button.style.marginBottom = 8;
        button.style.fontSize = 16;
        button.style.backgroundColor = new Color(0.13f, 0.22f, 0.3f);
        button.style.color = Color.white;
        menu.Add(button);
        return button;
    }

    private static void QuitGame() => Application.Quit();

    private void OnDisable()
    {
        // 일시 비활성화는 시작 대기를 해제하지 않는다. 접수된 저장도 같은 World의 완료 틱에서 계속 진행한다.
        _readTask = null;
        _slotCheck = null;
        _slotStatus = GameSaveStatusEnum.Missing;
        _startNewGame = false;
        ReleaseLoadWorld();
        if (_documentObject != null)
        {
            _documentObject.SetActive(false);
            UnityObjectLifecycleUtility.DestroyOwned(_documentObject);
            _documentObject = null;
        }
    }

    private void OnDestroy()
    {
        StopCapture();
        ReleaseStartGate();
    }

    private void ReleaseStartGate()
    {
        World world = _world;
        Entity gate = _startGate;
        _startGate = Entity.Null;
        _startNewGame = false;
        if (world == null)
        {
            return;
        }
        if (!world.IsCreated)
        {
            return;
        }
        if (gate != Entity.Null)
        {
            world.EntityManager.DestroyEntity(gate);
        }
    }
}
