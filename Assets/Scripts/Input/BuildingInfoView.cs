using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>정보 패널의 표시와 콜백만 소유한다. 반복 갱신은 기존 행을 재사용하고 요청은 Controller에 전달한다.</summary>
[RequireComponent(typeof(BuildingInfoController), typeof(WorldInputController))]
public sealed class BuildingInfoView : MonoBehaviour
{

    private BuildingInfoController _controller;
    private GameObject _rootObject;
    [SerializeField] private PanelSettings _panelSettings;
    private VisualElement _panel;
    private Label _title;
    private Label _details;
    private Label _feedback;
    private ProgressBar _miningProgress;
    private ProgressBar _craftingProgress;
    private ProgressBar _fuelProgress;
    private DropdownField _area;
    private DropdownField _item;
    private IntegerField _quantity;
    private VisualElement _transport;
    private BuildingInfoSettingsView _settings;
    private BuildingInfoInventoryView _generalSlots;
    private BuildingInfoInventoryView _inputSlots;
    private BuildingInfoInventoryView _productSlots;
    private BuildingInfoInventoryView _droneSlots;
    private Label _stationCharging;
    private VisualElement _droneActions;
    private Button _dockDrone;
    private Button _undockDrone;
    private VisualElement _powerGrid;
    private VisualElement _powerMetrics;
    private Label _powerConnection;
    private Label _maximumGeneration;
    private Label _maximumConsumption;
    private Label _currentGeneration;
    private Label _currentConsumption;
    private readonly List<DroneTransportAreaEnum> _areas = new List<DroneTransportAreaEnum>();
    private VisualElement _tasks;
    private Label _tasksHeading;
    private readonly List<WorldUiActionRow> _taskRows = new List<WorldUiActionRow>();
    private bool _resetDraft = true;
    private void OnEnable()
    {
        if (_panelSettings == null)
        {
            Debug.LogError($"{nameof(BuildingInfoView)}: HUD 패널 설정 참조가 필요합니다.", this);
            return;
        }

        _controller = GetComponent<BuildingInfoController>();
        _rootObject = new GameObject("Building information")
        {
            hideFlags = HideFlags.DontSave
        };
        _rootObject.SetActive(false);
        _rootObject.transform.SetParent(transform, false);
        var document = _rootObject.AddComponent<UIDocument>();
        document.panelSettings = _panelSettings;
        document.sortingOrder = 10;
        _rootObject.AddComponent<WorldInputUiBlocker>().Initialize(GetComponent<WorldInputController>());
        _rootObject.SetActive(true);
        var root = document.rootVisualElement;
        root.pickingMode = PickingMode.Ignore;
        root.style.position = Position.Absolute;
        root.style.left = 0;
        root.style.top = 0;
        root.style.right = 0;
        root.style.bottom = 0;
        root.style.unityFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _panel = new VisualElement
        {
            name = "building-info"
        };
        _panel.AddToClassList(WorldInputUiBlocker.BlockingUiClass);
        _panel.style.position = Position.Absolute;
        _panel.style.right = 12;
        _panel.style.top = 12;
        _panel.style.bottom = 112;
        _panel.style.width = 410;
        _panel.style.backgroundColor = new Color(0.035f, 0.055f, 0.08f, 0.97f);
        _panel.style.color = Color.white;
        _panel.style.paddingLeft = 14;
        _panel.style.paddingRight = 14;
        _panel.style.paddingTop = 10;
        _panel.style.paddingBottom = 10;
        root.Add(_panel);
        _title = new Label
        {
            name = "title"
        };
        _title.style.fontSize = 20;
        _panel.Add(_title);
        _panel.Add(new Button(() => GetComponent<WorldSelectionController>().Clear()) { text = "닫기" });
        var content = new ScrollView();
        content.style.flexGrow = 1;
        _panel.Add(content);
        _details = new Label
        {
            name = "details"
        };
        _details.style.whiteSpace = WhiteSpace.Normal;
        content.Add(_details);
        var workColor = new Color(0.09f, 0.46f, 0.38f);
        _miningProgress = AddProgressBar(content, "mining-progress", workColor);
        _craftingProgress = AddProgressBar(content, "crafting-progress", workColor);
        _fuelProgress = AddProgressBar(content, "fuel-progress", new Color(0.57f, 0.31f, 0.12f));
        BuildPowerGrid(content);
        var catalog = new BuildingInfoItemCatalog();
        _settings = new BuildingInfoSettingsView(_controller, catalog);
        content.Add(_settings.Root);
        BuildInventory(content, catalog);
        BuildDroneActions(content);
        BuildTransport(content);
        _tasksHeading = new Label("이 건물의 운반 작업");
        content.Add(_tasksHeading);
        _tasks = new VisualElement();
        content.Add(_tasks);
        _feedback = new Label
        {
            name = "feedback"
        };
        _feedback.style.whiteSpace = WhiteSpace.Normal;
        _panel.Add(_feedback);
        _controller.Changed += Render;
        _controller.SelectionChanged += ResetDraft;
        _resetDraft = true;
        Render();
    }

    private static ProgressBar AddProgressBar(VisualElement parent, string name, Color fill)
    {
        var bar = new ProgressBar { name = name, lowValue = 0f, highValue = 1f };
        bar.style.marginTop = 4;
        bar.style.marginBottom = 5;
        bar.Q<VisualElement>(className: ProgressBar.backgroundUssClassName).style.backgroundColor = new Color(0.13f, 0.18f, 0.23f);
        bar.Q<VisualElement>(className: ProgressBar.progressUssClassName).style.backgroundColor = fill;
        bar.Q<VisualElement>(className: ProgressBar.titleUssClassName).style.color = Color.white;
        parent.Add(bar);
        return bar;
    }

    private static void RenderWorkProgress(ProgressBar bar, float? progress, string title, string state = "")
    {
        bar.style.display = progress.HasValue ? DisplayStyle.Flex : DisplayStyle.None;
        if (!progress.HasValue)
        {
            return;
        }
        bar.value = Mathf.Clamp01(progress.Value);
        string percent = $"{Mathf.RoundToInt(bar.value * 100f)}%";
        bar.title = string.IsNullOrEmpty(state) ? $"{title}  {percent}" : $"{title}  {percent}  /  {state}";
    }

    private void RenderProgress()
    {
        RenderWorkProgress(_miningProgress, _controller.MiningProgress, "채굴 진행");
        RenderWorkProgress(_craftingProgress, _controller.CraftingProgress, "제작 진행", _controller.CraftingStatus);
        var fuel = _controller.FuelProgress;
        _fuelProgress.style.display = fuel.HasValue ? DisplayStyle.Flex : DisplayStyle.None;
        if (fuel.HasValue)
        {
            float capacity = fuel.Value.ActiveFuelCapacity;
            float remaining = fuel.Value.RemainingEnergy;
            _fuelProgress.value = capacity > 0f ? Mathf.Clamp01(remaining / capacity) : 0f;
            _fuelProgress.title = $"연료 잔여 에너지  {remaining:0.##} / {capacity:0.##}";
        }
    }

    private void BuildInventory(VisualElement parent, BuildingInfoItemCatalog catalog)
    {
        _generalSlots = new BuildingInfoInventoryView(catalog, "보관", "storage-slots");
        _inputSlots = new BuildingInfoInventoryView(catalog, "입력", "input-slots");
        _productSlots = new BuildingInfoInventoryView(catalog, "생산품", "product-slots");
        _droneSlots = new BuildingInfoInventoryView(catalog, "드론 보관", "drone-slots",
            _controller.ActivateStoredDrone);
        parent.Add(_generalSlots.Root);
        parent.Add(_inputSlots.Root);
        parent.Add(_productSlots.Root);
        parent.Add(_droneSlots.Root);
        _stationCharging = new Label { name = "station-charging" };
        parent.Add(_stationCharging);
    }

    private void BuildDroneActions(VisualElement parent)
    {
        _droneActions = new VisualElement { name = "selected-drone-actions" };
        _dockDrone = new Button(() => _controller.RequestDroneStation(DroneStationOperation.Dock)) { text = "정박" };
        _undockDrone = new Button(() => _controller.RequestDroneStation(DroneStationOperation.Activate)) { text = "정박 해제" };
        _droneActions.Add(_dockDrone);
        _droneActions.Add(_undockDrone);
        _droneActions.Add(new Button(() => _controller.RequestDroneStation(DroneStationOperation.Store)) { text = "수납" });
        parent.Add(_droneActions);
    }

    private void BuildPowerGrid(VisualElement parent)
    {
        _powerGrid = new VisualElement { name = "power-grid-summary" };
        _powerGrid.style.marginTop = 4;
        _powerGrid.style.marginBottom = 6;
        _powerConnection = new Label { name = "power-grid-status" };
        _powerGrid.Add(_powerConnection);
        _powerMetrics = BuildingInfoItemTile.NewGrid("power-grid-metrics");
        _maximumGeneration = AddPowerMetric(_powerMetrics, "최대 생산량", "power-max-generation");
        _maximumConsumption = AddPowerMetric(_powerMetrics, "최대 소비량", "power-max-consumption");
        _currentGeneration = AddPowerMetric(_powerMetrics, "현재 생산량", "power-current-generation");
        _currentConsumption = AddPowerMetric(_powerMetrics, "현재 소비량", "power-current-consumption");
        _powerGrid.Add(_powerMetrics);
        parent.Add(_powerGrid);
    }

    private static Label AddPowerMetric(VisualElement parent, string caption, string name)
    {
        var box = new VisualElement();
        box.style.width = Length.Percent(48);
        box.style.marginRight = 4;
        box.style.marginBottom = 4;
        box.style.paddingLeft = 6;
        box.style.paddingTop = 4;
        box.style.paddingBottom = 4;
        box.style.backgroundColor = new Color(0.14f, 0.20f, 0.25f);
        box.Add(new Label(caption));
        var value = new Label { name = name };
        value.style.fontSize = 19;
        box.Add(value);
        parent.Add(box);
        return value;
    }

    private void RenderPowerGrid()
    {
        _powerGrid.style.display = _controller.HasPowerGridSection ? DisplayStyle.Flex : DisplayStyle.None;
        if (!_controller.HasPowerGridSection)
        {
            return;
        }

        var grid = _controller.GridDisplay;
        _powerConnection.text = grid.IsConnected ? "연결 전력망  (에너지 / 초)" : "전력망 미연결";
        _powerMetrics.style.display = grid.IsConnected ? DisplayStyle.Flex : DisplayStyle.None;
        _maximumGeneration.text = grid.MaximumGeneration.ToString("0.##");
        _maximumConsumption.text = grid.MaximumConsumption.ToString("0.##");
        _currentGeneration.text = grid.HasPowerSample ? grid.CurrentGeneration.ToString("0.##") : "측정 대기";
        _currentConsumption.text = grid.HasPowerSample ? grid.CurrentConsumption.ToString("0.##") : "측정 대기";
    }

    private void BuildTransport(VisualElement parent)
    {
        _transport = new VisualElement();
        parent.Add(_transport);
        _transport.Add(new Label("일회성 운반 요청"));
        _area = new DropdownField("영역");
        _area.RegisterValueChangedCallback(OnTransportChanged);
        _item = new DropdownField("품목");
        _quantity = new IntegerField("수량")
        {
            value = 1,
            name = "transport-quantity"
        };
        _quantity.RegisterValueChangedCallback(OnTransportChanged);
        _transport.Add(_area);
        _transport.Add(_item);
        _transport.Add(_quantity);
        _transport.Add(new Button(() => SubmitTransport(DroneLogisticsTaskKindEnum.StorageImport)) { text = "반입 요청", name = "import-items" });
        _transport.Add(new Button(() => SubmitTransport(DroneLogisticsTaskKindEnum.StorageExport)) { text = "반출 요청", name = "export-items" });
    }

    private void SubmitTransport(DroneLogisticsTaskKindEnum kind)
    {
        if (_item.index < 0 || _item.index >= _controller.ItemTypes.Count)
        {
            return;
        }

        if (_area.index < 0 || _area.index >= _areas.Count)
        {
            return;
        }

        _controller.RequestTransport(kind, _areas[_area.index], _controller.ItemTypes[_item.index], _quantity.value);
    }

    private void OnTransportChanged(EventBase change)
    {
        UpdateTransportButtons();
    }

    private void UpdateTransportButtons()
    {
        if (_transport == null)
        {
            return;
        }

        bool valid = _area.index >= 0 && _area.index < _areas.Count && _quantity.value > 0;
        bool product = valid && _areas[_area.index] == DroneTransportAreaEnum.Product;
        _transport.Q<Button>("import-items").SetEnabled(valid && !product);
        _transport.Q<Button>("export-items").SetEnabled(valid);
    }

    private void ResetDraft()
    {
        _resetDraft = true;
        _settings.CloseEditors();
        _generalSlots.ResetSelection();
        _droneSlots.ResetSelection();
        Render();
    }

    private void Render()
    {
        if (_panel == null)
        {
            return;
        }

        _panel.style.display = _controller.HasSelection ? DisplayStyle.Flex : DisplayStyle.None;
        if (!_controller.HasSelection)
        {
            return;
        }

        _title.text = _controller.Title;
        _details.text = _controller.Details;
        _feedback.text = _controller.Feedback;
        RenderProgress();
        RenderPowerGrid();
        _settings.Render();
        _generalSlots.Render(_controller.GeneralSlots);
        _inputSlots.Render(_controller.InputSlots);
        _productSlots.Render(_controller.ProductSlots);
        _droneSlots.Render(_controller.DroneSlots);
        _stationCharging.text = _controller.StationChargingSummary;
        _stationCharging.style.display = string.IsNullOrEmpty(_stationCharging.text) ? DisplayStyle.None : DisplayStyle.Flex;
        _droneActions.style.display = _controller.HasDroneStationTarget ? DisplayStyle.Flex : DisplayStyle.None;
        _dockDrone.style.display = _controller.IsSelectedDroneDocked ? DisplayStyle.None : DisplayStyle.Flex;
        _undockDrone.style.display = _controller.IsSelectedDroneDocked ? DisplayStyle.Flex : DisplayStyle.None;
        bool storage = _controller.HasGeneralStorage;
        bool transport = storage || _controller.HasInput || _controller.HasProductOutput;
        _transport.style.display = transport ? DisplayStyle.Flex : DisplayStyle.None;
        bool itemCatalogChanged = _item.choices.Count != _controller.ItemTypes.Count;
        if (!itemCatalogChanged)
        {
            for (int i = 0; i < _item.choices.Count; i++)
            {
                itemCatalogChanged |= _item.choices[i] != BuildingInfoItemCatalog.GetName(_controller.ItemTypes[i]);
            }
        }

        if (itemCatalogChanged)
        {
            var choices = new List<string>();
            foreach (var type in _controller.ItemTypes)
            {
                choices.Add(BuildingInfoItemCatalog.GetName(type));
            }

            _item.choices = choices;
            _item.index = choices.Count > 0 ? 0 : -1;
        }

        if (_resetDraft)
        {
            _resetDraft = false;
            _areas.Clear();
            if (storage)
            {
                _areas.Add(DroneTransportAreaEnum.GeneralStorage);
            }

            if (_controller.HasInput)
            {
                _areas.Add(DroneTransportAreaEnum.Input);
            }

            if (_controller.HasProductOutput)
            {
                _areas.Add(DroneTransportAreaEnum.Product);
            }

            var areaNames = new List<string>();
            foreach (var area in _areas)
            {
                areaNames.Add(area.ToString());
            }

            _area.choices = areaNames;
            _area.index = areaNames.Count > 0 ? 0 : -1;
        }

        UpdateTransportButtons();
        RenderTasks();
    }

    private void RenderTasks()
    {
        var display = _controller.Tasks.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        _tasksHeading.style.display = display;
        _tasks.style.display = display;
        while (_taskRows.Count < _controller.Tasks.Count)
        {
            var row = new WorldUiActionRow();
            _taskRows.Add(row);
            _tasks.Add(row.Root);
        }

        for (int i = 0; i < _taskRows.Count; i++)
        {
            var row = _taskRows[i];
            row.Root.style.display = i < _controller.Tasks.Count ? DisplayStyle.Flex : DisplayStyle.None;
            if (i >= _controller.Tasks.Count)
            {
                continue;
            }

            var data = _controller.Tasks[i];
            row.Label.text = data.Text;
            row.Button.text = "취소";
            row.Button.SetEnabled(data.CanCancel);
            row.Click = () => _controller.CancelTask(data.Task);
        }
    }

    private void OnDisable()
    {
        if (_controller != null)
        {
            _controller.Changed -= Render;
            _controller.SelectionChanged -= ResetDraft;
        }

        if (_area != null)
        {
            _area.UnregisterValueChangedCallback(OnTransportChanged);
        }

        if (_quantity != null)
        {
            _quantity.UnregisterValueChangedCallback(OnTransportChanged);
        }

        if (_rootObject != null)
        {
            _rootObject.SetActive(false);
            UnityObjectLifecycleUtility.DestroyOwned(_rootObject);
        }

        _panel = null;
        _taskRows.Clear();
        _areas.Clear();
    }
}
